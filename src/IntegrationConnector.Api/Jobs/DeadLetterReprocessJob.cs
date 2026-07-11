using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Transformation;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IntegrationConnector.Api.Jobs;

/// <summary>Reprocessa um único registro de dead-letter em segundo plano (usado no reprocessamento em lote).</summary>
public class DeadLetterReprocessJob
{
    private readonly IDeadLetterRepository _deadLetterRepository;
    private readonly IPipelineRepository _pipelineRepository;
    private readonly IConnectorRepository _connectorRepository;
    private readonly IConnectorPluginFactory _pluginFactory;
    private readonly ISecretProtector _secretProtector;
    private readonly IDataTransformer _transformer;
    private readonly ILogger<DeadLetterReprocessJob> _logger;

    public DeadLetterReprocessJob(
        IDeadLetterRepository deadLetterRepository,
        IPipelineRepository pipelineRepository,
        IConnectorRepository connectorRepository,
        IConnectorPluginFactory pluginFactory,
        ISecretProtector secretProtector,
        IDataTransformer transformer,
        ILogger<DeadLetterReprocessJob> logger)
    {
        _deadLetterRepository = deadLetterRepository;
        _pipelineRepository = pipelineRepository;
        _connectorRepository = connectorRepository;
        _pluginFactory = pluginFactory;
        _secretProtector = secretProtector;
        _transformer = transformer;
        _logger = logger;
    }

    public async Task ReprocessAsync(Guid deadLetterId, CancellationToken ct)
    {
        var record = await _deadLetterRepository.GetByIdAsync(deadLetterId, ct);
        if (record is null || record.Reprocessed) return;

        var pipeline = await _pipelineRepository.GetByIdWithVersionsAsync(record.PipelineId, ct);
        var version = pipeline?.Versions.FirstOrDefault(v => v.VersionNumber == pipeline.ActiveVersionNumber);
        if (version is null) return;

        var definition = JsonConvert.DeserializeObject<PipelineDefinition>(version.DefinitionJson)!;
        var targetConnector = await _connectorRepository.GetByIdAsync(definition.TargetConnectorId, ct);
        if (targetConnector is null) return;

        targetConnector.ConfigurationJson = _secretProtector.Unprotect(targetConnector.ConfigurationJson);
        var plugin = _pluginFactory.Resolve(targetConnector.Type);
        var transformed = _transformer.Transform(record.RecordJson, definition.Mappings);

        try
        {
            await plugin.WriteAsync(targetConnector, definition.TargetOperation, transformed, ct);
            record.Reprocessed = true;
            record.ReprocessedAt = DateTime.UtcNow;
            _deadLetterRepository.Update(record);
            await _deadLetterRepository.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao reprocessar dead-letter {Id}", deadLetterId);
        }
    }
}
