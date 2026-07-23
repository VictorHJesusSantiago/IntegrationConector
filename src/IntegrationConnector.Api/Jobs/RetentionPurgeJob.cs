using System.Globalization;
using IntegrationConnector.Core.Interfaces;

namespace IntegrationConnector.Api.Jobs;

/// <summary>Job recorrente (Hangfire) que purga execuções antigas conforme a retenção configurada.</summary>
public class RetentionPurgeJob
{
    private readonly IPipelineRunRepository _runRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RetentionPurgeJob> _logger;

    public RetentionPurgeJob(IPipelineRunRepository runRepository, IConfiguration configuration, ILogger<RetentionPurgeJob> logger)
    {
        _runRepository = runRepository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var days = int.Parse(_configuration["Retention:PipelineRunRetentionDays"] ?? "90", CultureInfo.InvariantCulture);
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var purged = await _runRepository.PurgeOlderThanAsync(cutoff, ct);
        await _runRepository.SaveChangesAsync(ct);
        _logger.LogInformation("Retenção: {Count} execuções anteriores a {Cutoff} removidas.", purged, cutoff);
    }
}
