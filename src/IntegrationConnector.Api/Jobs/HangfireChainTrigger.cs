using Hangfire;
using IntegrationConnector.Core.Interfaces;

namespace IntegrationConnector.Api.Jobs;

/// <summary>Dispara o próximo pipeline encadeado como um novo job em segundo plano via Hangfire.</summary>
public class HangfireChainTrigger : IPipelineChainTrigger
{
    public void TriggerNext(Guid nextPipelineId)
        => BackgroundJob.Enqueue<PipelineJob>(job => job.RunAsync(nextPipelineId, "chained", false, null, CancellationToken.None));
}
