using System;
using System.Threading.Tasks;
using AccessibilityAgent.Checks;
using Xunit;

namespace AccessibilityAgent.Tests.Checks;

public sealed class NetworkChecksTcpTests
{
    [Fact(DisplayName = "TCP connect to google.com:443 succeeds")]
    public async Task Tcp_Google443_Success()
    {
        const string host = "google.com";
        const int port = 443;
        var result = await NetworkChecks.TcpAsync(host, port, timeoutMs: 5000);

        Assert.Equal($"tcp:{host}:{port}", result.Check);
        Assert.True(result.Success, $"Expected TCP success to {host}:{port}. Actual: success={result.Success}, message='{result.Message}'");
        Assert.True(result.DurationMs is null or >= 0);
    }

    [Fact(DisplayName = "TCP connect to google.com:81 fails (closed or timeout)")]
    public async Task Tcp_Google81_Fails()
    {
        const string host = "google.com";
        const int port = 81; // Обычно закрыт у Google, ожидаем отказ/тайм-аут
        var result = await NetworkChecks.TcpAsync(host, port, timeoutMs: 3000);

        Assert.Equal($"tcp:{host}:{port}", result.Check);
        Assert.False(result.Success, $"Expected TCP failure to {host}:{port}. Actual: success={result.Success}, message='{result.Message}'");
    }
}
