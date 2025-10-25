namespace AccessibilityAgent.Checks;

/// <summary>
/// Abstraction over network check implementations to simplify testing of the agent pipeline.
/// </summary>
internal interface ICheckRunner
{
    Task<CheckResult> PingAsync(string host, int timeoutMs);

    Task<CheckResult> DnsAsync(string host, int timeoutMs);

    Task<CheckResult> TcpAsync(string host, int port, int timeoutMs);

    Task<CheckResult> UdpAsync(string host, int port, string payload, int timeoutMs, bool expectResponse);

    Task<CheckResult> HttpAsync(Uri uri, string method, int timeoutMs, string? body, string contentType, IEnumerable<string> headers);
}
