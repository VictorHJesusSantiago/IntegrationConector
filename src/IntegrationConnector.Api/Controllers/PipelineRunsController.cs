using System.Text;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace IntegrationConnector.Api.Controllers;

/// <summary>Endpoints de observabilidade: histórico de execuções, busca avançada, falhas recentes, cancelamento e exportação.</summary>
[ApiController]
[Route("api/pipeline-runs")]
public class PipelineRunsController : ControllerBase
{
    private readonly IPipelineRunRepository _runRepository;
    private readonly IPipelineRunCancellationRegistry _cancellationRegistry;

    public PipelineRunsController(IPipelineRunRepository runRepository, IPipelineRunCancellationRegistry cancellationRegistry)
    {
        _runRepository = runRepository;
        _cancellationRegistry = cancellationRegistry;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PipelineRun>> GetById(Guid id, CancellationToken ct)
    {
        var run = await _runRepository.GetByIdAsync(id, ct);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("by-pipeline/{pipelineId:guid}")]
    public async Task<ActionResult<List<PipelineRun>>> GetByPipeline(Guid pipelineId, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _runRepository.GetByPipelineIdAsync(pipelineId, take, ct));

    /// <summary>Anônimo por design: alimenta o dashboard estático em /dashboard.html. Restrinja via proxy/rede em produção.</summary>
    [AllowAnonymous]
    [HttpGet("failures")]
    public async Task<ActionResult<List<PipelineRun>>> GetRecentFailures([FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _runRepository.GetRecentFailuresAsync(take, ct));

    /// <summary>Busca avançada por pipeline, status, período e texto no erro.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<PipelineRun>>> Search(
        [FromQuery] Guid? pipelineId,
        [FromQuery] PipelineRunStatus? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? errorContains,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var filter = new PipelineRunSearchFilter
        {
            PipelineId = pipelineId,
            Status = status,
            From = from,
            To = to,
            ErrorContains = errorContains,
            Take = take
        };
        return Ok(await _runRepository.SearchAsync(filter, ct));
    }

    /// <summary>Anônimo por design: alimenta o dashboard estático em /dashboard.html. Restrinja via proxy/rede em produção.</summary>
    [AllowAnonymous]
    [HttpGet("stats")]
    public async Task<ActionResult<PipelineRunStats>> GetStats(CancellationToken ct)
        => Ok(await _runRepository.GetStatsAsync(ct));

    /// <summary>Sinaliza cancelamento cooperativo de uma execução em andamento.</summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/cancel")]
    public IActionResult Cancel(Guid id)
        => _cancellationRegistry.Cancel(id) ? Accepted() : NotFound("Execução não está em andamento ou já finalizou.");

    /// <summary>Exporta os logs de uma execução em JSON ou CSV.</summary>
    [HttpGet("{id:guid}/logs/export")]
    public async Task<IActionResult> ExportLogs(Guid id, [FromQuery] string format = "json", CancellationToken ct = default)
    {
        var run = await _runRepository.GetByIdAsync(id, ct);
        if (run is null) return NotFound();

        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder("Timestamp,Level,Step,Message\n");
            foreach (var log in run.Logs.OrderBy(l => l.Timestamp))
                sb.AppendLine($"{log.Timestamp:o},{log.Level},{log.Step},\"{log.Message.Replace("\"", "\"\"")}\"");
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"run-{id}-logs.csv");
        }

        var json = JsonConvert.SerializeObject(run.Logs.OrderBy(l => l.Timestamp), Formatting.Indented);
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"run-{id}-logs.json");
    }
}
