namespace IntegrationConnector.Core.Entities;

/// <summary>Resultado da checagem periódica de conectividade de um conector (job recorrente).</summary>
public class ConnectorHealthCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectorId { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public long? LatencyMs { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
