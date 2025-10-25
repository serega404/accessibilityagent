using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AccessibilityAgent.Checks;
using SocketIOClient;
using SocketIOClient.Transport;

namespace AccessibilityAgent.Agent;

internal sealed class AgentRunner : IAsyncDisposable
/// <summary>
/// Класс AgentRunner управляет жизненным циклом агента доступности, обеспечивая подключение к серверу,
/// обработку входящих заданий, отправку результатов и поддержание связи через heartbeat.
/// Использует Socket.IO для взаимодействия с сервером и поддерживает асинхронную обработку заданий.
/// </summary>
{
    private const string AgentRegisterEvent = "agent:register";
    private const string AgentHeartbeatEvent = "agent:heartbeat";
    private const string JobRequestEvent = "job:request";
    private const string JobAcceptedEvent = "job:accepted";
    private const string JobResultEvent = "job:result";

    private static readonly string[] SupportedCapabilities = ["ping", "dns", "tcp", "udp", "http", "check"];

    private readonly AgentOptions _options;
    private readonly SocketIOClient.SocketIO _socket;
    private readonly AgentJobExecutor _executor;
    private readonly SemaphoreSlim _jobLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly string _agentVersion = typeof(AgentRunner).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private CancellationToken _runToken = CancellationToken.None;
    private Task? _heartbeatTask;
    private bool _disposed;

    public AgentRunner(AgentOptions options, AgentJobExecutor? executor = null)
    /// <summary>
    /// Создаёт экземпляр AgentRunner с указанными параметрами и инициализирует соединение Socket.IO.
    /// </summary>
    /// <param name="options">Параметры агента, включая токен, имя, настройки подключения и метаданные.</param>
    /// <param name="executor">Необязательный кастомный исполнитель заданий. Если не указан, используется стандартный.</param>
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _executor = executor ?? new AgentJobExecutor(new NetworkCheckRunner());

        var socketOptions = new SocketIOOptions
        {
            Transport = TransportProtocol.WebSocket,
            AutoUpgrade = false,
            Reconnection = true,
            ReconnectionDelay = (int)Math.Ceiling(options.ReconnectDelay.TotalMilliseconds),
            ReconnectionDelayMax = (int)Math.Ceiling(options.ReconnectDelayMax.TotalMilliseconds),
            ConnectionTimeout = TimeSpan.FromSeconds(20),
            Path = "/socket.io/",
            EIO = SocketIO.Core.EngineIO.V3
        };

        if (options.MaxReconnectAttempts.HasValue)
        {
            socketOptions.ReconnectionAttempts = options.MaxReconnectAttempts.Value;
        }

        // Для Socket.IO v4 передаем и в query, и в auth для совместимости с сервером
        socketOptions.Query = new List<KeyValuePair<string, string>>
        {
            new("token", options.Token),
            new("agent", options.AgentName)
        };
        // Для EIO v3 достаточно query, auth не используется
        _socket = new SocketIOClient.SocketIO(options.ServerUri, socketOptions);

        _socket.OnDisconnected += (_, reason) =>
        {
            LogWarning($"Socket disconnected: {reason}");
            // Try reconnect immediately
            if (!_cts.IsCancellationRequested)
                _ = _socket.ConnectAsync();
        };
        _socket.On(JobRequestEvent, async response => {
            Console.WriteLine("[DEBUG] job:request event received");
            await OnJobRequestedAsync(response).ConfigureAwait(false);
        });

        _socket.OnConnected += async (_, _) =>
        {
            Console.WriteLine("[C# debug] OnConnected");
            await OnConnectedAsync();
            // На всякий случай повесим обработчик ACK и попросим сервер подтвердить регистрацию
            try
            {
                await _socket.EmitAsync("agent:register", new Dictionary<string, object?>
                {
                    ["agent"] = _options.AgentName,
                    ["capabilities"] = SupportedCapabilities,
                    ["metadata"] = _options.Metadata.Count == 0 ? null : _options.Metadata,
                    ["runtime"] = new Dictionary<string, object?>
                    {
                        ["framework"] = RuntimeInformation.FrameworkDescription,
                        ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString()
                    }
                });
                Console.WriteLine("[C# debug] agent:register emitted");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[C# debug] agent:register emit failed: {ex.Message}");
            }
        };

        _socket.OnError += (_, ex) =>
        {
            Console.WriteLine($"[C# debug] OnError: {ex}");
            OnError(ex?.ToString() ?? "null");
        };

        _socket.OnReconnectAttempt += (_, attempt) =>
        {
            Console.WriteLine($"[C# debug] OnReconnectAttempt #{attempt}");
        };

        _socket.OnAny((eventName, response) => {
            Console.WriteLine($"[DEBUG] Event received: {eventName}");
        });
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    /// <summary>
    /// Запускает основной цикл агента: подключение к серверу, ожидание и обработка заданий, поддержка heartbeat.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для завершения работы агента.</param>
    {
        EnsureNotDisposed();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        _runToken = linkedCts.Token;

        _heartbeatTask = StartHeartbeatLoop(_runToken);

        try
        {
            LogInfo($"Connecting to {_options.ServerUri} as '{_options.AgentName}'...");
            await _socket.ConnectAsync().ConfigureAwait(false);
            await Task.Delay(Timeout.Infinite, _runToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_runToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
        finally
        {
            linkedCts.Cancel();

            if (_heartbeatTask is not null)
            {
                try
                {
                    await _heartbeatTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation during shutdown.
                }
            }

            if (_socket.Connected)
            {
                try
                {
                    await _socket.DisconnectAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogWarning($"Disconnect failed: {ex.Message}");
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    /// <summary>
    /// Асинхронно освобождает ресурсы, завершает соединение и отменяет все фоновые задачи.
    /// </summary>
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        if (_heartbeatTask is not null)
        {
            try
            {
                await _heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during disposal.
            }
        }

        if (_socket.Connected)
        {
            try
            {
                await _socket.DisconnectAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore errors on dispose.
            }
        }

        _socket.Dispose();
        _jobLock.Dispose();
        _cts.Dispose();
    }

    private async Task OnConnectedAsync()
    {
        LogInfo("Socket connected.");
        await SendRegistrationAsync().ConfigureAwait(false);
    }

    private void OnError(string error)
    {
        LogError($"Socket error: {error}");
    }

    private async Task OnJobRequestedAsync(SocketIOResponse response)
    {
        if (_runToken.IsCancellationRequested)
        {
            return;
        }

        var job = TryParseJob(response);
        if (job is null)
        {
            return;
        }

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
                result = new AgentJobExecutionResult
                {
                    JobId = job.Id,
                    Success = false,
                    Error = "Job cancelled"
                };
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
        finally
        {
            _jobLock.Release();
        }

        if (result is null)
        {
            return;
        }

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

        await SafeEmitAsync(JobAcceptedEvent, payload, cancellationToken).ConfigureAwait(false);
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

        await SafeEmitAsync(JobResultEvent, payload, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task SendRegistrationAsync()
    {
        var runtime = new Dictionary<string, string>
        {
            ["os"] = Environment.OSVersion.ToString(),
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString()
        };

        var payload = new Dictionary<string, object?>
        {
            ["agent"] = _options.AgentName,
            ["version"] = _agentVersion,
            ["capabilities"] = SupportedCapabilities,
            ["connectedAt"] = DateTimeOffset.UtcNow,
            ["metadata"] = _options.Metadata.Count == 0 ? null : _options.Metadata,
            ["runtime"] = runtime
        };

        await SafeEmitAsync(AgentRegisterEvent, payload, CancellationToken.None).ConfigureAwait(false);
    }

    private Task? StartHeartbeatLoop(CancellationToken cancellationToken)
    {
        if (_options.HeartbeatInterval <= TimeSpan.Zero)
        {
            return null;
        }

        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
                    await EmitHeartbeatAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogWarning($"Heartbeat failed: {ex.Message}");
                }
            }
        }, cancellationToken);
    }

    private async Task EmitHeartbeatAsync()
    {
        if (!_socket.Connected)
        {
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["agent"] = _options.AgentName,
            ["timestamp"] = DateTimeOffset.UtcNow,
            ["uptimeSeconds"] = Math.Round((DateTimeOffset.UtcNow - _startedAt).TotalSeconds, 2)
        };

        await SafeEmitAsync(AgentHeartbeatEvent, payload, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task SafeEmitAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        if (!_socket.Connected)
        {
            LogWarning($"Skip emit '{eventName}' because socket is disconnected.");
            return;
        }

        try
        {
            await _socket.EmitAsync(eventName, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ignore cancellation.
        }
        catch (Exception ex)
        {
            LogError($"Emit '{eventName}' failed: {ex.Message}", ex);
        }
    }

    private AgentJobRequest? TryParseJob(SocketIOResponse response)
    {
        try
        {
            var element = response.GetValue<JsonElement>(0);
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

            return new AgentJobRequest
            {
                Id = id,
                Type = type,
                Payload = payloadNode,
                Metadata = metadataNode
            };
        }
        catch (Exception ex)
        {
            LogError("Failed to parse job message.", ex);
            return null;
        }
    }

    private static Dictionary<string, object?> PrepareCheckPayload(CheckResult result)
    {
        return new Dictionary<string, object?>
        {
            ["check"] = result.Check,
            ["success"] = result.Success,
            ["message"] = result.Message,
            ["durationMs"] = result.DurationMs,
            ["data"] = result.Data
        };
    }

    private static string? ExtractString(JsonObject obj, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!obj.TryGetPropertyValue(name, out var node) || node is null)
            {
                continue;
            }

            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<string>(out var strValue))
                {
                    return strValue;
                }

                if (jsonValue.TryGetValue<long>(out var longValue))
                {
                    return longValue.ToString(CultureInfo.InvariantCulture);
                }

                if (jsonValue.TryGetValue<double>(out var doubleValue))
                {
                    return doubleValue.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        return null;
    }

    private static void LogInfo(string message)
    {
        Console.WriteLine($"[{DateTimeOffset.UtcNow:O}] INFO  {message}");
    }

    private static void LogWarning(string message)
    {
        Console.WriteLine($"[{DateTimeOffset.UtcNow:O}] WARN  {message}");
    }

    private static void LogError(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:O}] ERROR {message}");
        }
        else
        {
            Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:O}] ERROR {message}: {exception}");
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AgentRunner));
        }
    }
}