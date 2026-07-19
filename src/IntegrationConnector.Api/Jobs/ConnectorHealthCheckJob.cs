using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;

namespace IntegrationConnector.Api.Jobs;

/// <summary>Job recorrente (Hangfire) que testa a conectividade de todos os conectores ativos e registra o resultado.</summary>
public class ConnectorHealthCheckJob
{
    private readonly IConnectorRepository _connectorRepository;
    private readonly IConnectorHealthRepository _healthRepository;
    private readonly IConnectorPluginFactory _pluginFactory;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<ConnectorHealthCheckJob> _logger;

    public ConnectorHealthCheckJob(
        IConnectorRepository connectorRepository,
        IConnectorHealthRepository healthRepository,
        IConnectorPluginFactory pluginFactory,
        ISecretProtector secretProtector,
        ILogger<ConnectorHealthCheckJob> logger)
    {
        _connectorRepository = connectorRepository;
        _healthRepository = healthRepository;
        _pluginFactory = pluginFactory;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var connectors = await _connectorRepository.GetAllAsync(ct);
        foreach (var connector in connectors.Where(c => c.IsActive))
        {
            try
            {
                var decrypted = connector.WithDecryptedConfig(_secretProtector);
                var plugin = _pluginFactory.Resolve(connector.Type);
                var result = await plugin.TestConnectionAsync(decrypted, ct);

                await _healthRepository.AddAsync(new Core.Entities.ConnectorHealthCheck
                {
                    ConnectorId = connector.Id,
                    Success = result.Success,
                    Message = result.Message,
                    LatencyMs = result.LatencyMs
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao checar saúde do conector {ConnectorId}", connector.Id);
                await _healthRepository.AddAsync(new Core.Entities.ConnectorHealthCheck
                {
                    ConnectorId = connector.Id,
                    Success = false,
                    Message = ex.Message
                }, ct);
            }
        }

        await _healthRepository.SaveChangesAsync(ct);
    }
}
