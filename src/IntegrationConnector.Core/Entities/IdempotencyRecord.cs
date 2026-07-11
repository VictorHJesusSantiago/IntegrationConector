namespace IntegrationConnector.Core.Entities;

/// <summary>Chave de idempotência já processada por um pipeline, usada para evitar gravação duplicada em reprocessamentos.</summary>
public class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineId { get; set; }
    public string KeyHash { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
