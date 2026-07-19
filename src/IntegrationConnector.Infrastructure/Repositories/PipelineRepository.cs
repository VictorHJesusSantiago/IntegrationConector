using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Repositories;

public class PipelineRepository : IPipelineRepository
{
    private readonly IntegrationDbContext _db;
    public PipelineRepository(IntegrationDbContext db) => _db = db;

    public Task<Pipeline?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Pipelines.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Pipeline?> GetByIdWithVersionsAsync(Guid id, CancellationToken ct = default)
        => _db.Pipelines.Include(x => x.Versions).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<Pipeline>> GetAllAsync(CancellationToken ct = default)
        => _db.Pipelines.OrderBy(x => x.Name).ToListAsync(ct);

    public async Task AddAsync(Pipeline pipeline, CancellationToken ct = default)
        => await _db.Pipelines.AddAsync(pipeline, ct);

    public void Update(Pipeline pipeline)
    {
        pipeline.UpdatedAt = DateTime.UtcNow;
        _db.Pipelines.Update(pipeline);
    }

    public void Remove(Pipeline pipeline) => _db.Pipelines.Remove(pipeline);

    public void AddVersion(PipelineVersion version) => _db.PipelineVersions.Add(version);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
