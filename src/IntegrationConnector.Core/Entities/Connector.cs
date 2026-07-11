using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Core.Entities;

/// <summary>
/// Representa um conector plugável (REST, SOAP, FTP, banco ou fila).
/// A configuração é armazenada como JSON para permitir esquemas distintos por tipo.
/// </summary>
public class Connector
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ConnectorType Type { get; set; }

    /// <summary>JSON com a configuração específica do tipo de conector (ex.: BaseUrl, Auth, ConnectionString).</summary>
    public string ConfigurationJson { get; set; } = "{}";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
