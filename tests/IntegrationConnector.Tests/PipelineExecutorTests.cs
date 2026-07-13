using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Engine;
using IntegrationConnector.Transformation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace IntegrationConnector.Tests;

public class PipelineExecutorTests
{
    private static (Pipeline pipeline, Connector source, Connector target) BuildPipeline(RetryPolicySpec? retry = null)
    {
        var source = new Connector { Id = Guid.NewGuid(), Type = ConnectorType.Rest, Name = "origem" };
        var target = new Connector { Id = Guid.NewGuid(), Type = ConnectorType.Rest, Name = "destino" };

        var definition = new PipelineDefinition
        {
            SourceConnectorId = source.Id,
            TargetConnectorId = target.Id,
            SourceOperation = new ConnectorOperation { Action = "GET", Target = "/dados" },
            TargetOperation = new ConnectorOperation { Action = "POST", Target = "/destino" },
            Mappings = new List<MappingRule> { new() { SourcePath = "$.nome", TargetPath = "nome" } },
            RetryPolicy = retry ?? new RetryPolicySpec { MaxAttempts = 2, BaseDelaySeconds = 0 }
        };

        var pipeline = new Pipeline { Id = Guid.NewGuid(), Name = "teste", IsEnabled = true, ActiveVersionNumber = 1 };
        pipeline.Versions.Add(new PipelineVersion
        {
            PipelineId = pipeline.Id,
            VersionNumber = 1,
            DefinitionJson = JsonConvert.SerializeObject(definition)
        });

        return (pipeline, source, target);
    }

