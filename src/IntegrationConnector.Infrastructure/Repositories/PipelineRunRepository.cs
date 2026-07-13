using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Repositories;

public class PipelineRunRepository : IPipelineRunRepository
{
    private readonly IntegrationDbContext _db;
    public PipelineRunRepository(IntegrationDbContext db) => _db = db;

    public Task<PipelineRun?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.PipelineRuns.Include(x => x.Logs).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<PipelineRun>> GetByPipelineIdAsync(Guid pipelineId, int take = 50, CancellationToken ct = default)
        => _db.PipelineRuns
            .Where(x => x.PipelineId == pipelineId)
            .OrderByDescending(x => x.StartedAt)
            .Take(take)
            .ToListAsync(ct);

    public Task<List<PipelineRun>> GetRecentFailuresAsync(int take = 50, CancellationToken ct = default)
        => _db.PipelineRuns
            .Where(x => x.Status == PipelineRunStatus.Failed)
            .OrderByDescending(x => x.StartedAt)
            .Take(take)
            .ToListAsync(ct);

    public Task<List<PipelineRun>> SearchAsync(PipelineRunSearchFilter filter, CancellationToken ct = default)
    {
        var query = _db.PipelineRuns.AsQueryable();

        if (filter.PipelineId.HasValue) query = query.Where(x => x.PipelineId == filter.PipelineId.Value);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.From.HasValue) query = query.Where(x => x.StartedAt >= filter.From.Value);
        if (filter.To.HasValue) query = query.Where(x => x.StartedAt <= filter.To.Value);
        if (!string.IsNullOrWhiteSpace(filter.ErrorContains))
            query = query.Where(x => x.ErrorMessage != null && x.ErrorMessage.Contains(filter.ErrorContains));

        return query.OrderByDescending(x => x.StartedAt).Take(filter.Take).ToListAsync(ct);
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

    public async Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        var oldRuns = await _db.PipelineRuns.Where(x => x.StartedAt < cutoffUtc).ToListAsync(ct);
        _db.PipelineRuns.RemoveRange(oldRuns);
        return oldRuns.Count;
    }

    public async Task AddAsync(PipelineRun run, CancellationToken ct = default)
        => await _db.PipelineRuns.AddAsync(run, ct);

    public void Update(PipelineRun run) => _db.PipelineRuns.Update(run);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
