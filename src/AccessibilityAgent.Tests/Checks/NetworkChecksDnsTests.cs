using System.Threading.Tasks;
using AccessibilityAgent.Checks;
using Xunit;

namespace AccessibilityAgent.Tests.Checks;

public sealed class NetworkChecksDnsTests
{
    [Fact(DisplayName = "DNS resolves google.com successfully")]
    public async Task Dns_Google_Success()
    {
        const string host = "google.com";
        var result = await NetworkChecks.DnsAsync(host, timeoutMs: 5000);

        Assert.Equal($"dns:{host}", result.Check);
        Assert.True(result.Success, $"Expected DNS success for {host}. Actual: success={result.Success}, message='{result.Message}'");

        // Дополнительные метаданные с адресами не обязательны, но если есть — не пустые
        if (result.Data is not null && result.Data.TryGetValue("addresses", out var addresses))
        {
            Assert.False(string.IsNullOrWhiteSpace(addresses));
        }
    }

    [Fact(DisplayName = "DNS for nonexistent.invalid fails quickly")]
    public async Task Dns_Invalid_Fails()
    {
        // RFC 2606: .invalid зарезервирован и не должен резолвиться
        const string host = "nonexistent.invalid";
        var result = await NetworkChecks.DnsAsync(host, timeoutMs: 2000);

        Assert.Equal($"dns:{host}", result.Check);
        Assert.False(result.Success, $"Expected DNS failure for {host}. Actual: success={result.Success}, message='{result.Message}'");
    }
}
