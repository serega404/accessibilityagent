using System.Globalization;
using System.Text.Json.Nodes;
using AccessibilityAgent.Checks;


namespace AccessibilityAgent.Agent;

/// <summary>
/// Исполнитель заданий агента доступности. Отвечает за обработку запросов на выполнение различных сетевых и сервисных проверок (ping, dns, tcp, udp, http и составных).
/// Использует <see cref="ICheckRunner"/> для выполнения конкретных проверок.
/// </summary>
internal sealed class AgentJobExecutor
{
    private readonly ICheckRunner _checkRunner;

    /// <summary>
    /// Создает экземпляр <see cref="AgentJobExecutor"/> с указанным исполнителем проверок.
    /// </summary>
    /// <param name="checkRunner">Реализация <see cref="ICheckRunner"/>, используемая для выполнения проверок.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="checkRunner"/> равен null.</exception>
    public AgentJobExecutor(ICheckRunner checkRunner)
    {
        _checkRunner = checkRunner ?? throw new ArgumentNullException(nameof(checkRunner));
    }

    /// <summary>
    /// Асинхронно выполняет задание агента, определяя тип проверки и возвращая результат выполнения.
    /// </summary>
    /// <param name="request">Запрос на выполнение задания агента.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Результат выполнения задания <see cref="AgentJobExecutionResult"/>.</returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="request"/> равен null.</exception>
    /// <exception cref="OperationCanceledException">Если операция была отменена.</exception>
    public async Task<AgentJobExecutionResult> ExecuteAsync(AgentJobRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checks = await ExecuteInternalAsync(request, cancellationToken);
            var materialized = checks.ToList();
            return new AgentJobExecutionResult
            {
                JobId = request.Id,
                Success = materialized.All(c => c.Success),
                Checks = materialized
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AgentJobExecutionResult
            {
                JobId = request.Id,
                Success = false,
                Error = ex.Message,
                ErrorDetails = ex.ToString()
            };
        }
    }

    /// <summary>
    /// Внутренний метод для выбора и выполнения соответствующей проверки по типу задания.
    /// </summary>
    /// <param name="request">Запрос на выполнение задания агента.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Список результатов проверок.</returns>
    private async Task<IReadOnlyList<CheckResult>> ExecuteInternalAsync(AgentJobRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            throw new InvalidOperationException("Job type is missing.");
        }

        var normalizedType = request.Type.Trim().ToLowerInvariant();

        return normalizedType switch
        {
            "ping" => new[] { await ExecutePingAsync(request) },
            "dns" => new[] { await ExecuteDnsAsync(request) },
            "tcp" => new[] { await ExecuteTcpAsync(request) },
            "udp" => new[] { await ExecuteUdpAsync(request) },
            "http" => new[] { await ExecuteHttpAsync(request) },
            "check" => await ExecuteCompositeAsync(request, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported job type '{request.Type}'.")
        };
    }

    private async Task<CheckResult> ExecutePingAsync(AgentJobRequest request)
    {
        var payload = AsObject(request.Payload);
        var host = RequireNonEmpty(GetString(payload, "host"), "host", request.Type);
        var timeout = NormalizeTimeout(GetInt(payload, "timeoutMs", "timeout"), "timeoutMs") ?? CheckDefaults.PingTimeoutMs;
        return await _checkRunner.PingAsync(host, timeout);
    }

    private async Task<CheckResult> ExecuteDnsAsync(AgentJobRequest request)
    {
        var payload = AsObject(request.Payload);
        var host = RequireNonEmpty(GetString(payload, "host"), "host", request.Type);
        var timeout = NormalizeTimeout(GetInt(payload, "timeoutMs", "timeout"), "timeoutMs") ?? CheckDefaults.DnsTimeoutMs;
        return await _checkRunner.DnsAsync(host, timeout);
    }

    private async Task<CheckResult> ExecuteTcpAsync(AgentJobRequest request)
    {
        var payload = AsObject(request.Payload);
        var host = RequireNonEmpty(GetString(payload, "host"), "host", request.Type);
        var port = NormalizePort(GetInt(payload, "port"), "port");
        var timeout = NormalizeTimeout(GetInt(payload, "timeoutMs", "timeout"), "timeoutMs") ?? CheckDefaults.TcpTimeoutMs;
        return await _checkRunner.TcpAsync(host, port, timeout);
    }

    private async Task<CheckResult> ExecuteUdpAsync(AgentJobRequest request)
    {
        var payload = AsObject(request.Payload);
        var host = RequireNonEmpty(GetString(payload, "host"), "host", request.Type);
        var port = NormalizePort(GetInt(payload, "port"), "port");
        var timeout = NormalizeTimeout(GetInt(payload, "timeoutMs", "timeout"), "timeoutMs") ?? CheckDefaults.UdpTimeoutMs;
        var payloadText = GetString(payload, "payload") ?? string.Empty;
        var expectResponse = GetBool(payload, "expectResponse") ?? false;
        return await _checkRunner.UdpAsync(host, port, payloadText, timeout, expectResponse);
    }

