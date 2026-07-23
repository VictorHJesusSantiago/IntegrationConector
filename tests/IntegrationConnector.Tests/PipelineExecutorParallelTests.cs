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

/// <summary>
/// Testes do caminho de escrita paralela (<c>ExecutionOptions.MaxDegreeOfParallelism &gt; 1</c>).
///
/// Este caminho não tinha nenhuma cobertura, e era exatamente onde vivia a falha mais grave do motor:
/// todos os repositórios do executor compartilham um único <c>IntegrationDbContext</c> (todos Scoped),
/// que não é thread-safe. As tasks paralelas chamavam dedupe, validação, idempotência e dead-letter
/// simultaneamente sobre esse contexto — algo que, em produção, estoura com
/// "A second operation was started on this context instance before a previous operation completed".
///
/// O teste central aqui não verifica um resultado, e sim uma INVARIANTE: nenhuma operação de
/// repositório pode se sobrepor a outra no tempo. É o tipo de bug que testes baseados em resultado
/// nunca pegam, porque o resultado costuma estar certo até o dia em que o agendador intercala as
/// threads de outro jeito.
/// </summary>
public class PipelineExecutorParallelTests
{
    /// <summary>
    /// Detector de reentrância concorrente. Registra a maior sobreposição observada, permitindo tanto
    /// exigir serialização (repositórios) quanto exigir paralelismo real (plugin de destino).
    /// </summary>
    private sealed class ConcurrencyProbe
    {
        private int _active;
        private int _maxObserved;

        public int MaxObserved => Volatile.Read(ref _maxObserved);

        public IDisposable Enter()
        {
            var current = Interlocked.Increment(ref _active);

            // Atualiza o máximo observado sem perder atualizações concorrentes.
            int observed;
            while (current > (observed = Volatile.Read(ref _maxObserved)))
                Interlocked.CompareExchange(ref _maxObserved, current, observed);

            return new Scope(this);
        }

        private sealed class Scope : IDisposable
        {
            private readonly ConcurrencyProbe _probe;
            public Scope(ConcurrencyProbe probe) => _probe = probe;
            public void Dispose() => Interlocked.Decrement(ref _probe._active);
        }
    }

    private const int RecordCount = 12;
    private const int DegreeOfParallelism = 4;

    private static string BuildSourceJson(int records) =>
        JsonConvert.SerializeObject(Enumerable.Range(1, records).Select(i => new { nome = $"pessoa-{i}", id = i }));

    private static (Pipeline pipeline, Connector source, Connector target) BuildPipeline(
        int maxDegreeOfParallelism,
        string? targetJsonSchema = null,
        bool continueOnRecordError = true)
    {
        var source = new Connector { Id = Guid.NewGuid(), Type = ConnectorType.Rest, Name = "origem" };
        var target = new Connector { Id = Guid.NewGuid(), Type = ConnectorType.Rest, Name = "destino" };

        var definition = new PipelineDefinition
        {
            SourceConnectorId = source.Id,
            TargetConnectorId = target.Id,
            SourceOperation = new ConnectorOperation { Action = "GET", Target = "/dados" },
            TargetOperation = new ConnectorOperation { Action = "POST", Target = "/destino" },
            Mappings = new List<MappingRule>
            {
                new() { SourcePath = "$.nome", TargetPath = "nome" },
                new() { SourcePath = "$.id", TargetPath = "id" }
            },
            RetryPolicy = new RetryPolicySpec { MaxAttempts = 1, BaseDelaySeconds = 0 },
            IdempotencyKeyPath = "id",
            TargetJsonSchema = targetJsonSchema,
            Execution = new ExecutionOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                ContinueOnRecordError = continueOnRecordError
            }
        };

        var pipeline = new Pipeline { Id = Guid.NewGuid(), Name = "teste-paralelo", IsEnabled = true, ActiveVersionNumber = 1 };
        pipeline.Versions.Add(new PipelineVersion
        {
            PipelineId = pipeline.Id,
            VersionNumber = 1,
            DefinitionJson = JsonConvert.SerializeObject(definition)
        });

