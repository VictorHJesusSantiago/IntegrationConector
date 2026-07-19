using IntegrationConnector.Api.Dtos;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IntegrationConnector.Api.Controllers;

/// <summary>
/// Governança de versionamento: diff entre versões, workflow de aprovação (Draft -&gt; InReview -&gt; Published),
/// exportação/importação de pipelines como bundle JSON portável e clonagem.
/// </summary>
[ApiController]
[Route("api/pipelines/{pipelineId:guid}")]
public class PipelineGovernanceController : ControllerBase
{
    private readonly IPipelineRepository _repository;

    public PipelineGovernanceController(IPipelineRepository repository)
    {
        _repository = repository;
    }

    /// <summary>Compara duas versões campo a campo (o que foi adicionado, removido ou alterado).</summary>
    [HttpGet("versions/{v1:int}/diff/{v2:int}")]
    public async Task<IActionResult> Diff(Guid pipelineId, int v1, int v2, CancellationToken ct)
    {
        var pipeline = await _repository.GetByIdWithVersionsAsync(pipelineId, ct);
        if (pipeline is null) return NotFound();

        var version1 = pipeline.Versions.FirstOrDefault(x => x.VersionNumber == v1);
        var version2 = pipeline.Versions.FirstOrDefault(x => x.VersionNumber == v2);
        if (version1 is null || version2 is null) return NotFound("Uma das versões informadas não existe.");

        var diff = ComputeJsonDiff(JObject.Parse(version1.DefinitionJson), JObject.Parse(version2.DefinitionJson));
        return Ok(new { fromVersion = v1, toVersion = v2, changes = diff });
    }

    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("versions/submit-for-review")]
    public async Task<IActionResult> SubmitForReview(Guid pipelineId, SubmitForReviewRequest request, CancellationToken ct)
    {
        var pipeline = await _repository.GetByIdWithVersionsAsync(pipelineId, ct);
        var version = pipeline?.Versions.FirstOrDefault(v => v.VersionNumber == request.VersionNumber);
        if (version is null) return NotFound();

        version.Status = PipelineVersionStatus.InReview;
        _repository.Update(pipeline!);
        await _repository.SaveChangesAsync(ct);
        return Ok(version);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("versions/approve")]
    public async Task<IActionResult> Approve(Guid pipelineId, ApproveVersionRequest request, CancellationToken ct)
    {
        var pipeline = await _repository.GetByIdWithVersionsAsync(pipelineId, ct);
        var version = pipeline?.Versions.FirstOrDefault(v => v.VersionNumber == request.VersionNumber);
        if (version is null) return NotFound();

        version.Status = PipelineVersionStatus.Published;
        version.ReviewedBy = request.ReviewedBy;
        version.ReviewedAt = DateTime.UtcNow;
        pipeline!.ActiveVersionNumber = version.VersionNumber;

        _repository.Update(pipeline);
        await _repository.SaveChangesAsync(ct);
        return Ok(version);
    }

    /// <summary>Exporta a versão ativa do pipeline como um bundle JSON portável entre ambientes.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(Guid pipelineId, CancellationToken ct)
    {
        var pipeline = await _repository.GetByIdWithVersionsAsync(pipelineId, ct);
        if (pipeline is null) return NotFound();

        var activeVersion = pipeline.Versions.First(v => v.VersionNumber == pipeline.ActiveVersionNumber);
        var definition = JsonConvert.DeserializeObject<PipelineDefinition>(activeVersion.DefinitionJson)!;

        var bundle = new PipelineExportBundle(pipeline.Name, pipeline.Description, pipeline.TriggerType, pipeline.CronExpression, pipeline.IntervalSeconds, definition);
        return Ok(bundle);
    }

    /// <summary>Cria um novo pipeline a partir de um bundle exportado (portabilidade entre ambientes).</summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("~/api/pipelines/import")]
    public async Task<ActionResult<Pipeline>> Import(PipelineExportBundle bundle, CancellationToken ct)
    {
        var pipeline = new Pipeline
        {
            Name = $"{bundle.Name} (importado)",
            Description = bundle.Description,
            TriggerType = bundle.TriggerType,
            CronExpression = bundle.CronExpression,
            IntervalSeconds = bundle.IntervalSeconds,
            ActiveVersionNumber = 1
        };

        pipeline.Versions.Add(new PipelineVersion
        {
            PipelineId = pipeline.Id,
            VersionNumber = 1,
            DefinitionJson = JsonConvert.SerializeObject(bundle.Definition),
            ChangeNotes = "Importado via bundle"
        });

        await _repository.AddAsync(pipeline, ct);
        await _repository.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Export), new { pipelineId = pipeline.Id }, pipeline);
    }

    /// <summary>Clona o pipeline (com a definição da versão ativa) como ponto de partida para um novo fluxo.</summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("clone")]
    public async Task<ActionResult<Pipeline>> Clone(Guid pipelineId, CancellationToken ct)
    {
        var source = await _repository.GetByIdWithVersionsAsync(pipelineId, ct);
        if (source is null) return NotFound();

        var activeVersion = source.Versions.First(v => v.VersionNumber == source.ActiveVersionNumber);

        var clone = new Pipeline
        {
            Name = $"{source.Name} (cópia)",
            Description = source.Description,
            TriggerType = PipelineTriggerType.Manual,
            ActiveVersionNumber = 1,
            IsEnabled = false
        };

        clone.Versions.Add(new PipelineVersion
        {
            PipelineId = clone.Id,
            VersionNumber = 1,
            DefinitionJson = activeVersion.DefinitionJson,
            ChangeNotes = $"Clonado de '{source.Name}' (v{activeVersion.VersionNumber})"
        });

        await _repository.AddAsync(clone, ct);
        await _repository.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Export), new { pipelineId = clone.Id }, clone);
    }

    private static List<string> ComputeJsonDiff(JObject left, JObject right)
    {
        var changes = new List<string>();
        var allKeys = left.Properties().Select(p => p.Name).Union(right.Properties().Select(p => p.Name)).Distinct();

        foreach (var key in allKeys)
        {
            var leftValue = left[key];
            var rightValue = right[key];

            if (leftValue is null) changes.Add($"+ {key}: {rightValue}");
            else if (rightValue is null) changes.Add($"- {key}");
            else if (!JToken.DeepEquals(leftValue, rightValue)) changes.Add($"~ {key}: {leftValue} -> {rightValue}");
        }

        return changes;
    }
}
