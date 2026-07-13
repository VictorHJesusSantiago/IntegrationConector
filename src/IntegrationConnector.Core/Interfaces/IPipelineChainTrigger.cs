namespace IntegrationConnector.Core.Interfaces;

/// <summary>Dispara a execução do próximo pipeline encadeado após o sucesso do pipeline atual.</summary>
public interface IPipelineChainTrigger
{
    void TriggerNext(Guid nextPipelineId);
}

/// <summary>Registro em memória dos tokens de cancelamento das execuções em andamento.</summary>
public interface IPipelineRunCancellationRegistry
{
    CancellationTokenSource Register(Guid runId);
    bool Cancel(Guid runId);
    void Remove(Guid runId);
}
