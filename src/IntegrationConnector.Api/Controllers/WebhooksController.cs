using System.Security.Cryptography;
using System.Text;
using Hangfire;
using IntegrationConnector.Api.Jobs;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationConnector.Api.Controllers;

/// <summary>
/// Endpoint HTTP dedicado para gatilhos externos (webhooks): o corpo da requisição é usado
/// diretamente como payload de origem do pipeline, pulando a etapa de leitura do conector de origem.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class WebhooksController : ControllerBase
{
    private readonly IPipelineRepository _pipelineRepository;

    public WebhooksController(IPipelineRepository pipelineRepository)
    {
        _pipelineRepository = pipelineRepository;
    }

    [HttpPost("{pipelineId:guid}/{token}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Trigger(Guid pipelineId, string token, CancellationToken ct)
    {
        var pipeline = await _pipelineRepository.GetByIdAsync(pipelineId, ct);
        if (pipeline is null) return NotFound();
        if (pipeline.TriggerType != PipelineTriggerType.Webhook)
            return Problem(detail: "Este pipeline não está configurado para gatilho via webhook.", statusCode: StatusCodes.Status400BadRequest, title: "Gatilho incompatível");
        if (!pipeline.IsEnabled)
            return Problem(detail: "Pipeline desabilitado.", statusCode: StatusCodes.Status409Conflict, title: "Pipeline desabilitado");
        if (!IsWebhookTokenValid(pipeline.WebhookToken, token)) return Unauthorized();

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);

        var jobId = BackgroundJob.Enqueue<PipelineJob>(job => job.RunAsync(pipelineId, "webhook", false, body, CancellationToken.None));
        return Accepted(new { backgroundJobId = jobId });
    }

    /// <summary>
    /// Compara o token do webhook em tempo constante. Uma comparação de string comum ("!=") aborta no
    /// primeiro caractere divergente, e essa diferença de tempo é mensurável em rede: um atacante pode
    /// recuperar o token caractere a caractere. FixedTimeEquals elimina esse canal lateral.
    /// </summary>
    private static bool IsWebhookTokenValid(string? expectedToken, string? providedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken) || string.IsNullOrWhiteSpace(providedToken)) return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        var providedBytes = Encoding.UTF8.GetBytes(providedToken);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
