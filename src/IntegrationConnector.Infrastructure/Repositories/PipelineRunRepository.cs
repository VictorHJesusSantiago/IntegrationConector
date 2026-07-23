using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Repositories;

public class PipelineRunRepository : IPipelineRunRepository
{
    /// <summary>
    /// Teto rígido de linhas por consulta. O "take" chega da query string sem validação; sem este
    /// limite, um único GET com take=10000000 materializa a tabela inteira em memória — negação de
    /// serviço trivial e não autenticada de fato.
    /// </summary>
    private const int MaxTake = 500;

    private readonly IntegrationDbContext _db;
    public PipelineRunRepository(IntegrationDbContext db) => _db = db;

    private static int ClampTake(int take) => take switch
    {
        < 1 => 1,
        > MaxTake => MaxTake,
        _ => take
    };

    // Somente esta consulta traz os logs: é a que alimenta o detalhe/exportação de uma execução.
    // As listagens abaixo NÃO fazem Include(Logs) de propósito — uma execução pode acumular centenas
    // de logs, e trazê-los para montar uma grade de 50 linhas multiplica o volume lido por ordens de
    // grandeza (e ainda faz o EF materializar o join cartesiano inteiro em memória).
    public Task<PipelineRun?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.PipelineRuns.Include(x => x.Logs).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<PipelineRun>> GetByPipelineIdAsync(Guid pipelineId, int take = 50, CancellationToken ct = default)
        => _db.PipelineRuns
            .AsNoTracking()
            .Where(x => x.PipelineId == pipelineId)
            .OrderByDescending(x => x.StartedAt)
            .Take(ClampTake(take))
            .ToListAsync(ct);

    public Task<List<PipelineRun>> GetRecentFailuresAsync(int take = 50, CancellationToken ct = default)
        => _db.PipelineRuns
            .AsNoTracking()
            .Where(x => x.Status == PipelineRunStatus.Failed)
            .OrderByDescending(x => x.StartedAt)
            .Take(ClampTake(take))
            .ToListAsync(ct);

    public Task<List<PipelineRun>> SearchAsync(PipelineRunSearchFilter filter, CancellationToken ct = default)
    {
        var query = _db.PipelineRuns.AsNoTracking().AsQueryable();

        if (filter.PipelineId.HasValue) query = query.Where(x => x.PipelineId == filter.PipelineId.Value);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.From.HasValue) query = query.Where(x => x.StartedAt >= filter.From.Value);
        if (filter.To.HasValue) query = query.Where(x => x.StartedAt <= filter.To.Value);
        if (!string.IsNullOrWhiteSpace(filter.ErrorContains))
            query = query.Where(x => x.ErrorMessage != null && x.ErrorMessage.Contains(filter.ErrorContains));

        return query.OrderByDescending(x => x.StartedAt).Take(ClampTake(filter.Take)).ToListAsync(ct);
    }

    public async Task<PipelineRunStats> GetStatsAsync(CancellationToken ct = default)
    {
        var total = await _db.PipelineRuns.CountAsync(ct);
        var succeeded = await _db.PipelineRuns.CountAsync(x => x.Status == PipelineRunStatus.Succeeded, ct);
        var failed = await _db.PipelineRuns.CountAsync(x => x.Status == PipelineRunStatus.Failed, ct);
        var running = await _db.PipelineRuns.CountAsync(x => x.Status == PipelineRunStatus.Running || x.Status == PipelineRunStatus.Retrying, ct);

        return new PipelineRunStats
        {
            TotalRuns = total,
            Succeeded = succeeded,
            Failed = failed,
            Running = running,
            SuccessRatePercent = total == 0 ? 0 : Math.Round(succeeded * 100.0 / total, 2)
        };
    }

    public async Task<int> CountRecentConsecutiveFailuresAsync(Guid pipelineId, CancellationToken ct = default)
    {
        var recentRuns = await _db.PipelineRuns
            .Where(x => x.PipelineId == pipelineId && x.Status != PipelineRunStatus.Running && x.Status != PipelineRunStatus.Retrying)
            .OrderByDescending(x => x.StartedAt)
            .Take(20)
            .Select(x => x.Status)
            .ToListAsync(ct);

        int count = 0;
        foreach (var status in recentRuns)
        {
            if (status == PipelineRunStatus.Failed) count++;
            else break;
        }
        return count;
    }

    /// <summary>
    /// Remove execuções anteriores ao corte via DELETE em lote no servidor.
    ///
    /// A versão anterior materializava TODA execução expirada em memória (com change tracking) só
    /// para marcá-las como removidas — num banco com meses de histórico isso é um OutOfMemory
    /// esperando acontecer, justamente no job que deveria conter o crescimento da tabela.
    /// Os PipelineRunLog associados são removidos pelo cascade configurado no DbContext.
    /// </summary>
    public Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
        => _db.PipelineRuns.Where(x => x.StartedAt < cutoffUtc).ExecuteDeleteAsync(ct);

    public async Task AddAsync(PipelineRun run, CancellationToken ct = default)
        => await _db.PipelineRuns.AddAsync(run, ct);

    public void Update(PipelineRun run) => _db.PipelineRuns.Update(run);

    public void AddLog(PipelineRunLog log) => _db.PipelineRunLogs.Add(log);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
