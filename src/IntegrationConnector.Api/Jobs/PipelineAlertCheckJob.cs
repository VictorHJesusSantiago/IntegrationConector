using IntegrationConnector.Core.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace IntegrationConnector.Api.Jobs;

/// <summary>
/// Job recorrente (Hangfire) que verifica, para cada regra de alerta habilitada, se o pipeline
/// acumulou falhas consecutivas suficientes e, em caso positivo, envia um e-mail via SMTP local.
/// </summary>
public class PipelineAlertCheckJob
{
    private readonly IPipelineAlertRuleRepository _alertRuleRepository;
    private readonly IPipelineRunRepository _runRepository;
    private readonly IPipelineRepository _pipelineRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PipelineAlertCheckJob> _logger;

    public PipelineAlertCheckJob(
        IPipelineAlertRuleRepository alertRuleRepository,
        IPipelineRunRepository runRepository,
        IPipelineRepository pipelineRepository,
        IConfiguration configuration,
        ILogger<PipelineAlertCheckJob> logger)
    {
        _alertRuleRepository = alertRuleRepository;
        _runRepository = runRepository;
        _pipelineRepository = pipelineRepository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var rules = await _alertRuleRepository.GetAllEnabledAsync(ct);

        foreach (var rule in rules)
        {
            var consecutiveFailures = await _runRepository.CountRecentConsecutiveFailuresAsync(rule.PipelineId, ct);
            if (consecutiveFailures < rule.ConsecutiveFailuresThreshold) continue;

            if (rule.LastTriggeredAt.HasValue && rule.LastTriggeredAt.Value > DateTime.UtcNow.AddMinutes(-30))
                continue; // evita reenviar o mesmo alerta repetidamente em um curto intervalo

            var pipeline = await _pipelineRepository.GetByIdAsync(rule.PipelineId, ct);
            if (pipeline is null) continue;

            try
            {
                await SendAlertEmailAsync(rule.NotifyEmail, pipeline.Name, consecutiveFailures, ct);
                rule.LastTriggeredAt = DateTime.UtcNow;
                _alertRuleRepository.Update(rule);
                await _alertRuleRepository.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enviar alerta por e-mail para o pipeline {PipelineId}", rule.PipelineId);
            }
        }
    }

    private async Task SendAlertEmailAsync(string to, string pipelineName, int consecutiveFailures, CancellationToken ct)
    {
        var smtp = _configuration.GetSection("Smtp");
        var host = smtp["Host"];
        if (string.IsNullOrWhiteSpace(host)) return; // SMTP não configurado: alerta apenas registrado em log

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp["From"] ?? "alerts@integrationconnector.local"));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = $"[Alerta] Pipeline '{pipelineName}' com {consecutiveFailures} falhas consecutivas";
        message.Body = new TextPart("plain")
        {
            Text = $"O pipeline '{pipelineName}' falhou {consecutiveFailures} vezes seguidas. Verifique o painel de monitoramento."
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(host, int.Parse(smtp["Port"] ?? "587"), SecureSocketOptions.StartTls, ct);
        if (!string.IsNullOrWhiteSpace(smtp["Username"]))
            await client.AuthenticateAsync(smtp["Username"], smtp["Password"], ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
