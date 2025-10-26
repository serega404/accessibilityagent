using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AccessibilityAgent.Checks;
using Microsoft.AspNetCore.SignalR.Client;

namespace AccessibilityAgent.Agent;

internal sealed class SignalRAgentRunner : IAsyncDisposable
{
    private const string AgentRegisterMethod = "Register";
    private const string AgentHeartbeatMethod = "Heartbeat";
    private const string JobAcceptedMethod = "JobAccepted";
    private const string JobResultMethod = "JobResult";
    private const string IssueTokenMethod = "IssueToken";
    private const string JobRequestEvent = "job:request";

    private static readonly string[] SupportedCapabilities = new[] { "ping", "dns", "tcp", "udp", "http", "check" };

    private readonly AgentOptions _options;
    private readonly AgentJobExecutor _executor;
    private readonly HubConnection _hub;

    private readonly SemaphoreSlim _jobLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly string _agentVersion = typeof(SignalRAgentRunner).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private CancellationToken _runToken = CancellationToken.None;
    private Task? _heartbeatTask;
    private bool _disposed;

    public SignalRAgentRunner(AgentOptions options, AgentJobExecutor? executor = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _executor = executor ?? new AgentJobExecutor(new NetworkCheckRunner());

        var hubUrl = BuildHubUrl(options.ServerUri, "/agentHub", new Dictionary<string, string>
        {
            ["token"] = options.Token,
            ["agent"] = options.AgentName
        });

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new ProgressiveRetryPolicy(options.ReconnectDelay, options.ReconnectDelayMax))
            .Build();

        _hub.On<JsonElement>(JobRequestEvent, async element =>
        {
            await OnJobRequestedAsync(element).ConfigureAwait(false);
        });
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        EnsureNotDisposed();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        _runToken = linkedCts.Token;

        _heartbeatTask = StartHeartbeatLoop(_runToken);

