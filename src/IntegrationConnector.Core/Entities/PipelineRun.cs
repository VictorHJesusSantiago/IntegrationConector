using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Core.Entities;

/// <summary>Registro de execução de um pipeline, usado para monitoramento e observabilidade.</summary>
public class PipelineRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineId { get; set; }
    public Pipeline? Pipeline { get; set; }

    public int PipelineVersionNumber { get; set; }
    public PipelineRunStatus Status { get; set; } = PipelineRunStatus.Pending;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public int AttemptCount { get; set; }
    public int RecordsRead { get; set; }
    public int RecordsWritten { get; set; }
    public int RecordsFailed { get; set; }

    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }

    /// <summary>Origem do disparo: manual, cron, interval, webhook.</summary>
    public string TriggerSource { get; set; } = "manual";

    /// <summary>Indica se a execução foi em modo dry-run (não gravou no destino).</summary>
    public bool IsDryRun { get; set; }

    public List<PipelineRunLog> Logs { get; set; } = new();
}
