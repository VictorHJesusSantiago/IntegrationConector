using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Repositories;

public class IdempotencyRepository : IIdempotencyRepository
{
    private readonly IntegrationDbContext _db;
    public IdempotencyRepository(IntegrationDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid pipelineId, string keyHash, CancellationToken ct = default)
        => _db.IdempotencyRecords.AnyAsync(x => x.PipelineId == pipelineId && x.KeyHash == keyHash, ct);

    public async Task MarkProcessedAsync(Guid pipelineId, string keyHash, CancellationToken ct = default)
        => await _db.IdempotencyRecords.AddAsync(new IdempotencyRecord { PipelineId = pipelineId, KeyHash = keyHash }, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
