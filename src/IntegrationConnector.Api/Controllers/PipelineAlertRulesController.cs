using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationConnector.Api.Controllers;

/// <summary>Regras de alerta por e-mail para falhas consecutivas de pipeline.</summary>
[ApiController]
[Route("api/pipeline-alert-rules")]
public class PipelineAlertRulesController : ControllerBase
{
    private readonly IPipelineAlertRuleRepository _repository;

    public PipelineAlertRulesController(IPipelineAlertRuleRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("by-pipeline/{pipelineId:guid}")]
    public async Task<ActionResult<List<PipelineAlertRule>>> GetByPipeline(Guid pipelineId, CancellationToken ct)
        => Ok(await _repository.GetByPipelineIdAsync(pipelineId, ct));

    [HttpPost]
    public async Task<ActionResult<PipelineAlertRule>> Create(PipelineAlertRule rule, CancellationToken ct)
    {
        await _repository.AddAsync(rule, ct);
        await _repository.SaveChangesAsync(ct);
        return Ok(rule);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PipelineAlertRule>> Update(Guid id, PipelineAlertRule rule, CancellationToken ct)
    {
        rule.Id = id;
        _repository.Update(rule);
        await _repository.SaveChangesAsync(ct);
        return Ok(rule);
    }
}
