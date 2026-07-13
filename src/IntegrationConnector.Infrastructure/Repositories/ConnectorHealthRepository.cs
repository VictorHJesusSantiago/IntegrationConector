using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Repositories;

public class ConnectorHealthRepository : IConnectorHealthRepository
{
    private readonly IntegrationDbContext _db;
    public ConnectorHealthRepository(IntegrationDbContext db) => _db = db;

    public async Task AddAsync(ConnectorHealthCheck check, CancellationToken ct = default)
        => await _db.ConnectorHealthChecks.AddAsync(check, ct);

    public Task<ConnectorHealthCheck?> GetLatestAsync(Guid connectorId, CancellationToken ct = default)
        => _db.ConnectorHealthChecks
            .Where(x => x.ConnectorId == connectorId)
            .OrderByDescending(x => x.CheckedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<Dictionary<Guid, ConnectorHealthCheck>> GetLatestForAllAsync(CancellationToken ct = default)
    {
        var all = await _db.ConnectorHealthChecks.ToListAsync(ct);
        return all
            .GroupBy(x => x.ConnectorId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CheckedAt).First());
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
