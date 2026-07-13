using System.Collections.Concurrent;
using IntegrationConnector.Core.Interfaces;

namespace IntegrationConnector.Engine;

/// <summary>Implementação em memória (singleton) do registro de cancelamento de execuções.</summary>
public class PipelineRunCancellationRegistry : IPipelineRunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();

    public CancellationTokenSource Register(Guid runId)
    {
        var cts = new CancellationTokenSource();
        _tokens[runId] = cts;
        return cts;
    }

    public bool Cancel(Guid runId)
    {
        if (!_tokens.TryGetValue(runId, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    public void Remove(Guid runId)
    {
        if (_tokens.TryRemove(runId, out var cts)) cts.Dispose();
    }
}
