using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Infrastructure.Data;
using IntegrationConnector.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationConnector.Tests;

/// <summary>
/// Testes de integração com um banco real (SQLite em memória) em vez de mocks — necessários para
/// pegar problemas do próprio EF Core (ex.: heurística de Added vs Modified para entidades novas
/// com chave gerada no cliente, adicionadas à coleção de navegação de uma entidade já rastreada),
/// que testes com repositórios mockados nunca exercitam de verdade.
/// </summary>
public class PipelineRunRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IntegrationDbContext _db;

    public PipelineRunRepositoryIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new IntegrationDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AddLog_OnAlreadyTrackedRun_PersistsAsNewRow_NotAsFailedUpdate()
    {
        var repository = new PipelineRunRepository(_db);

        var pipeline = new Pipeline { Name = "pipeline-teste" };
        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync();

        var run = new PipelineRun { PipelineId = pipeline.Id, Status = PipelineRunStatus.Running };
        await repository.AddAsync(run);
        await repository.SaveChangesAsync();

        // Simula o que PipelineExecutor faz: com o mesmo DbContext/run já rastreado (Unchanged),
        // adiciona um novo log e persiste novamente — sem usar AddLog(), o EF Core marcaria o log
        // como "Modified" (Guid não-default) e o SaveChanges geraria um UPDATE para linha inexistente.
        var log = new PipelineRunLog { PipelineRunId = run.Id, Level = "Information", Step = "read", Message = "teste" };
        run.Logs.Add(log);
        repository.AddLog(log);

        run.Status = PipelineRunStatus.Succeeded;
        run.FinishedAt = DateTime.UtcNow;

        var affectedRows = await repository.SaveChangesAsync();

        Assert.True(affectedRows > 0);

        var reloaded = await repository.GetByIdAsync(run.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(PipelineRunStatus.Succeeded, reloaded!.Status);
        Assert.Single(reloaded.Logs);
        Assert.Equal("teste", reloaded.Logs.First().Message);
    }

    [Fact]
    public async Task AddLog_WithoutExplicitTracking_WouldBeMisclassified_RegressionGuard()
    {
        // Este teste documenta o bug original: adicionar o log SOMENTE à coleção de navegação
        // (sem repository.AddLog) faz o EF Core tratá-lo como "Modified" e falhar ao salvar,
        // já que a linha ainda não existe. Serve de guarda de regressão para o fix aplicado.
        var repository = new PipelineRunRepository(_db);

        var pipeline = new Pipeline { Name = "pipeline-teste" };
        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync();

        var run = new PipelineRun { PipelineId = pipeline.Id, Status = PipelineRunStatus.Running };
        await repository.AddAsync(run);
        await repository.SaveChangesAsync();

        run.Logs.Add(new PipelineRunLog { PipelineRunId = run.Id, Level = "Information", Step = "read", Message = "sem tracking explicito" });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repository.SaveChangesAsync());
    }
}
