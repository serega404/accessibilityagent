using AccessibilityAgent.Agent;
using AccessibilityAgent.Checks;
using AccessibilityAgent.Output;

namespace AccessibilityAgent.Cli;

/// <summary>
/// Диспетчер команд CLI для утилиты проверки доступности хоста и сетевых сервисов.
/// Принимает аргументы командной строки, валидирует их, запускает соответствующие проверки
/// (ping, dns, tcp, udp, http, а также составную check) и выводит результат в человекочитаемом или JSON-формате.
/// </summary>
/// <remarks>
/// Поддерживаемые команды:
/// - ping: ICMP-эхо до хоста;
/// - dns: разрешение имени хоста;
/// - tcp: попытка установить TCP-соединение на порт;
/// - udp: отправка UDP-пакета и опциональное ожидание ответа;
/// - http: HTTP-запрос по указанному URL;
/// - check: составная проверка с комбинируемыми опциями и сводкой.
/// По умолчанию используются встроенные таймауты, которые можно переопределить аргументами.
/// </remarks>
/// <example>
/// Примеры:
///   accessibilityagent ping example.com --timeout 3000
///   accessibilityagent tcp example.com 443 --format json
///   accessibilityagent http https://example.com --method GET --timeout 5000
///   accessibilityagent check example.com --tcp-port 80 --http --format json
/// </example>
internal static class CommandDispatcher
{