        return (pipeline, source, target);
    }

    /// <summary>
    /// Monta o executor com todos os repositórios instrumentados por um ÚNICO probe — espelhando o
    /// fato de que, em produção, eles compartilham o mesmo DbContext.
    /// </summary>
    private static (PipelineExecutor executor, Func<PipelineRun?> getRun) BuildExecutor(
        Pipeline pipeline,
        Connector source,
        Connector target,
        IConnectorPlugin plugin,
        ConcurrencyProbe databaseProbe)
    {
        var pipelineRepo = new Mock<IPipelineRepository>();
        pipelineRepo.Setup(r => r.GetByIdWithVersionsAsync(pipeline.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);

        var connectorRepo = new Mock<IConnectorRepository>();
        connectorRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        connectorRepo.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        PipelineRun? savedRun = null;
        var runRepo = new Mock<IPipelineRunRepository>();
        runRepo.Setup(r => r.AddAsync(It.IsAny<PipelineRun>(), It.IsAny<CancellationToken>()))
               .Callback<PipelineRun, CancellationToken>((run, _) => savedRun = run)
               .Returns(Task.CompletedTask);
        runRepo.Setup(r => r.AddLog(It.IsAny<PipelineRunLog>()))
               .Callback(() => { using (databaseProbe.Enter()) { Thread.Sleep(1); } });
        runRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
               .Returns(async () => { using (databaseProbe.Enter()) { await Task.Delay(5); } return 1; });

        var deadLetterRepo = new Mock<IDeadLetterRepository>();
        deadLetterRepo.Setup(r => r.AddAsync(It.IsAny<DeadLetterRecord>(), It.IsAny<CancellationToken>()))
                      .Returns(async () => { using (databaseProbe.Enter()) { await Task.Delay(5); } });
        deadLetterRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                      .Returns(async () => { using (databaseProbe.Enter()) { await Task.Delay(5); } return 1; });

        var idempotencyRepo = new Mock<IIdempotencyRepository>();
        idempotencyRepo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(async () => { using (databaseProbe.Enter()) { await Task.Delay(5); } return false; });
        idempotencyRepo.Setup(r => r.MarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(async () => { using (databaseProbe.Enter()) { await Task.Delay(5); } });
        idempotencyRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                       .Returns(async () => { using (databaseProbe.Enter()) { await Task.Delay(5); } return 1; });

        var secretProtector = new Mock<ISecretProtector>();
        secretProtector.Setup(s => s.Unprotect(It.IsAny<string>())).Returns((string s) => s);

        var pluginFactory = new Mock<IConnectorPluginFactory>();
        pluginFactory.Setup(f => f.Resolve(ConnectorType.Rest)).Returns(plugin);

        var executor = new PipelineExecutor(
            pipelineRepo.Object, runRepo.Object, connectorRepo.Object, pluginFactory.Object,
            new DataTransformer(), deadLetterRepo.Object, idempotencyRepo.Object,
            secretProtector.Object, new Mock<IPipelineChainTrigger>().Object,
            new PipelineRunCancellationRegistry(), NullLogger<PipelineExecutor>.Instance);

        return (executor, () => savedRun);
    }

    private static Mock<IConnectorPlugin> BuildPlugin(string sourceJson, ConcurrencyProbe? writeProbe = null, Func<string, bool>? failWhen = null)
    {
        var plugin = new Mock<IConnectorPlugin>();
        plugin.Setup(p => p.Type).Returns(ConnectorType.Rest);
        plugin.Setup(p => p.ReadAsync(It.IsAny<Connector>(), It.IsAny<ConnectorOperation>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(sourceJson);
        plugin.Setup(p => p.WriteAsync(It.IsAny<Connector>(), It.IsAny<ConnectorOperation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns<Connector, ConnectorOperation, string, CancellationToken>(async (_, _, payload, ct) =>
              {
                  using (writeProbe?.Enter())
                  {
                      await Task.Delay(20, ct);
                      if (failWhen?.Invoke(payload) == true)
                          throw new InvalidOperationException($"destino recusou: {payload}");
                  }
              });
        return plugin;
    }

    [Fact]
    public async Task escrita_paralela_nunca_usa_o_dbcontext_de_forma_concorrente()
    {
        var (pipeline, source, target) = BuildPipeline(DegreeOfParallelism);
        var databaseProbe = new ConcurrencyProbe();
        var writeProbe = new ConcurrencyProbe();
        var plugin = BuildPlugin(BuildSourceJson(RecordCount), writeProbe);

        var (executor, getRun) = BuildExecutor(pipeline, source, target, plugin.Object, databaseProbe);

        await executor.ExecuteAsync(pipeline.Id, "manual");

        // A invariante que importa: acesso ao banco estritamente serializado.
        Assert.Equal(1, databaseProbe.MaxObserved);

        // ...sem que isso tenha custado o paralelismo: a escrita no destino (I/O externo, o que o
        // MaxDegreeOfParallelism existe para acelerar) de fato rodou concorrente.
        Assert.True(writeProbe.MaxObserved > 1, $"esperava escrita concorrente, mas o máximo observado foi {writeProbe.MaxObserved}");
        Assert.True(writeProbe.MaxObserved <= DegreeOfParallelism, "o paralelismo excedeu o limite configurado");
    }

    [Fact]
    public async Task escrita_paralela_grava_todos_os_registros_e_conclui_com_sucesso()
    {
        var (pipeline, source, target) = BuildPipeline(DegreeOfParallelism);
        var plugin = BuildPlugin(BuildSourceJson(RecordCount));

        var (executor, getRun) = BuildExecutor(pipeline, source, target, plugin.Object, new ConcurrencyProbe());

        await executor.ExecuteAsync(pipeline.Id, "manual");

        var run = getRun();
        Assert.NotNull(run);
        Assert.Equal(PipelineRunStatus.Succeeded, run!.Status);
        Assert.Equal(RecordCount, run.RecordsRead);
        Assert.Equal(RecordCount, run.RecordsWritten);
        Assert.Equal(0, run.RecordsFailed);

        plugin.Verify(p => p.WriteAsync(It.IsAny<Connector>(), It.IsAny<ConnectorOperation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(RecordCount));
    }

    [Fact]
    public async Task escrita_paralela_envia_falhas_para_dead_letter_e_continua_o_lote()
    {
        var (pipeline, source, target) = BuildPipeline(DegreeOfParallelism);

        // Dois registros são recusados pelo destino; o restante deve ser gravado normalmente.
        var plugin = BuildPlugin(BuildSourceJson(RecordCount), failWhen: payload => payload.Contains("pessoa-3") || payload.Contains("pessoa-7"));

        var (executor, getRun) = BuildExecutor(pipeline, source, target, plugin.Object, new ConcurrencyProbe());

        await executor.ExecuteAsync(pipeline.Id, "manual");

        var run = getRun();
        Assert.NotNull(run);
        Assert.Equal(RecordCount - 2, run!.RecordsWritten);
        Assert.Equal(2, run.RecordsFailed);
        Assert.Equal(PipelineRunStatus.PartiallySucceeded, run.Status);
    }

    [Fact]
    public async Task escrita_paralela_soma_rejeicoes_de_schema_com_falhas_de_escrita()
    {
        // O schema exige que "id" seja par — rejeitando metade dos registros antes da escrita.
        // Antes da correção, "run.RecordsFailed = falhasDeEscrita" SOBRESCREVIA a contagem de
        // rejeições de schema, e o painel reportava menos falhas do que realmente ocorreram.
        const string schema = """
        {
          "type": "object",
          "properties": { "id": { "type": "integer", "multipleOf": 2 } },
          "required": ["id"]
        }
        """;

        var (pipeline, source, target) = BuildPipeline(DegreeOfParallelism, targetJsonSchema: schema);

        // Além das rejeições de schema, o destino recusa um dos registros pares.
        var plugin = BuildPlugin(BuildSourceJson(RecordCount), failWhen: payload => payload.Contains("pessoa-4"));

        var (executor, getRun) = BuildExecutor(pipeline, source, target, plugin.Object, new ConcurrencyProbe());

        await executor.ExecuteAsync(pipeline.Id, "manual");

        var run = getRun();
        Assert.NotNull(run);

        // 12 registros: 6 ímpares rejeitados pelo schema + 1 par recusado na escrita = 7 falhas.
        Assert.Equal(7, run!.RecordsFailed);
        Assert.Equal(5, run.RecordsWritten);
    }

    [Fact]
    public async Task escrita_sequencial_permanece_em_lote_unico()
    {
        // Com paralelismo 1 o comportamento clássico precisa ser preservado: UMA chamada de escrita
        // com o lote inteiro (conectores de banco dependem disso para fazer upsert em lote).
        var (pipeline, source, target) = BuildPipeline(maxDegreeOfParallelism: 1);
        var plugin = BuildPlugin(BuildSourceJson(RecordCount));

        var (executor, getRun) = BuildExecutor(pipeline, source, target, plugin.Object, new ConcurrencyProbe());

        await executor.ExecuteAsync(pipeline.Id, "manual");

        var run = getRun();
        Assert.NotNull(run);
        Assert.Equal(RecordCount, run!.RecordsWritten);
        plugin.Verify(p => p.WriteAsync(It.IsAny<Connector>(), It.IsAny<ConnectorOperation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
