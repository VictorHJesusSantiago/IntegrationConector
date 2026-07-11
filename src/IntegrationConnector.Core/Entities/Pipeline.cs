using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Core.Entities;

/// <summary>
/// Um fluxo de integração (pipeline). O conteúdo executável fica em <see cref="PipelineVersion"/>,
/// permitindo versionamento e publicação controlada.
/// </summary>
public class Pipeline
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;

    public PipelineTriggerType TriggerType { get; set; } = PipelineTriggerType.Manual;

    /// <summary>Expressão cron (quando TriggerType = Cron).</summary>
    public string? CronExpression { get; set; }

    /// <summary>Intervalo em segundos (quando TriggerType = Interval).</summary>
    public int? IntervalSeconds { get; set; }

    public int ActiveVersionNumber { get; set; } = 1;

    /// <summary>Token secreto usado para autenticar chamadas de webhook de entrada (quando TriggerType = Webhook).</summary>
    public string? WebhookToken { get; set; }

    /// <summary>Pipeline disparado automaticamente após a conclusão bem-sucedida deste (encadeamento).</summary>
    public Guid? NextPipelineId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<PipelineVersion> Versions { get; set; } = new();
    public List<PipelineRun> Runs { get; set; } = new();
}
