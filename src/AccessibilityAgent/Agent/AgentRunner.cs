using System;
using System.Threading;
using System.Threading.Tasks;

namespace AccessibilityAgent.Agent;

internal sealed class AgentRunner : IAsyncDisposable
{
    private readonly SignalRAgentRunner _inner;

    public AgentRunner(AgentOptions options, AgentJobExecutor? executor = null)
    {
        _inner = new SignalRAgentRunner(options, executor);
    }

    public Task RunAsync(CancellationToken cancellationToken) => _inner.RunAsync(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
