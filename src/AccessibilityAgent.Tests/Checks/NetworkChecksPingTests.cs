using System;
using System.Threading.Tasks;
using AccessibilityAgent.Checks;
using Xunit;

namespace AccessibilityAgent.Tests.Checks;

public sealed class NetworkChecksPingTests
{
    private static bool IsIcmpPermissionIssue(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        // На Linux без CAP_NET_RAW возможны Permission denied / Operation not permitted
        var msg = message.ToLowerInvariant();
        return msg.Contains("permission denied")
               || msg.Contains("operation not permitted")
               || msg.Contains("requires elevated privileges")
               || msg.Contains("access denied");
    }

    [Fact(DisplayName = "Ping 127.0.0.1 succeeds or is allowed to fail due to ICMP permissions")]
    public async Task Ping_Localhost_SucceedsOrPermission()
    {
        // Используем явный IPv4, чтобы не зависеть от настроек IPv6 в окружении
        const string host = "127.0.0.1";
        var result = await NetworkChecks.PingAsync(host, timeoutMs: 2000);

        // В большинстве окружений пинг localhost успешен. Однако на Linux без прав ICMP может быть отказ в доступе.
        Assert.True(
            result.Success || IsIcmpPermissionIssue(result.Message),
            $@"Expected success or ICMP permission issue. Actual: success={result.Success}, message='{result.Message}'\nCheck='{result.Check}', durationMs={result.DurationMs}"
        );

        Assert.StartsWith($"ping:{host}", result.Check, StringComparison.Ordinal);
        Assert.True(result.DurationMs is null or >= 0);
    }

    [Fact(DisplayName = "Ping to TEST-NET address fails (likely timeout)")]
    public async Task Ping_Unreachable_Fails()
    {
        // RFC 5737 TEST-NET-1: 192.0.2.0/24 – не должен роутиться в Интернет
        const string unreachable = "192.0.2.1";
        var result = await NetworkChecks.PingAsync(unreachable, timeoutMs: 1500);

        // Разрешаем как неуспех по статусу (TimedOut/Unreachable/Unknown), так и исключение с сообщением об отсутствии привилегий ICMP
        Assert.True(
            !result.Success || IsIcmpPermissionIssue(result.Message),
            $"Expected failure (unreachable) or ICMP permission issue. Actual: success={result.Success}, message='{result.Message}'"
        );

        Assert.StartsWith($"ping:{unreachable}", result.Check, StringComparison.Ordinal);
    }
}
