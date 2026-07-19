using IntegrationConnector.Core.Entities;

namespace IntegrationConnector.Core.Interfaces;

public interface IConnectorRepository
{
    Task<Connector?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Connector>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Connector connector, CancellationToken ct = default);
    void Update(Connector connector);
    void Remove(Connector connector);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IPipelineRepository
{
    Task<Pipeline?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Pipeline?> GetByIdWithVersionsAsync(Guid id, CancellationToken ct = default);
    Task<List<Pipeline>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Pipeline pipeline, CancellationToken ct = default);
    void Update(Pipeline pipeline);
    void Remove(Pipeline pipeline);

    /// <summary>
    /// Rastreia explicitamente uma nova <see cref="PipelineVersion"/> como "Added" ao publicar em um
    /// Pipeline já existente/rastreado. Mesmo motivo de <see cref="IPipelineRunRepository.AddLog"/>:
    /// evita que o EF Core marque a versão nova como "Modified" pela heurística de chave não-default.
    /// </summary>
    void AddVersion(PipelineVersion version);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IPipelineRunRepository
{
    Task<PipelineRun?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<PipelineRun>> GetByPipelineIdAsync(Guid pipelineId, int take = 50, CancellationToken ct = default);
    Task<List<PipelineRun>> GetRecentFailuresAsync(int take = 50, CancellationToken ct = default);
    Task<List<PipelineRun>> SearchAsync(PipelineRunSearchFilter filter, CancellationToken ct = default);
    Task<PipelineRunStats> GetStatsAsync(CancellationToken ct = default);
    Task<int> CountRecentConsecutiveFailuresAsync(Guid pipelineId, CancellationToken ct = default);
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
    Task AddAsync(PipelineRun run, CancellationToken ct = default);
    void Update(PipelineRun run);

    /// <summary>
    /// Rastreia explicitamente um novo <see cref="PipelineRunLog"/> como "Added". Necessário porque
    /// seu Id é um Guid gerado no cliente (não-default): se apenas adicionado à coleção de navegação
    /// de um PipelineRun já rastreado, o EF Core o marcaria incorretamente como "Modified" (heurística
    /// de chave), gerando um UPDATE para uma linha inexistente.
    /// </summary>
    void AddLog(PipelineRunLog log);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public class PipelineRunSearchFilter
{
    public Guid? PipelineId { get; set; }
    public Core.Enums.PipelineRunStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? ErrorContains { get; set; }
    public int Take { get; set; } = 50;
}

public class PipelineRunStats
{
    public int TotalRuns { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Running { get; set; }
    public double SuccessRatePercent { get; set; }
}

public interface IDeadLetterRepository
{
    Task AddAsync(DeadLetterRecord record, CancellationToken ct = default);
    Task<List<DeadLetterRecord>> GetByRunIdAsync(Guid runId, CancellationToken ct = default);
    Task<List<DeadLetterRecord>> GetByPipelineIdAsync(Guid pipelineId, bool onlyPending = true, CancellationToken ct = default);
    Task<DeadLetterRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Update(DeadLetterRecord record);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken ct = default);
    Task<List<AuditLogEntry>> GetRecentAsync(int take = 100, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<User>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    void Update(User user);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IConnectorHealthRepository
{
    Task AddAsync(ConnectorHealthCheck check, CancellationToken ct = default);
    Task<ConnectorHealthCheck?> GetLatestAsync(Guid connectorId, CancellationToken ct = default);
    Task<Dictionary<Guid, ConnectorHealthCheck>> GetLatestForAllAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IPipelineAlertRuleRepository
{
    Task<List<PipelineAlertRule>> GetByPipelineIdAsync(Guid pipelineId, CancellationToken ct = default);
    Task<List<PipelineAlertRule>> GetAllEnabledAsync(CancellationToken ct = default);
    Task AddAsync(PipelineAlertRule rule, CancellationToken ct = default);
    void Update(PipelineAlertRule rule);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IIdempotencyRepository
{
    Task<bool> ExistsAsync(Guid pipelineId, string keyHash, CancellationToken ct = default);
    Task MarkProcessedAsync(Guid pipelineId, string keyHash, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
