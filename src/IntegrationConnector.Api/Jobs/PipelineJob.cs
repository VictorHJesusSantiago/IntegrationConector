using IntegrationConnector.Engine;

namespace IntegrationConnector.Api.Jobs;

/// <summary>Job invocado pelo Hangfire (agendamento cron/intervalo) para disparar a execução de um pipeline.</summary>
public class PipelineJob
{
    private readonly IPipelineExecutor _executor;
    private readonly ILogger<PipelineJob> _logger;

    public PipelineJob(IPipelineExecutor executor, ILogger<PipelineJob> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(Guid pipelineId, string triggerSource, CancellationToken ct)
    {
        _logger.LogInformation("Disparando pipeline {PipelineId} via {Trigger}", pipelineId, triggerSource);
        await _executor.ExecuteAsync(pipelineId, triggerSource, ct);
    }

    public async Task RunAsync(Guid pipelineId, string triggerSource, bool dryRun, string? seedPayloadJson, CancellationToken ct)
    {
        _logger.LogInformation("Disparando pipeline {PipelineId} via {Trigger} (dryRun={DryRun})", pipelineId, triggerSource, dryRun);
        await _executor.ExecuteAsync(pipelineId, triggerSource, dryRun, seedPayloadJson, ct);
    }
}
