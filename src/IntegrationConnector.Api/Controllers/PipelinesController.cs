using Hangfire;
using IntegrationConnector.Api.Dtos;
using IntegrationConnector.Api.Jobs;
using IntegrationConnector.Api.Validation;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace IntegrationConnector.Api.Controllers;

[ApiController]
[Route("api/pipelines")]
public class PipelinesController : ControllerBase
{
    private readonly IPipelineRepository _repository;
    private readonly IConnectorRepository _connectorRepository;
    private readonly IPipelineSchedulerService _scheduler;
    private readonly IAuditLogRepository _auditLog;

    public PipelinesController(
        IPipelineRepository repository,
        IConnectorRepository connectorRepository,
        IPipelineSchedulerService scheduler,
        IAuditLogRepository auditLog)
    {
        _repository = repository;
        _connectorRepository = connectorRepository;
        _scheduler = scheduler;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<List<Pipeline>>> GetAll(CancellationToken ct)
        => Ok(await _repository.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Pipeline>> GetById(Guid id, CancellationToken ct)
    {
        var pipeline = await _repository.GetByIdWithVersionsAsync(id, ct);
        return pipeline is null ? NotFound() : Ok(pipeline);
    }

    [HttpPost]
    public async Task<ActionResult<Pipeline>> Create(CreatePipelineRequest request, CancellationToken ct)
    {
        var validationErrors = await PipelineDefinitionValidator.ValidateAsync(request.Definition, _connectorRepository, ct);
        if (validationErrors.Count > 0)
        {
            var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
            foreach (var error in validationErrors) modelState.AddModelError("Definition", error);
            return ValidationProblem(modelState);
        }

        var pipeline = new Pipeline
        {
            Name = request.Name,
            Description = request.Description,
            TriggerType = request.TriggerType,
            CronExpression = request.CronExpression,
            IntervalSeconds = request.IntervalSeconds,
            ActiveVersionNumber = 1,
            WebhookToken = request.TriggerType == PipelineTriggerType.Webhook ? Guid.NewGuid().ToString("N") : null
        };

        pipeline.Versions.Add(new PipelineVersion
        {
            PipelineId = pipeline.Id,
            VersionNumber = 1,
            DefinitionJson = JsonConvert.SerializeObject(request.Definition),
            ChangeNotes = "Versão inicial",
            Status = PipelineVersionStatus.Published
        });

        await _repository.AddAsync(pipeline, ct);
        await _repository.SaveChangesAsync(ct);

        _scheduler.Sync(pipeline);
        await AuditAsync("Create", pipeline.Id, pipeline.Name, ct);
        return CreatedAtAction(nameof(GetById), new { id = pipeline.Id }, pipeline);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Pipeline>> Update(Guid id, UpdatePipelineRequest request, CancellationToken ct)
    {
        var pipeline = await _repository.GetByIdWithVersionsAsync(id, ct);
        if (pipeline is null) return NotFound();

        pipeline.Name = request.Name;
        pipeline.Description = request.Description;
        pipeline.IsEnabled = request.IsEnabled;
        pipeline.TriggerType = request.TriggerType;
        pipeline.CronExpression = request.CronExpression;
        pipeline.IntervalSeconds = request.IntervalSeconds;

        if (request.TriggerType == PipelineTriggerType.Webhook && string.IsNullOrWhiteSpace(pipeline.WebhookToken))
            pipeline.WebhookToken = Guid.NewGuid().ToString("N");

        _repository.Update(pipeline);
        await _repository.SaveChangesAsync(ct);

        _scheduler.Sync(pipeline);
        await AuditAsync("Update", pipeline.Id, pipeline.Name, ct);
        return Ok(pipeline);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var pipeline = await _repository.GetByIdAsync(id, ct);
        if (pipeline is null) return NotFound();

        _repository.Remove(pipeline);
        await _repository.SaveChangesAsync(ct);

        _scheduler.Remove(id);
        await AuditAsync("Delete", id, pipeline.Name, ct);
        return NoContent();
    }

    /// <summary>Encadeia este pipeline para disparar outro automaticamente após sucesso (ou remove o encadeamento com null).</summary>
    [HttpPut("{id:guid}/chain")]
    public async Task<ActionResult<Pipeline>> SetChain(Guid id, [FromBody] Guid? nextPipelineId, CancellationToken ct)
    {
        var pipeline = await _repository.GetByIdAsync(id, ct);
        if (pipeline is null) return NotFound();

        pipeline.NextPipelineId = nextPipelineId;
        _repository.Update(pipeline);
        await _repository.SaveChangesAsync(ct);
        return Ok(pipeline);
    }

    /// <summary>Publica uma nova versão da definição do pipeline, preservando o histórico das anteriores.</summary>
    [HttpPost("{id:guid}/versions")]
    public async Task<ActionResult<PipelineVersion>> PublishVersion(Guid id, PublishPipelineVersionRequest request, CancellationToken ct)
    {
        var pipeline = await _repository.GetByIdWithVersionsAsync(id, ct);
        if (pipeline is null) return NotFound();

        var validationErrors = await PipelineDefinitionValidator.ValidateAsync(request.Definition, _connectorRepository, ct);
        if (validationErrors.Count > 0)
        {
            var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
            foreach (var error in validationErrors) modelState.AddModelError("Definition", error);
            return ValidationProblem(modelState);
        }

        var nextVersion = pipeline.Versions.Count == 0 ? 1 : pipeline.Versions.Max(v => v.VersionNumber) + 1;
        var version = new PipelineVersion
        {
            PipelineId = pipeline.Id,
            VersionNumber = nextVersion,
            DefinitionJson = JsonConvert.SerializeObject(request.Definition),
            ChangeNotes = request.ChangeNotes,
            Status = PipelineVersionStatus.Published
        };

        pipeline.Versions.Add(version);
        pipeline.ActiveVersionNumber = nextVersion;

        _repository.Update(pipeline);
        await _repository.SaveChangesAsync(ct);
        await AuditAsync("PublishVersion", pipeline.Id, $"{pipeline.Name} v{nextVersion}", ct);
        return Ok(version);
    }

    /// <summary>Reverte o pipeline para uma versão anteriormente publicada (rollback).</summary>
    [HttpPost("{id:guid}/versions/{versionNumber:int}/activate")]
    public async Task<ActionResult<Pipeline>> ActivateVersion(Guid id, int versionNumber, CancellationToken ct)
    {
        var pipeline = await _repository.GetByIdWithVersionsAsync(id, ct);
        if (pipeline is null) return NotFound();
        if (!pipeline.Versions.Any(v => v.VersionNumber == versionNumber))
            return BadRequest($"Versão {versionNumber} não existe para este pipeline.");

        pipeline.ActiveVersionNumber = versionNumber;
        _repository.Update(pipeline);
        await _repository.SaveChangesAsync(ct);
        await AuditAsync("ActivateVersion", pipeline.Id, $"{pipeline.Name} v{versionNumber}", ct);
        return Ok(pipeline);
    }

    /// <summary>Dispara a execução manual do pipeline em segundo plano (via Hangfire) e retorna imediatamente.</summary>
    [HttpPost("{id:guid}/run")]
    public async Task<IActionResult> RunNow(Guid id, CancellationToken ct)
    {
        var jobId = BackgroundJob.Enqueue<PipelineJob>(job => job.RunAsync(id, "manual", false, null, CancellationToken.None));
        await AuditAsync("RunManual", id, null, ct);
        return Accepted(new { backgroundJobId = jobId });
    }

    /// <summary>Executa leitura + transformação sem gravar no destino, para inspecionar o resultado antes de publicar.</summary>
    [HttpPost("{id:guid}/dry-run")]
    public IActionResult DryRun(Guid id)
    {
        var jobId = BackgroundJob.Enqueue<PipelineJob>(job => job.RunAsync(id, "dry-run", true, null, CancellationToken.None));
        return Accepted(new { backgroundJobId = jobId });
    }

    private async Task AuditAsync(string action, Guid entityId, string? details, CancellationToken ct)
    {
        await _auditLog.AddAsync(new Core.Entities.AuditLogEntry
        {
            Username = User.Identity?.Name ?? "anonymous",
            Action = action,
            EntityType = "Pipeline",
            EntityId = entityId,
            Details = details
        }, ct);
        await _auditLog.SaveChangesAsync(ct);
    }
}
