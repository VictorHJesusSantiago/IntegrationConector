namespace IntegrationConnector.Core.Entities;

/// <summary>Linha de log estruturado de uma execução, usada nos painéis de observabilidade.</summary>
public class PipelineRunLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineRunId { get; set; }
    public PipelineRun? PipelineRun { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "Information";
    public string Step { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
