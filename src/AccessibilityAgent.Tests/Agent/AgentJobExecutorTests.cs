using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AccessibilityAgent.Agent;
using AccessibilityAgent.Checks;
using Xunit;

namespace AccessibilityAgent.Tests.Agent;

public sealed class AgentJobExecutorTests
{
    // Проверяет, что при неизвестном типе задания возвращается ошибка
    [Fact]
    public async Task ExecuteAsync_UnknownJobType_ReturnsError()
    {
        var runner = new RecordingCheckRunner();
        var executor = new AgentJobExecutor(runner);
        var job = new AgentJobRequest
        {
            Id = "job-unknown",
            Type = "something",
            Payload = JsonNode.Parse("{}")
        };

        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Unsupported job type", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // Проверяет, что при отсутствии host в ping-задании возвращается ошибка валидации
    [Fact]
    public async Task ExecuteAsync_PingJobWithoutHost_ReturnsValidationError()
    {
        var runner = new RecordingCheckRunner();
        var executor = new AgentJobExecutor(runner);
        var job = new AgentJobRequest
        {
            Id = "job-ping",
            Type = "ping",
            Payload = JsonNode.Parse("{}")
        };

        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("missing required property 'host'", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.Checks.Count);
    }

    // Проверяет, что параметры TCP-задания корректно передаются в runner
    [Fact]
    public async Task ExecuteAsync_TcpJob_PassesParametersToRunner()
    {
        var runner = new RecordingCheckRunner();
        var executor = new AgentJobExecutor(runner);
        var job = new AgentJobRequest
        {
            Id = "job-tcp",
            Type = "tcp",
            Payload = JsonNode.Parse("{\"host\":\"example.test\",\"port\":8080,\"timeoutMs\":1500}")
        };

        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.Checks);
        Assert.Equal("example.test", runner.TcpHost);
        Assert.Equal(8080, runner.TcpPort);
        Assert.Equal(1500, runner.TcpTimeoutMs);
    }

    // Вспомогательный runner для фиксации параметров вызова
    private sealed class RecordingCheckRunner : ICheckRunner
    {
        public string? TcpHost { get; private set; }

        public int? TcpPort { get; private set; }

        public int? TcpTimeoutMs { get; private set; }

        public Task<CheckResult> PingAsync(string host, int timeoutMs) => Task.FromResult(Success($"ping:{host}", timeoutMs));

        public Task<CheckResult> DnsAsync(string host, int timeoutMs) => Task.FromResult(Success($"dns:{host}", timeoutMs));

        public Task<CheckResult> TcpAsync(string host, int port, int timeoutMs)
        {
            TcpHost = host;
            TcpPort = port;
            TcpTimeoutMs = timeoutMs;
            return Task.FromResult(Success($"tcp:{host}:{port}", timeoutMs));
        }

        public Task<CheckResult> UdpAsync(string host, int port, string payload, int timeoutMs, bool expectResponse) => Task.FromResult(Success($"udp:{host}:{port}", timeoutMs));

        public Task<CheckResult> HttpAsync(Uri uri, string method, int timeoutMs, string? body, string contentType, IEnumerable<string> headers) => Task.FromResult(Success($"http:{uri}", timeoutMs));

        private static CheckResult Success(string check, int durationMs)
        {
            return new CheckResult
            {
                Check = check,
                Success = true,
                Message = "ok",
                DurationMs = durationMs,
                Data = null
            };
        }
    }
}