    private async Task<CheckResult> ExecuteHttpAsync(AgentJobRequest request)
    {
        var payload = AsObject(request.Payload);
        var url = RequireNonEmpty(GetString(payload, "url", "uri"), "url", request.Type);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid URL '{url}'.");
        }

        var method = GetString(payload, "method")?.Trim().ToUpperInvariant() ?? "GET";
        var timeout = NormalizeTimeout(GetInt(payload, "timeoutMs", "timeout"), "timeoutMs") ?? CheckDefaults.HttpTimeoutMs;
        var body = GetString(payload, "body", "data");
        var contentType = GetString(payload, "contentType", "content-type") ?? "text/plain";
        var headers = ExtractStringList(payload, "headers", "header");
        return await _checkRunner.HttpAsync(uri, method, timeout, body, contentType, headers);
    }

    private async Task<IReadOnlyList<CheckResult>> ExecuteCompositeAsync(AgentJobRequest request, CancellationToken cancellationToken)
    {
        var payload = AsObject(request.Payload);
        var host = RequireNonEmpty(GetString(payload, "host"), "host", request.Type);
        var tasks = new List<Task<CheckResult>>();
        var overallTimeout = NormalizeTimeout(GetInt(payload, "timeoutMs", "timeout"), "timeoutMs");

        var skipPing = GetBool(payload, "skipPing", "noPing", "skip_ping") == true;
        if (!skipPing)
        {
            var timeout = NormalizeTimeout(GetInt(payload, "pingTimeoutMs", "pingTimeout"), "pingTimeoutMs") ?? overallTimeout ?? CheckDefaults.PingTimeoutMs;
            tasks.Add(_checkRunner.PingAsync(host, timeout));
        }

        var skipDns = GetBool(payload, "skipDns", "noDns", "skip_dns") == true;
        if (!skipDns)
        {
            var dnsHost = GetString(payload, "dnsHost") ?? host;
            var timeout = NormalizeTimeout(GetInt(payload, "dnsTimeoutMs", "dnsTimeout"), "dnsTimeoutMs") ?? overallTimeout ?? CheckDefaults.DnsTimeoutMs;
            tasks.Add(_checkRunner.DnsAsync(dnsHost, timeout));
        }

        var tcpPorts = ExtractIntList(payload, "tcpPorts", "tcpPort", "tcp");
        if (tcpPorts.Count > 0)
        {
            var timeout = NormalizeTimeout(GetInt(payload, "tcpTimeoutMs", "tcpTimeout"), "tcpTimeoutMs") ?? overallTimeout ?? CheckDefaults.TcpTimeoutMs;
            foreach (var portValue in tcpPorts)
            {
                var port = NormalizePort(portValue, "tcpPorts");
                tasks.Add(_checkRunner.TcpAsync(host, port, timeout));
            }
        }

        var udpPorts = ExtractIntList(payload, "udpPorts", "udpPort", "udp");
        if (udpPorts.Count > 0)
        {
            var udpPayload = GetString(payload, "udpPayload", "payload") ?? string.Empty;
            var udpExpectResponse = GetBool(payload, "udpExpectResponse", "udpExpectresponse") ?? false;
            var timeout = NormalizeTimeout(GetInt(payload, "udpTimeoutMs", "udpTimeout"), "udpTimeoutMs") ?? overallTimeout ?? CheckDefaults.UdpTimeoutMs;

            foreach (var portValue in udpPorts)
            {
                var port = NormalizePort(portValue, "udpPorts");
                tasks.Add(_checkRunner.UdpAsync(host, port, udpPayload, timeout, udpExpectResponse));
            }
        }

        var httpUris = BuildCompositeHttpUris(host, payload);
        if (httpUris.Count > 0)
        {
            var method = GetString(payload, "httpMethod", "method")?.Trim().ToUpperInvariant() ?? "GET";
            var timeout = NormalizeTimeout(GetInt(payload, "httpTimeoutMs", "httpTimeout"), "httpTimeoutMs") ?? overallTimeout ?? CheckDefaults.HttpTimeoutMs;
            var body = GetString(payload, "httpBody", "body");
            var contentType = GetString(payload, "httpContentType", "contentType", "content-type") ?? "text/plain";
            var headers = ExtractStringList(payload, "httpHeaders", "headers", "header");

            foreach (var uri in httpUris)
            {
                tasks.Add(_checkRunner.HttpAsync(uri, method, timeout, body, contentType, headers));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (tasks.Count == 0)
        {
            return Array.Empty<CheckResult>();
        }

        return await Task.WhenAll(tasks);
    }

    private static List<Uri> BuildCompositeHttpUris(string host, JsonObject payload)
    {
        var uris = new List<Uri>();
        var explicitUrls = ExtractStringList(payload, "httpUrls", "httpUrl");

        foreach (var url in explicitUrls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            {
                throw new InvalidOperationException($"Invalid HTTP URL '{url}'.");
            }

            uris.Add(parsed);
        }

        var httpPorts = ExtractIntList(payload, "httpPorts", "httpPort");
        var httpFlag = GetBool(payload, "http") == true;
        var useHttps = GetBool(payload, "https") == true;
        var httpPath = GetString(payload, "httpPath");

        if (uris.Count == 0 && (httpFlag || httpPorts.Count > 0))
        {
            var path = string.IsNullOrWhiteSpace(httpPath) ? "/" : httpPath!;

            if (httpPorts.Count == 0)
            {
                uris.Add(BuildUri(host, null, path, useHttps));
            }
            else
            {
                foreach (var portValue in httpPorts)
                {
                    var port = NormalizePort(portValue, "httpPorts");
                    uris.Add(BuildUri(host, port, path, useHttps));
                }
            }
        }

        return uris;
    }

    private static Uri BuildUri(string host, int? port, string path, bool useHttps)
    {
        var builder = new UriBuilder(useHttps ? "https" : "http", host)
        {
            Path = string.IsNullOrWhiteSpace(path) ? "/" : path
        };

        if (port.HasValue)
        {
            builder.Port = port.Value;
        }

        return builder.Uri;
    }

    private static string RequireNonEmpty(string? value, string propertyName, string jobType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Job '{jobType}' is missing required property '{propertyName}'.");
        }

        return value;
    }

    private static int NormalizePort(int? port, string propertyName)
    {
        if (!port.HasValue)
        {
            throw new InvalidOperationException($"Port property '{propertyName}' is required.");
        }

        if (port.Value is < 1 or > 65_535)
        {
            throw new InvalidOperationException($"Port '{port.Value}' in '{propertyName}' is out of range (1-65535).");
        }

        return port.Value;
    }

    private static int NormalizePort(int port, string propertyName)
    {
        if (port is < 1 or > 65_535)
        {
            throw new InvalidOperationException($"Port '{port}' in '{propertyName}' is out of range (1-65535).");
        }

        return port;
    }

    private static int? NormalizeTimeout(int? timeout, string propertyName)
    {
        if (!timeout.HasValue)
        {
            return null;
        }

        if (timeout.Value <= 0)
        {
            throw new InvalidOperationException($"Timeout '{propertyName}' must be greater than zero when specified.");
        }

        return timeout.Value;
    }

    private static JsonObject AsObject(JsonNode? node)
    {
        return node as JsonObject ?? new JsonObject();
    }

    private static string? GetString(JsonObject obj, params string[] propertyNames)
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

            var value = ReadStringFromNode(node);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static int? GetInt(JsonObject obj, params string[] propertyNames)
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

            if (TryReadInt(node, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool? GetBool(JsonObject obj, params string[] propertyNames)
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

            if (TryReadBool(node, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static IReadOnlyList<int> ExtractIntList(JsonObject obj, params string[] propertyNames)
    {
        var values = new List<int>();

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

            if (node is JsonArray array)
            {
                foreach (var element in array)
                {
                    if (element is null)
                    {
                        continue;
                    }

                    if (TryReadInt(element, out var value))
                    {
                        values.Add(value);
                    }
                }
            }
            else if (TryReadInt(node, out var single))
            {
                values.Add(single);
            }
        }

        return values.Count == 0 ? Array.Empty<int>() : values;
    }

    private static IReadOnlyList<string> ExtractStringList(JsonObject obj, params string[] propertyNames)
    {
        var values = new List<string>();

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

            switch (node)
            {
                case JsonArray array:
                    foreach (var element in array)
                    {
                        var str = ReadStringFromNode(element);
                        if (!string.IsNullOrWhiteSpace(str))
                        {
                            values.Add(str);
                        }
                    }

                    break;
                case JsonObject nested:
                    foreach (var kvp in nested)
                    {
                        var str = ReadStringFromNode(kvp.Value);
                        if (str is not null)
                        {
                            values.Add($"{kvp.Key}:{str}");
                        }
                    }

                    break;
                default:
                    var single = ReadStringFromNode(node);
                    if (!string.IsNullOrWhiteSpace(single))
                    {
                        values.Add(single);
                    }

                    break;
            }
        }

        return values.Count == 0 ? Array.Empty<string>() : values;
    }

    private static bool TryReadInt(JsonNode node, out int value)
    {
        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                value = intValue;
                return true;
            }

            if (jsonValue.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue)
            {
                value = (int)longValue;
                return true;
            }

            if (jsonValue.TryGetValue<string>(out var stringValue) && int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadBool(JsonNode node, out bool value)
    {
        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var boolValue))
            {
                value = boolValue;
                return true;
            }

            if (jsonValue.TryGetValue<string>(out var stringValue))
            {
                if (bool.TryParse(stringValue, out var parsedBool))
                {
                    value = parsedBool;
                    return true;
                }

                if (int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                {
                    value = parsedInt != 0;
                    return true;
                }
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                value = intValue != 0;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadStringFromNode(JsonNode? node)
    {
        if (node is null)
        {
            return null;
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

            if (jsonValue.TryGetValue<bool>(out var boolValue))
            {
                return boolValue ? "true" : "false";
            }
        }

        return node.ToJsonString();
    }
}
