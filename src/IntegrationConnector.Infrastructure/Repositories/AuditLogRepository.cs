using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly IntegrationDbContext _db;
    public AuditLogRepository(IntegrationDbContext db) => _db = db;

    public async Task AddAsync(AuditLogEntry entry, CancellationToken ct = default)
        => await _db.AuditLogEntries.AddAsync(entry, ct);

    public Task<List<AuditLogEntry>> GetRecentAsync(int take = 100, CancellationToken ct = default)
        => _db.AuditLogEntries.OrderByDescending(x => x.Timestamp).Take(take).ToListAsync(ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
