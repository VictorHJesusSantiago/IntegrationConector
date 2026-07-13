using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Repositories;

public class PipelineAlertRuleRepository : IPipelineAlertRuleRepository
{
    private readonly IntegrationDbContext _db;
    public PipelineAlertRuleRepository(IntegrationDbContext db) => _db = db;

    public Task<List<PipelineAlertRule>> GetByPipelineIdAsync(Guid pipelineId, CancellationToken ct = default)
        => _db.PipelineAlertRules.Where(x => x.PipelineId == pipelineId).ToListAsync(ct);

    public Task<List<PipelineAlertRule>> GetAllEnabledAsync(CancellationToken ct = default)
        => _db.PipelineAlertRules.Where(x => x.IsEnabled).ToListAsync(ct);

    public async Task AddAsync(PipelineAlertRule rule, CancellationToken ct = default)
        => await _db.PipelineAlertRules.AddAsync(rule, ct);

    public void Update(PipelineAlertRule rule) => _db.PipelineAlertRules.Update(rule);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
