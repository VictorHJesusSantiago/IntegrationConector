using IntegrationConnector.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace IntegrationConnector.Infrastructure.Data;

public class IntegrationDbContext : DbContext
{
    public IntegrationDbContext(DbContextOptions<IntegrationDbContext> options) : base(options) { }

    public DbSet<Connector> Connectors => Set<Connector>();
    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<PipelineVersion> PipelineVersions => Set<PipelineVersion>();
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<PipelineRunLog> PipelineRunLogs => Set<PipelineRunLog>();
    public DbSet<DeadLetterRecord> DeadLetterRecords => Set<DeadLetterRecord>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ConnectorHealthCheck> ConnectorHealthChecks => Set<ConnectorHealthCheck>();
    public DbSet<PipelineAlertRule> PipelineAlertRules => Set<PipelineAlertRule>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Connector>(e =>
        {
            e.ToTable("connectors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.ConfigurationJson).IsRequired();
            e.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<Pipeline>(e =>
        {
            e.ToTable("pipelines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(x => x.Name);
            e.HasMany(x => x.Versions)
                .WithOne(v => v.Pipeline)
                .HasForeignKey(v => v.PipelineId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Runs)
                .WithOne(r => r.Pipeline)
                .HasForeignKey(r => r.PipelineId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PipelineVersion>(e =>
        {
            e.ToTable("pipeline_versions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PipelineId, x.VersionNumber }).IsUnique();
        });

        modelBuilder.Entity<PipelineRun>(e =>
        {
            e.ToTable("pipeline_runs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StartedAt);
            e.HasIndex(x => x.Status);
            e.HasMany(x => x.Logs)
                .WithOne(l => l.PipelineRun)
                .HasForeignKey(l => l.PipelineRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PipelineRunLog>(e =>
        {
            e.ToTable("pipeline_run_logs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PipelineRunId);
        });

        modelBuilder.Entity<DeadLetterRecord>(e =>
        {
            e.ToTable("dead_letter_records");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PipelineId);
            e.HasIndex(x => x.PipelineRunId);
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log_entries");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<ConnectorHealthCheck>(e =>
        {
            e.ToTable("connector_health_checks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConnectorId, x.CheckedAt });
        });

        modelBuilder.Entity<PipelineAlertRule>(e =>
        {
            e.ToTable("pipeline_alert_rules");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PipelineId);
        });

        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_records");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PipelineId, x.KeyHash }).IsUnique();
        });
    }
}
