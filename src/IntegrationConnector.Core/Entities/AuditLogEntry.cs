namespace IntegrationConnector.Core.Entities;

/// <summary>Registro de auditoria de ações administrativas (criação/edição/remoção/publicação/execução manual).</summary>
public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Username { get; set; } = "anonymous";
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? Details { get; set; }
}
