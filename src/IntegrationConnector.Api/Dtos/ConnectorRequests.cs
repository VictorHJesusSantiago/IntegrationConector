using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Api.Dtos;

public record CreateConnectorRequest(string Name, string? Description, ConnectorType Type, string ConfigurationJson);
public record UpdateConnectorRequest(string Name, string? Description, string ConfigurationJson, bool IsActive);
