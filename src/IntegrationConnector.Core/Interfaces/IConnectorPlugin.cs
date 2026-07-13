using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Core.Interfaces;

/// <summary>
/// Contrato comum implementado por todo conector plugável.
/// Cada implementação recebe a configuração já resolvida do <see cref="Connector"/> e executa
/// leitura ou escrita a partir de uma <see cref="ConnectorOperation"/>.
/// </summary>
public interface IConnectorPlugin
{
    ConnectorType Type { get; }

    /// <summary>Lê dados da origem e retorna o payload como JSON (array ou objeto).</summary>
    Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct);

    /// <summary>Escreve o payload JSON transformado no destino.</summary>
    Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct);

    /// <summary>Testa a conectividade/configuração do conector.</summary>
    Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct);
}

public class ConnectorTestResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public long? LatencyMs { get; set; }
}

/// <summary>Resolve o plugin correto a partir do tipo do conector (padrão Factory + Strategy).</summary>
public interface IConnectorPluginFactory
{
    IConnectorPlugin Resolve(ConnectorType type);
}
