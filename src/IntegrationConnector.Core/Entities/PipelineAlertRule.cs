namespace IntegrationConnector.Core.Entities;

/// <summary>Regra de alerta: dispara e-mail (SMTP local) quando um pipeline acumula N falhas consecutivas.</summary>
public class PipelineAlertRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineId { get; set; }
    public int ConsecutiveFailuresThreshold { get; set; } = 3;
    public string NotifyEmail { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastTriggeredAt { get; set; }
}
