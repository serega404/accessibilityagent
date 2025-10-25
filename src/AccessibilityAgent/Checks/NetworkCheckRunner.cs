namespace AccessibilityAgent.Checks;

/// <summary>
/// Default implementation of <see cref="ICheckRunner"/> that delegates to <see cref="NetworkChecks"/>.
/// </summary>
internal sealed class NetworkCheckRunner : ICheckRunner
{
    public Task<CheckResult> PingAsync(string host, int timeoutMs) => NetworkChecks.PingAsync(host, timeoutMs);

    public Task<CheckResult> DnsAsync(string host, int timeoutMs) => NetworkChecks.DnsAsync(host, timeoutMs);

    public Task<CheckResult> TcpAsync(string host, int port, int timeoutMs) => NetworkChecks.TcpAsync(host, port, timeoutMs);

    public Task<CheckResult> UdpAsync(string host, int port, string payload, int timeoutMs, bool expectResponse) => NetworkChecks.UdpAsync(host, port, payload, timeoutMs, expectResponse);

    public Task<CheckResult> HttpAsync(Uri uri, string method, int timeoutMs, string? body, string contentType, IEnumerable<string> headers) => NetworkChecks.HttpAsync(uri, method, timeoutMs, body, contentType, headers);
}
