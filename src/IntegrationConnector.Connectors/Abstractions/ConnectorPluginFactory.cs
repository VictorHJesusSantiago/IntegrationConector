using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;

namespace IntegrationConnector.Connectors.Abstractions;

/// <summary>
/// Resolve o <see cref="IConnectorPlugin"/> apropriado para cada <see cref="ConnectorType"/>.
/// Novos tipos de conector são adicionados apenas registrando a implementação no DI —
/// nenhuma mudança é necessária no motor de execução (padrão plugin).
/// </summary>
public class ConnectorPluginFactory : IConnectorPluginFactory
{
    private readonly IEnumerable<IConnectorPlugin> _plugins;

    public ConnectorPluginFactory(IEnumerable<IConnectorPlugin> plugins)
    {
        _plugins = plugins;
    }

    public IConnectorPlugin Resolve(ConnectorType type)
        => _plugins.FirstOrDefault(p => p.Type == type)
           ?? throw new NotSupportedException($"Nenhum plugin registrado para o tipo de conector '{type}'.");
}