        try
        {
            LogInfo($"Connecting to {_options.ServerUri} as '{_options.AgentName}' (SignalR)...");
            await _hub.StartAsync(_runToken).ConfigureAwait(false);
            await OnConnectedAsync().ConfigureAwait(false);
            await Task.Delay(Timeout.Infinite, _runToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_runToken.IsCancellationRequested)
        {
            // shutting down
        }
        finally
        {
            linkedCts.Cancel();
            if (_heartbeatTask is not null)
            {
                try { await _heartbeatTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
            try { await _hub.StopAsync().ConfigureAwait(false); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        if (_heartbeatTask is not null)
        {
            try { await _heartbeatTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        try { await _hub.StopAsync().ConfigureAwait(false); } catch { }
        await _hub.DisposeAsync();
        _jobLock.Dispose();
        _cts.Dispose();
    }

    private async Task OnConnectedAsync()
    {
        LogInfo("Connected.");
        await TryIssuePersonalTokenIfNeededAsync().ConfigureAwait(false);
        await SendRegistrationAsync().ConfigureAwait(false);
    }

    private async Task TryIssuePersonalTokenIfNeededAsync()
    {
        if (!_options.AutoIssuePersonalToken) return;
        try
        {
            var resp = await _hub.InvokeAsync<JsonElement>(IssueTokenMethod, new Dictionary<string, object?>
            {
                ["agent"] = _options.AgentName
            });
            if (resp.ValueKind == JsonValueKind.Object && resp.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                if (resp.TryGetProperty("token", out var tok) && tok.ValueKind == JsonValueKind.String)
                {
                    var issued = tok.GetString();
                    if (!string.IsNullOrWhiteSpace(issued) && !string.Equals(issued, _options.Token, StringComparison.Ordinal))
                    {
                        var creds = new AgentCredentials
                        {
                            AgentName = _options.AgentName,
                            ServerUrl = _options.ServerUri.ToString(),
                            Token = issued!,
                            IssuedAt = DateTimeOffset.UtcNow,
                            Metadata = _options.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
                        };
                        if (AgentCredentialStore.Save(_options.CredentialFilePath, creds))
                        {
                            LogInfo($"Personal token saved to '{_options.CredentialFilePath}'. It will be used on next start.");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogWarning($"Issuing personal token failed: {ex.Message}");
        }
    }

    private async Task OnJobRequestedAsync(JsonElement element)
    {
        if (_runToken.IsCancellationRequested) return;
        var job = TryParseJob(element);
        if (job is null) return;
        LogInfo($"Job received: id={job.Id}, type={job.Type}");
        await ProcessJobAsync(job, _runToken).ConfigureAwait(false);
    }

    private async Task ProcessJobAsync(AgentJobRequest job, CancellationToken cancellationToken)
    {
        AgentJobExecutionResult? result = null;
        await _jobLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EmitJobAcceptedAsync(job, cancellationToken).ConfigureAwait(false);
            result = await _executor.ExecuteAsync(job, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogWarning($"Job '{job.Id}' cancelled.");
            if (!_cts.IsCancellationRequested)
            {
                result = new AgentJobExecutionResult { JobId = job.Id, Success = false, Error = "Job cancelled" };
            }
        }
        catch (Exception ex)
        {
            LogError($"Job '{job.Id}' failed: {ex.Message}", ex);
            result = new AgentJobExecutionResult
            {
                JobId = job.Id,
                Success = false,
                Error = ex.Message,
                ErrorDetails = ex.ToString()
            };
        }
        finally { _jobLock.Release(); }

        if (result is null) return;
        await EmitJobResultAsync(job, result).ConfigureAwait(false);
        LogInfo($"Job '{job.Id}' completed, success={result.Success}");
    }

    private async Task EmitJobAcceptedAsync(AgentJobRequest job, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["jobId"] = job.Id,
            ["agent"] = _options.AgentName,
            ["type"] = job.Type,
            ["receivedAt"] = DateTimeOffset.UtcNow,
            ["metadata"] = job.Metadata
        };
        await SafeInvokeAsync(JobAcceptedMethod, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task EmitJobResultAsync(AgentJobRequest job, AgentJobExecutionResult result)
    {
        var checksPayload = result.Checks.Select(PrepareCheckPayload).ToArray();
        var payload = new Dictionary<string, object?>
        {
            ["jobId"] = result.JobId,
            ["agent"] = _options.AgentName,
            ["success"] = result.Success,
            ["error"] = result.Error,
            ["errorDetails"] = result.ErrorDetails,
            ["completedAt"] = DateTimeOffset.UtcNow,
            ["checks"] = checksPayload,
            ["metadata"] = job.Metadata
        };
        await SafeInvokeAsync(JobResultMethod, payload, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task SendRegistrationAsync()
    {
        var payload = new Dictionary<string, object?>
        {
            ["agent"] = _options.AgentName,
            ["version"] = _agentVersion,
            ["capabilities"] = SupportedCapabilities,
            ["connectedAt"] = DateTimeOffset.UtcNow,
            ["metadata"] = _options.Metadata.Count == 0 ? null : _options.Metadata,
            ["runtime"] = new Dictionary<string, object?>
            {
                ["framework"] = RuntimeInformation.FrameworkDescription,
                ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["startedAt"] = _startedAt
            }
        };
        await SafeInvokeAsync(AgentRegisterMethod, payload, CancellationToken.None).ConfigureAwait(false);
    }

    private Task? StartHeartbeatLoop(CancellationToken cancellationToken)
    {
        if (_options.HeartbeatInterval <= TimeSpan.Zero) return null;
        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
                    await SafeInvokeAsync(AgentHeartbeatMethod, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { LogWarning($"Heartbeat failed: {ex.Message}"); }
            }
        }, cancellationToken);
    }

    private async Task SafeInvokeAsync(string methodName, object payload, CancellationToken cancellationToken)
    {
        try { await _hub.InvokeAsync(methodName, payload, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { LogWarning($"Invoke '{methodName}' failed: {ex.Message}"); }
    }

    private async Task SafeInvokeAsync(string methodName, CancellationToken cancellationToken)
    {
        try { await _hub.InvokeAsync(methodName, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { LogWarning($"Invoke '{methodName}' failed: {ex.Message}"); }
    }

    private AgentJobRequest? TryParseJob(JsonElement element)
    {
        try
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                LogWarning("Job payload is not a JSON object.");
                return null;
            }
            var node = JsonNode.Parse(element.GetRawText()) as JsonObject;
            if (node is null)
            {
                LogWarning("Unable to parse job payload.");
                return null;
            }
            if (node.TryGetPropertyValue("job", out var jobNode) && jobNode is JsonObject jobObject)
            {
                node = jobObject;
            }
            var id = ExtractString(node, "id", "jobId");
            var type = ExtractString(node, "type", "command");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type))
            {
                LogWarning("Job payload missing id or type.");
                return null;
            }
            var payloadNode = node.TryGetPropertyValue("payload", out var payload) ? payload?.DeepClone() : null;
            var metadataNode = node.TryGetPropertyValue("metadata", out var metadata) ? metadata?.DeepClone() : null;
            return new AgentJobRequest { Id = id, Type = type, Payload = payloadNode, Metadata = metadataNode };
        }
        catch (Exception ex)
        {
            LogError("Failed to parse job message.", ex);
            return null;
        }
    }

    private static Dictionary<string, object?> PrepareCheckPayload(CheckResult result) => new()
    {
        ["check"] = result.Check,
        ["success"] = result.Success,
        ["message"] = result.Message,
        ["durationMs"] = result.DurationMs,
        ["data"] = result.Data
    };

    private static string? ExtractString(JsonObject obj, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!obj.TryGetPropertyValue(name, out var node) || node is null) continue;
            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<string>(out var strValue)) return strValue;
                if (jsonValue.TryGetValue<long>(out var longValue)) return longValue.ToString(CultureInfo.InvariantCulture);
                if (jsonValue.TryGetValue<double>(out var doubleValue)) return doubleValue.ToString(CultureInfo.InvariantCulture);
            }
        }
        return null;
    }

    private static void LogInfo(string message) => Console.WriteLine($"[{DateTimeOffset.UtcNow:O}] INFO  {message}");
    private static void LogWarning(string message) => Console.WriteLine($"[{DateTimeOffset.UtcNow:O}] WARN  {message}");
    private static void LogError(string message, Exception? exception = null)
    {
        if (exception is null) Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:O}] ERROR {message}");
        else Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:O}] ERROR {message}: {exception}");
    }

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SignalRAgentRunner));
    }

    private static string BuildHubUrl(Uri baseUri, string path, Dictionary<string, string> query)
    {
        var builder = new UriBuilder(baseUri);
        var basePath = builder.Path?.TrimEnd('/') ?? string.Empty;
        var addPath = path.StartsWith('/') ? path : "/" + path;
        builder.Path = basePath + addPath;
        var q = string.Join('&', query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        builder.Query = q;
        return builder.ToString();
    }

    private sealed class ProgressiveRetryPolicy : IRetryPolicy
    {
        private readonly TimeSpan _min;
        private readonly TimeSpan _max;
        public ProgressiveRetryPolicy(TimeSpan min, TimeSpan max)
        {
            _min = min <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : min;
            _max = max < _min ? _min : max;
        }
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            var delay = TimeSpan.FromMilliseconds(Math.Min(_max.TotalMilliseconds, _min.TotalMilliseconds * (retryContext.PreviousRetryCount + 1)));
            return delay;
        }
    }
}
