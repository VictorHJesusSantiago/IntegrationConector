using Hangfire;
using IntegrationConnector.Api.Jobs;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Transformation;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace IntegrationConnector.Api.Controllers;

/// <summary>Consulta e reprocessamento seletivo de registros que falharam durante execuções de pipeline.</summary>
[ApiController]
[Route("api/dead-letters")]
public class DeadLettersController : ControllerBase
{
    private readonly IDeadLetterRepository _deadLetterRepository;
    private readonly IPipelineRepository _pipelineRepository;
    private readonly IConnectorRepository _connectorRepository;
    private readonly IConnectorPluginFactory _pluginFactory;
    private readonly ISecretProtector _secretProtector;
    private readonly IDataTransformer _transformer;

    public DeadLettersController(
        IDeadLetterRepository deadLetterRepository,
        IPipelineRepository pipelineRepository,
        IConnectorRepository connectorRepository,
        IConnectorPluginFactory pluginFactory,
        ISecretProtector secretProtector,
        IDataTransformer transformer)
    {
        _deadLetterRepository = deadLetterRepository;
        _pipelineRepository = pipelineRepository;
        _connectorRepository = connectorRepository;
        _pluginFactory = pluginFactory;
        _secretProtector = secretProtector;
        _transformer = transformer;
    }

    [HttpGet("by-pipeline/{pipelineId:guid}")]
    public async Task<ActionResult<List<DeadLetterRecord>>> GetByPipeline(Guid pipelineId, [FromQuery] bool onlyPending = true, CancellationToken ct = default)
        => Ok(await _deadLetterRepository.GetByPipelineIdAsync(pipelineId, onlyPending, ct));

    [HttpGet("by-run/{runId:guid}")]
    public async Task<ActionResult<List<DeadLetterRecord>>> GetByRun(Guid runId, CancellationToken ct)
        => Ok(await _deadLetterRepository.GetByRunIdAsync(runId, ct));

    /// <summary>
    /// Reprocessa um registro específico do dead-letter: reaplica o mapeamento vigente do pipeline
    /// (mesma versão ativa) e regrava diretamente no conector de destino, sem repetir a leitura da origem.
    /// </summary>
    [HttpPost("{id:guid}/reprocess")]
    public async Task<IActionResult> Reprocess(Guid id, CancellationToken ct)
    {
        var record = await _deadLetterRepository.GetByIdAsync(id, ct);
        if (record is null) return NotFound();
        if (record.Reprocessed) return Conflict("Registro já foi reprocessado.");

        var pipeline = await _pipelineRepository.GetByIdWithVersionsAsync(record.PipelineId, ct);
        if (pipeline is null) return NotFound("Pipeline não encontrado.");

        var version = pipeline.Versions.FirstOrDefault(v => v.VersionNumber == pipeline.ActiveVersionNumber);
        if (version is null) return BadRequest("Pipeline sem versão ativa.");

        var definition = JsonConvert.DeserializeObject<PipelineDefinition>(version.DefinitionJson)!;
        var targetConnector = await _connectorRepository.GetByIdAsync(definition.TargetConnectorId, ct);
        if (targetConnector is null) return BadRequest("Conector de destino não encontrado.");

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
            return Ok(record);
        }
        catch (Exception ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    /// <summary>Reprocessa em lote todos os registros pendentes de uma execução, em segundo plano.</summary>
    [HttpPost("by-run/{runId:guid}/reprocess-all")]
    public async Task<IActionResult> ReprocessAll(Guid runId, CancellationToken ct)
    {
        var records = await _deadLetterRepository.GetByRunIdAsync(runId, ct);
        var pending = records.Where(r => !r.Reprocessed).ToList();

        foreach (var record in pending)
            BackgroundJob.Enqueue<DeadLetterReprocessJob>(job => job.ReprocessAsync(record.Id, CancellationToken.None));

        return Accepted(new { queuedCount = pending.Count });
    }
}
