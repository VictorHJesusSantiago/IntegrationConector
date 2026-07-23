using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Api.Dtos;

public record CreateConnectorRequest(string Name, string? Description, ConnectorType Type, string ConfigurationJson);
public record UpdateConnectorRequest(string Name, string? Description, string ConfigurationJson, bool IsActive);

/// <summary>
/// Representação de um conector para saída da API. Existe para que a entidade <c>Connector</c> nunca
/// seja serializada diretamente: o <c>ConfigurationJson</c> persistido contém os segredos cifrados, e
/// devolvê-lo — ainda que cifrado — expõe comprimento aproximado, permite comparar segredos entre
/// conectores e entrega material a ataques offline. Aqui os campos sensíveis saem como "***".
/// </summary>
public record ConnectorResponse(
    Guid Id,
    string Name,
    string? Description,
    ConnectorType Type,
    string ConfigurationJson,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public static ConnectorResponse FromEntity(Core.Entities.Connector connector, Core.Interfaces.ISecretProtector secretProtector) => new(
        connector.Id,
        connector.Name,
        connector.Description,
        connector.Type,
        secretProtector.Redact(connector.ConfigurationJson),
        connector.IsActive,
        connector.CreatedAt,
        connector.UpdatedAt);
}
