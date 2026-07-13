using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Repositories;

public class DeadLetterRepository : IDeadLetterRepository
{
    private readonly IntegrationDbContext _db;
    public DeadLetterRepository(IntegrationDbContext db) => _db = db;

    public async Task AddAsync(DeadLetterRecord record, CancellationToken ct = default)
        => await _db.DeadLetterRecords.AddAsync(record, ct);

    public Task<List<DeadLetterRecord>> GetByRunIdAsync(Guid runId, CancellationToken ct = default)
        => _db.DeadLetterRecords.Where(x => x.PipelineRunId == runId).OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public Task<List<DeadLetterRecord>> GetByPipelineIdAsync(Guid pipelineId, bool onlyPending = true, CancellationToken ct = default)
        => _db.DeadLetterRecords
            .Where(x => x.PipelineId == pipelineId && (!onlyPending || !x.Reprocessed))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public Task<DeadLetterRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.DeadLetterRecords.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Update(DeadLetterRecord record) => _db.DeadLetterRecords.Update(record);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
