namespace IntegrationConnector.Core.Entities;

/// <summary>Registro individual que falhou na transformação/escrita durante uma execução, disponível para reprocessamento manual.</summary>
public class DeadLetterRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineId { get; set; }
    public Guid PipelineRunId { get; set; }
    public string RecordJson { get; set; } = "{}";
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Reprocessed { get; set; }
    public DateTime? ReprocessedAt { get; set; }
}
