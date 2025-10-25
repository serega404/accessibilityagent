namespace AccessibilityAgent.Checks;

/// <summary>
/// Central place for default timeout values used by network checks.
/// </summary>
internal static class CheckDefaults
{
    public const int PingTimeoutMs = 4_000;
    public const int DnsTimeoutMs = 4_000;
    public const int TcpTimeoutMs = 3_000;
    public const int UdpTimeoutMs = 2_000;
    public const int HttpTimeoutMs = 5_000;
}