    public static async Task<int> RunAsync(string[] args)
    {
        // Если нет аргументов или запрошена общая справка, выводим общее руководство по использованию
        if (args.Length == 0 || IsGlobalHelp(args))
        {
            PrintGeneralUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].ToLowerInvariant();
        var commandArgs = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "ping" => await HandlePingAsync(commandArgs),
                "dns" => await HandleDnsAsync(commandArgs),
                "tcp" => await HandleTcpAsync(commandArgs),
                "udp" => await HandleUdpAsync(commandArgs),
                "http" => await HandleHttpAsync(commandArgs),
                "check" => await HandleCompositeAsync(commandArgs),
                "agent" => await HandleAgentAsync(commandArgs),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex}");
            return 2;
        }
    }

    private static bool IsGlobalHelp(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return false;

        var first = args[0];
        return string.Equals(first, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(first, "-h", StringComparison.OrdinalIgnoreCase)
            || string.Equals(first, "help", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintGeneralUsage()
    {
        Console.WriteLine("AccessibilityAgent - host reachability checker");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  accessibilityagent <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  ping   <host> [--timeout <ms>] [--format json]");
        Console.WriteLine("  dns    <host> [--format json]");
        Console.WriteLine("  tcp    <host> <port> [--timeout <ms>] [--format json]");
        Console.WriteLine("  udp    <host> <port> [--payload <text>] [--expect-response] [--timeout <ms>] [--format json]");
        Console.WriteLine("  http   <url> [--method <verb>] [--timeout <ms>] [--header key=value] [--body <text>] [--format json]");
        Console.WriteLine("  check  <host> [protocol-specific options] [--format json]");
        Console.WriteLine();
        Console.WriteLine("Run 'accessibilityagent <command> --help' for details on a specific command.");
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintGeneralUsage();
        return 1;
    }

    private static async Task<int> HandlePingAsync(string[] args)
    {
        var parsed = ParsedArguments.Parse(args);
        if (parsed.IsHelpRequested)
        {
            PrintPingUsage();
            return 0;
        }

        var host = RequireOption(parsed, ["host", "h"], "Host is required for ping checks.");
    var timeout = parsed.TryGetIntOption(["timeout"], minValue: 1) ?? CheckDefaults.PingTimeoutMs;
        var result = await NetworkChecks.PingAsync(host, timeout);

        var format = ResultWriter.DetermineFormat(parsed);
        ResultWriter.Write([result], format, includeSummary: false);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> HandleDnsAsync(string[] args)
    {
        var parsed = ParsedArguments.Parse(args);
        if (parsed.IsHelpRequested)
        {
            PrintDnsUsage();
            return 0;
        }

        var host = RequireOption(parsed, ["host", "h"], "Host name is required for DNS checks.");
    var timeout = parsed.TryGetIntOption(["timeout"], minValue: 1) ?? CheckDefaults.DnsTimeoutMs;
        var result = await NetworkChecks.DnsAsync(host, timeout);

        var format = ResultWriter.DetermineFormat(parsed);
        ResultWriter.Write([result], format, includeSummary: false);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> HandleTcpAsync(string[] args)
    {
        var parsed = ParsedArguments.Parse(args);
        if (parsed.IsHelpRequested)
        {
            PrintTcpUsage();
            return 0;
        }

        var host = RequireOption(parsed, ["host", "h"], "Host is required for TCP checks.");
        var port = parsed.RequireIntOption(["port", "p"], minValue: 1, maxValue: 65_535);
    var timeout = parsed.TryGetIntOption(["timeout"], minValue: 1) ?? CheckDefaults.TcpTimeoutMs;
        var result = await NetworkChecks.TcpAsync(host, port, timeout);

        var format = ResultWriter.DetermineFormat(parsed);
        ResultWriter.Write([result], format, includeSummary: false);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> HandleUdpAsync(string[] args)
    {
        var parsed = ParsedArguments.Parse(args);
        if (parsed.IsHelpRequested)
        {
            PrintUdpUsage();
            return 0;
        }

        var host = RequireOption(parsed, ["host", "h"], "Host is required for UDP checks.");
        var port = parsed.RequireIntOption(["port", "p"], minValue: 1, maxValue: 65_535);
    var timeout = parsed.TryGetIntOption(["timeout"], minValue: 1) ?? CheckDefaults.UdpTimeoutMs;
        var payload = parsed.GetOption(["payload"]) ?? string.Empty;
        var expectResponse = parsed.HasFlag(["expect-response", "expectresponse"]);
        var result = await NetworkChecks.UdpAsync(host, port, payload, timeout, expectResponse);

        var format = ResultWriter.DetermineFormat(parsed);
        ResultWriter.Write([result], format, includeSummary: false);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> HandleHttpAsync(string[] args)
    {
        var parsed = ParsedArguments.Parse(args);
        if (parsed.IsHelpRequested)
        {
            PrintHttpUsage();
            return 0;
        }

        var url = RequireOption(parsed, ["url", "u"], "URL is required for HTTP checks.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Invalid URL '{url}'.");
        }

        var method = parsed.GetOption(["method", "m"]) ?? "GET";
    var timeout = parsed.TryGetIntOption(["timeout"], minValue: 1) ?? CheckDefaults.HttpTimeoutMs;
        var body = parsed.GetOption(["body", "data"]);
        var contentType = parsed.GetOption(["content-type", "contenttype"]) ?? "text/plain";
        var headerValues = parsed.GetOptionValues(["header", "H"]);
        var result = await NetworkChecks.HttpAsync(uri, method, timeout, body, contentType, headerValues);

        var format = ResultWriter.DetermineFormat(parsed);
        ResultWriter.Write([result], format, includeSummary: false);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> HandleCompositeAsync(string[] args)
    {
        var parsed = ParsedArguments.Parse(args);
        if (parsed.IsHelpRequested)
        {
            PrintCompositeUsage();
            return 0;
        }

        var host = RequireOption(parsed, ["host", "h"], "Host is required for composite checks.");
        var format = ResultWriter.DetermineFormat(parsed);
        var checkTasks = new List<Task<CheckResult>>();

        var overallTimeout = parsed.TryGetIntOption(["timeout"], minValue: 1);

        if (!parsed.HasFlag(["skip-ping", "no-ping", "skip_ping"]))
        {
            var timeout = parsed.TryGetIntOption(["ping-timeout"], minValue: 1) ?? overallTimeout ?? CheckDefaults.PingTimeoutMs;
            checkTasks.Add(NetworkChecks.PingAsync(host, timeout));
        }

        if (!parsed.HasFlag(["skip-dns", "no-dns", "skip_dns"]))
        {
            var dnsHost = parsed.GetOption(["dns-host"]) ?? host;
            var timeout = parsed.TryGetIntOption(["dns-timeout"], minValue: 1) ?? overallTimeout ?? CheckDefaults.DnsTimeoutMs;
            checkTasks.Add(NetworkChecks.DnsAsync(dnsHost, timeout));
        }

        foreach (var portToken in parsed.GetOptionValues(["tcp-port", "tcp"]))
        {
            if (!int.TryParse(portToken, out var port) || port is < 1 or > 65_535)
            {
                throw new ArgumentException($"Invalid TCP port '{portToken}'.");
            }

            var timeout = parsed.TryGetIntOption(["tcp-timeout"], minValue: 1) ?? overallTimeout ?? CheckDefaults.TcpTimeoutMs;
            checkTasks.Add(NetworkChecks.TcpAsync(host, port, timeout));
        }

        var udpPayload = parsed.GetOption(["udp-payload"]);
        var udpExpectResponse = parsed.HasFlag(["udp-expect-response", "udp-expectresponse"]);
        foreach (var portToken in parsed.GetOptionValues(["udp-port", "udp"]))
        {
            if (!int.TryParse(portToken, out var port) || port is < 1 or > 65_535)
            {
                throw new ArgumentException($"Invalid UDP port '{portToken}'.");
            }

            var timeout = parsed.TryGetIntOption(["udp-timeout"], minValue: 1) ?? overallTimeout ?? CheckDefaults.UdpTimeoutMs;
            checkTasks.Add(NetworkChecks.UdpAsync(host, port, udpPayload ?? string.Empty, timeout, udpExpectResponse));
        }

        var httpUrls = parsed.GetOptionValues(["http-url"]).ToList();
        var httpMethod = parsed.GetOption(["http-method"]) ?? parsed.GetOption(["method"]) ?? "GET";
    var httpTimeout = parsed.TryGetIntOption(["http-timeout"], minValue: 1) ?? overallTimeout ?? CheckDefaults.HttpTimeoutMs;
        var httpBody = parsed.GetOption(["http-body", "body"]);
        var httpContentType = parsed.GetOption(["http-content-type", "content-type"]) ?? "text/plain";
        var httpHeaders = parsed.GetOptionValues(["http-header", "header"]);

        var httpPath = parsed.GetOption(["http-path"]) ?? "/";
        var httpPorts = parsed.GetOptionValues(["http-port"]).ToList();
        var httpFlag = parsed.HasFlag(["http"]);
        var useHttps = parsed.HasFlag(["https"]);

        if (httpUrls.Count == 0 && (httpFlag || httpPorts.Count > 0))
        {
            if (httpPorts.Count == 0)
            {
                var uriBuilder = new UriBuilder(useHttps ? "https" : "http", host)
                {
                    Path = httpPath
                };
                httpUrls.Add(uriBuilder.Uri.ToString());
            }
            else
            {
                foreach (var portToken in httpPorts)
                {
                    if (!int.TryParse(portToken, out var port) || port is < 1 or > 65_535)
                    {
                        throw new ArgumentException($"Invalid HTTP port '{portToken}'.");
                    }

                    var uriBuilder = new UriBuilder(useHttps ? "https" : "http", host, port)
                    {
                        Path = httpPath
                    };
                    httpUrls.Add(uriBuilder.Uri.ToString());
                }
            }
        }

        foreach (var urlToken in httpUrls)
        {
            if (!Uri.TryCreate(urlToken, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException($"Invalid HTTP URL '{urlToken}'.");
            }

            checkTasks.Add(NetworkChecks.HttpAsync(uri, httpMethod, httpTimeout, httpBody, httpContentType, httpHeaders));
        }

        // Выполняем все запрошенные проверки одновременно, сохраняя предсказуемый порядок вывода.
        var results = checkTasks.Count == 0
            ? Array.Empty<CheckResult>()
            : await Task.WhenAll(checkTasks);

        ResultWriter.Write(results, format, includeSummary: true);
        return results.All(r => r.Success) ? 0 : 1;
    }

    private static string RequireOption(ParsedArguments parsed, string[] optionNames, string message)
    {
        var value = parsed.GetOption(optionNames);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value!;
        }

        var positional = parsed.ConsumePositional();
        if (!string.IsNullOrWhiteSpace(positional))
        {
            return positional!;
        }

        throw new ArgumentException(message);
    }

    private static void PrintPingUsage()
    {
        Console.WriteLine("Usage: accessibilityagent ping <host> [--timeout <ms>] [--format json]");
        Console.WriteLine("Sends an ICMP echo request and reports roundtrip time.");
    }

    private static void PrintDnsUsage()
    {
        Console.WriteLine("Usage: accessibilityagent dns <host> [--timeout <ms>] [--format json]");
        Console.WriteLine("Resolves the host name and lists resolved IP addresses.");
    }

    private static void PrintTcpUsage()
    {
        Console.WriteLine("Usage: accessibilityagent tcp <host> <port> [--timeout <ms>] [--format json]");
        Console.WriteLine("Attempts to establish a TCP connection to the host:port.");
    }

    private static void PrintUdpUsage()
    {
        Console.WriteLine("Usage: accessibilityagent udp <host> <port> [--payload <text>] [--expect-response] [--timeout <ms>] [--format json]");
        Console.WriteLine("Sends a UDP datagram and optionally waits for a response.");
    }

    private static void PrintHttpUsage()
    {
        Console.WriteLine("Usage: accessibilityagent http <url> [--method <verb>] [--timeout <ms>] [--header key=value] [--body <text>] [--format json]");
        Console.WriteLine("Performs an HTTP request and reports status code.");
    }

    private static void PrintCompositeUsage()
    {
        Console.WriteLine("Usage: accessibilityagent check <host> [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  --skip-ping                 Skip the ping check");
        Console.WriteLine("  --skip-dns                  Skip DNS resolution");
        Console.WriteLine("  --tcp-port <value>          Add a TCP port check (repeatable)");
        Console.WriteLine("  --udp-port <value>          Add a UDP port check (repeatable)");
        Console.WriteLine("  --udp-payload <text>        Payload to send with UDP checks");
        Console.WriteLine("  --udp-expect-response       Wait for UDP response");
        Console.WriteLine("  --http                      Check default HTTP endpoint (http://host/)");
        Console.WriteLine("  --https                     Use HTTPS when constructing default HTTP URLs");
        Console.WriteLine("  --http-url <value>          Explicit HTTP URL to check (repeatable)");
        Console.WriteLine("  --http-port <value>         Construct URL from host and port (repeatable)");
        Console.WriteLine("  --http-path <value>         Path for constructed HTTP URLs (default '/')");
        Console.WriteLine("  --timeout <ms>              Default timeout for all checks");
        Console.WriteLine("  --ping-timeout <ms>         Ping timeout override");
        Console.WriteLine("  --dns-timeout <ms>          DNS timeout override");
        Console.WriteLine("  --tcp-timeout <ms>          TCP timeout override");
        Console.WriteLine("  --udp-timeout <ms>          UDP timeout override");
        Console.WriteLine("  --http-timeout <ms>         HTTP timeout override");
        Console.WriteLine("  --http-method <verb>        HTTP method (default GET)");
        Console.WriteLine("  --http-header key=value     Additional HTTP headers");
        Console.WriteLine("  --http-body <text>          Optional HTTP request body");
        Console.WriteLine("  --format json               Emit JSON output");
    }

    private static async Task<int> HandleAgentAsync(string[] args)
    {
        var parsed = ParsedArguments.Parse(args);
        if (parsed.IsHelpRequested)
        {
            PrintAgentUsage();
            return 0;
        }

        var serverUrl = parsed.GetOption(["server", "url", "s"]) ?? Environment.GetEnvironmentVariable("AA_SERVER_URL");
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new ArgumentException("Agent mode requires --server option or AA_SERVER_URL environment variable.");
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri))
        {
            throw new ArgumentException($"Invalid server URL '{serverUrl}'.");
        }

        var credentialPath = parsed.GetOption(["creds", "credentials", "credential-file"]) ?? Environment.GetEnvironmentVariable("AA_AGENT_CREDENTIAL_FILE") ?? Agent.AgentCredentialStore.GetDefaultPath();
        // Попытка прочитать сохранённый персональный токен
        var saved = Agent.AgentCredentialStore.Load(credentialPath);
        var token = parsed.GetOption(["token", "t"]) ?? Environment.GetEnvironmentVariable("AA_AGENT_TOKEN") ?? saved?.Token;
        // Если нет ни мастер-токена, ни сохранённого, потребуем хотя бы мастер-токен для первичной инициализации
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Agent mode requires --token (master or personal) or AA_AGENT_TOKEN, or existing credentials file.");
        }

    var name = parsed.GetOption(["name", "n"]) ?? Environment.GetEnvironmentVariable("AA_AGENT_NAME") ?? saved?.AgentName ?? Environment.MachineName;

        var reconnectDelayMs = parsed.TryGetIntOption(["reconnect-delay"], minValue: 100) ?? 2_000;
        var reconnectDelayMaxMs = parsed.TryGetIntOption(["reconnect-delay-max"], minValue: reconnectDelayMs) ?? 30_000;
        var reconnectAttempts = parsed.TryGetIntOption(["reconnect-attempts"], minValue: 1);
        var heartbeatMs = parsed.TryGetIntOption(["heartbeat"], minValue: 0) ?? 30_000;

        var metadata = ParseMetadata(parsed.GetOptionValues(["metadata", "meta"]));
        if (metadata is null && saved?.Metadata is { Count: > 0 })
        {
            metadata = saved!.Metadata!;
        }

        var options = new AgentOptions(
            serverUri,
            token,
            name,
            TimeSpan.FromMilliseconds(reconnectDelayMs),
            TimeSpan.FromMilliseconds(reconnectDelayMaxMs),
            reconnectAttempts,
            TimeSpan.FromMilliseconds(heartbeatMs),
            metadata,
            credentialFilePath: credentialPath,
            autoIssuePersonalToken: !parsed.HasFlag(["no-auto-issue"])) ;

    await using var runner = new AgentRunner(options);
        using var shutdownCts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdownCts.Cancel();
        };

        try
        {
            await runner.RunAsync(shutdownCts.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Agent terminated with error: {ex.Message}");
            return 1;
        }
    }

    private static IReadOnlyDictionary<string, string>? ParseMetadata(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var separatorIndex = raw.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == raw.Length - 1)
            {
                throw new ArgumentException($"Metadata entry '{raw}' must be in key=value format.");
            }

            var key = raw[..separatorIndex].Trim();
            var value = raw[(separatorIndex + 1)..].Trim();

            if (key.Length == 0)
            {
                throw new ArgumentException($"Metadata entry '{raw}' contains an empty key.");
            }

            metadata[key] = value;
        }

        return metadata.Count == 0 ? null : metadata;
    }

    private static void PrintAgentUsage()
    {
        Console.WriteLine("Usage: accessibilityagent agent [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  --server <url>             Coordinator Socket.IO endpoint (or AA_SERVER_URL)");
        Console.WriteLine("  --token <value>            Authentication token (or AA_AGENT_TOKEN)");
        Console.WriteLine("  --name <value>             Agent name override (default machine name)");
        Console.WriteLine("  --reconnect-delay <ms>     Initial reconnect delay in milliseconds (default 2000)");
        Console.WriteLine("  --reconnect-delay-max <ms> Maximum reconnect delay in milliseconds (default 30000)");
        Console.WriteLine("  --reconnect-attempts <n>   Limits reconnect attempts (optional)");
        Console.WriteLine("  --heartbeat <ms>           Heartbeat interval in milliseconds (default 30000, zero to disable)");
        Console.WriteLine("  --metadata key=value       Additional metadata (repeatable)");
        Console.WriteLine("  --credentials <path>       Path to credentials file (default ~/.config/accessibilityagent/agent.json)");
        Console.WriteLine("  --no-auto-issue            Do not request personal token automatically on first run");
    }
}
