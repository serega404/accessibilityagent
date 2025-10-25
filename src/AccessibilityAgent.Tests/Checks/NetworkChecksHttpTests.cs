using System;
using System.Threading.Tasks;
using AccessibilityAgent.Checks;
using Xunit;

namespace AccessibilityAgent.Tests.Checks;

public sealed class NetworkChecksHttpTests
{
    [Fact(DisplayName = "HTTP GET to google.com returns success (2xx)")]
    public async Task Http_Google_Success()
    {
        var uri = new Uri("https://www.google.com/");
        var result = await NetworkChecks.HttpAsync(uri, method: "GET", timeoutMs: 5000, body: null, contentType: "text/plain", headerValues: Array.Empty<string>());

        Assert.Equal($"http:{uri}", result.Check);
        Assert.True(result.Success, $"Expected 2xx from {uri}. Actual: success={result.Success}, message='{result.Message}'");
        Assert.NotNull(result.Data);

        if (result.Data is not null && result.Data.TryGetValue("statusCode", out var codeStr) && int.TryParse(codeStr, out var code))
        {
            Assert.InRange(code, 200, 299);
        }
    }
}
