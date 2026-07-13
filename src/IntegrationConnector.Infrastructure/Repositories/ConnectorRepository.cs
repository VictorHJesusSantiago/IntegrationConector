using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Repositories;

public class ConnectorRepository : IConnectorRepository
{
    private readonly IntegrationDbContext _db;
    public ConnectorRepository(IntegrationDbContext db) => _db = db;

    public Task<Connector?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Connectors.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<Connector>> GetAllAsync(CancellationToken ct = default)
        => _db.Connectors.OrderBy(x => x.Name).ToListAsync(ct);

    public async Task AddAsync(Connector connector, CancellationToken ct = default)
        => await _db.Connectors.AddAsync(connector, ct);

    public void Update(Connector connector)
    {
        connector.UpdatedAt = DateTime.UtcNow;
        _db.Connectors.Update(connector);
    }

    public void Remove(Connector connector) => _db.Connectors.Remove(connector);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