    private static PipelineExecutor BuildExecutor(
        Mock<IPipelineRepository> pipelineRepo,
        Mock<IPipelineRunRepository> runRepo,
        Mock<IConnectorRepository> connectorRepo,
        Mock<IConnectorPluginFactory> pluginFactory)
    {
        var deadLetterRepo = new Mock<IDeadLetterRepository>();
        deadLetterRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var idempotencyRepo = new Mock<IIdempotencyRepository>();
        idempotencyRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var secretProtector = new Mock<ISecretProtector>();
        secretProtector.Setup(s => s.Unprotect(It.IsAny<string>())).Returns((string s) => s);

        var chainTrigger = new Mock<IPipelineChainTrigger>();
        var cancellationRegistry = new PipelineRunCancellationRegistry();

        return new PipelineExecutor(
            pipelineRepo.Object, runRepo.Object, connectorRepo.Object, pluginFactory.Object,
            new DataTransformer(), deadLetterRepo.Object, idempotencyRepo.Object,
            secretProtector.Object, chainTrigger.Object, cancellationRegistry,
            NullLogger<PipelineExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_MarksRunAsSucceeded_WhenReadWriteSucceed()
    {
        var (pipeline, source, target) = BuildPipeline();

        var pipelineRepo = new Mock<IPipelineRepository>();
        pipelineRepo.Setup(r => r.GetByIdWithVersionsAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);

        var connectorRepo = new Mock<IConnectorRepository>();
        connectorRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        connectorRepo.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var plugin = new Mock<IConnectorPlugin>();
        plugin.Setup(p => p.Type).Returns(ConnectorType.Rest);
        plugin.Setup(p => p.ReadAsync(source, It.IsAny<ConnectorOperation>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("{\"nome\":\"Ana\"}");
        plugin.Setup(p => p.WriteAsync(target, It.IsAny<ConnectorOperation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var pluginFactory = new Mock<IConnectorPluginFactory>();
        pluginFactory.Setup(f => f.Resolve(ConnectorType.Rest)).Returns(plugin.Object);

        PipelineRun? savedRun = null;
        var runRepo = new Mock<IPipelineRunRepository>();
        runRepo.Setup(r => r.AddAsync(It.IsAny<PipelineRun>(), It.IsAny<CancellationToken>()))
               .Callback<PipelineRun, CancellationToken>((run, _) => savedRun = run)
               .Returns(Task.CompletedTask);
        runRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var executor = BuildExecutor(pipelineRepo, runRepo, connectorRepo, pluginFactory);

        await executor.ExecuteAsync(pipeline.Id, "manual");

        Assert.NotNull(savedRun);
        Assert.Equal(PipelineRunStatus.Succeeded, savedRun!.Status);
        Assert.Equal(1, savedRun.RecordsRead);
        Assert.Equal(1, savedRun.RecordsWritten);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesAndFails_WhenWriteAlwaysThrows()
    {
        var (pipeline, source, target) = BuildPipeline(new RetryPolicySpec { MaxAttempts = 3, BaseDelaySeconds = 0 });

        var pipelineRepo = new Mock<IPipelineRepository>();
        pipelineRepo.Setup(r => r.GetByIdWithVersionsAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);

        var connectorRepo = new Mock<IConnectorRepository>();
        connectorRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        connectorRepo.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var plugin = new Mock<IConnectorPlugin>();
        plugin.Setup(p => p.Type).Returns(ConnectorType.Rest);
        plugin.Setup(p => p.ReadAsync(source, It.IsAny<ConnectorOperation>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("{\"nome\":\"Ana\"}");
        plugin.Setup(p => p.WriteAsync(target, It.IsAny<ConnectorOperation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("destino indisponível"));

        var pluginFactory = new Mock<IConnectorPluginFactory>();
        pluginFactory.Setup(f => f.Resolve(ConnectorType.Rest)).Returns(plugin.Object);

        PipelineRun? savedRun = null;
        var runRepo = new Mock<IPipelineRunRepository>();
        runRepo.Setup(r => r.AddAsync(It.IsAny<PipelineRun>(), It.IsAny<CancellationToken>()))
               .Callback<PipelineRun, CancellationToken>((run, _) => savedRun = run)
               .Returns(Task.CompletedTask);
        runRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var executor = BuildExecutor(pipelineRepo, runRepo, connectorRepo, pluginFactory);

        await executor.ExecuteAsync(pipeline.Id, "manual");

        Assert.NotNull(savedRun);
        Assert.Equal(PipelineRunStatus.Failed, savedRun!.Status);
        Assert.Equal(3, savedRun.AttemptCount);
        plugin.Verify(p => p.WriteAsync(target, It.IsAny<ConnectorOperation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_DoesNotCallWrite()
    {
        var (pipeline, source, target) = BuildPipeline();

        var pipelineRepo = new Mock<IPipelineRepository>();
        pipelineRepo.Setup(r => r.GetByIdWithVersionsAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);

        var connectorRepo = new Mock<IConnectorRepository>();
        connectorRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        connectorRepo.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var plugin = new Mock<IConnectorPlugin>();
        plugin.Setup(p => p.Type).Returns(ConnectorType.Rest);
        plugin.Setup(p => p.ReadAsync(source, It.IsAny<ConnectorOperation>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("{\"nome\":\"Ana\"}");

        var pluginFactory = new Mock<IConnectorPluginFactory>();
        pluginFactory.Setup(f => f.Resolve(ConnectorType.Rest)).Returns(plugin.Object);

        PipelineRun? savedRun = null;
        var runRepo = new Mock<IPipelineRunRepository>();
        runRepo.Setup(r => r.AddAsync(It.IsAny<PipelineRun>(), It.IsAny<CancellationToken>()))
               .Callback<PipelineRun, CancellationToken>((run, _) => savedRun = run)
               .Returns(Task.CompletedTask);
        runRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var executor = BuildExecutor(pipelineRepo, runRepo, connectorRepo, pluginFactory);

        await executor.ExecuteAsync(pipeline.Id, "manual", dryRun: true, seedPayloadJson: null);

        Assert.NotNull(savedRun);
        Assert.True(savedRun!.IsDryRun);
        Assert.Equal(PipelineRunStatus.Succeeded, savedRun.Status);
        Assert.Equal(0, savedRun.RecordsWritten);
        plugin.Verify(p => p.WriteAsync(It.IsAny<Connector>(), It.IsAny<ConnectorOperation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithSeedPayload_SkipsSourceRead()
    {
        var (pipeline, source, target) = BuildPipeline();

        var pipelineRepo = new Mock<IPipelineRepository>();
        pipelineRepo.Setup(r => r.GetByIdWithVersionsAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);

        var connectorRepo = new Mock<IConnectorRepository>();
        connectorRepo.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var plugin = new Mock<IConnectorPlugin>();
        plugin.Setup(p => p.Type).Returns(ConnectorType.Rest);
        plugin.Setup(p => p.WriteAsync(target, It.IsAny<ConnectorOperation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var pluginFactory = new Mock<IConnectorPluginFactory>();
        pluginFactory.Setup(f => f.Resolve(ConnectorType.Rest)).Returns(plugin.Object);

        PipelineRun? savedRun = null;
        var runRepo = new Mock<IPipelineRunRepository>();
        runRepo.Setup(r => r.AddAsync(It.IsAny<PipelineRun>(), It.IsAny<CancellationToken>()))
               .Callback<PipelineRun, CancellationToken>((run, _) => savedRun = run)
               .Returns(Task.CompletedTask);
        runRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var executor = BuildExecutor(pipelineRepo, runRepo, connectorRepo, pluginFactory);

        await executor.ExecuteAsync(pipeline.Id, "webhook", dryRun: false, seedPayloadJson: "{\"nome\":\"Bia\"}");

        Assert.NotNull(savedRun);
        Assert.Equal(PipelineRunStatus.Succeeded, savedRun!.Status);
        connectorRepo.Verify(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>()), Times.Never);
        plugin.Verify(p => p.WriteAsync(target, It.IsAny<ConnectorOperation>(), It.Is<string>(s => s.Contains("Bia")), It.IsAny<CancellationToken>()), Times.Once);
    }
}
