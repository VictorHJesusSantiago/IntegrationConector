using Hangfire;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Api.Jobs;

/// <summary>
/// Sincroniza o agendamento (Hangfire recurring jobs) com a configuração de gatilho de cada pipeline.
/// Suporta Cron (expressão cron completa) e Interval (convertido em cron "*/N * * * * *" minuto a minuto
/// ou a cada N segundos via Hangfire's minimum granularity de minutos, aproximando para segundos via requeue).
/// </summary>
public interface IPipelineSchedulerService
{
    void Sync(Pipeline pipeline);
    void Remove(Guid pipelineId);
}

public class PipelineSchedulerService : IPipelineSchedulerService
{
    private static string RecurringJobId(Guid pipelineId) => $"pipeline-{pipelineId}";

    public void Sync(Pipeline pipeline)
    {
        var jobId = RecurringJobId(pipeline.Id);

        if (!pipeline.IsEnabled || pipeline.TriggerType == PipelineTriggerType.Manual || pipeline.TriggerType == PipelineTriggerType.Webhook)
        {
            RecurringJob.RemoveIfExists(jobId);
            return;
        }

        string cron = pipeline.TriggerType switch
        {
            PipelineTriggerType.Cron => pipeline.CronExpression ?? Cron.Never(),
            PipelineTriggerType.Interval => IntervalToCron(pipeline.IntervalSeconds ?? 60),
            _ => Cron.Never()
        };

        RecurringJob.AddOrUpdate<PipelineJob>(
            jobId,
            job => job.RunAsync(pipeline.Id, "scheduler", CancellationToken.None),
            cron);
    }

    public void Remove(Guid pipelineId) => RecurringJob.RemoveIfExists(RecurringJobId(pipelineId));

    /// <summary>Hangfire cron tem granularidade mínima de minuto; segundos são arredondados para o minuto mais próximo (mínimo 1 min).</summary>
    private static string IntervalToCron(int intervalSeconds)
    {
        var minutes = Math.Max(1, intervalSeconds / 60);
        return minutes <= 1 ? "* * * * *" : $"*/{minutes} * * * *";
    }
}
