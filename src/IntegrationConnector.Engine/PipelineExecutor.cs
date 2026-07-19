using System.Security.Cryptography;
using System.Text;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Transformation;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.CircuitBreaker;

namespace IntegrationConnector.Engine;

/// <summary>
/// Orquestra a execução de um pipeline: lê a versão ativa, resolve os conectores de origem/destino/
/// secundário, aplica join, transformação, agregação e validação de schema, grava o resultado
/// (sequencial ou em paralelo controlado) com retry, circuit breaker, timeout, dead-letter,
/// idempotência e encadeamento — registrando tudo para os painéis de observabilidade.
/// </summary>
public class PipelineExecutor : IPipelineExecutor
{
    private readonly IPipelineRepository _pipelineRepository;
    private readonly IPipelineRunRepository _runRepository;
    private readonly IConnectorRepository _connectorRepository;
    private readonly IConnectorPluginFactory _pluginFactory;
    private readonly IDataTransformer _transformer;
    private readonly IDeadLetterRepository _deadLetterRepository;
    private readonly IIdempotencyRepository _idempotencyRepository;
    private readonly ISecretProtector _secretProtector;
    private readonly IPipelineChainTrigger _chainTrigger;
    private readonly IPipelineRunCancellationRegistry _cancellationRegistry;
    private readonly ILogger<PipelineExecutor> _logger;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, AsyncCircuitBreakerPolicy> CircuitBreakers = new();

    public PipelineExecutor(
        IPipelineRepository pipelineRepository,
        IPipelineRunRepository runRepository,
        IConnectorRepository connectorRepository,
        IConnectorPluginFactory pluginFactory,
        IDataTransformer transformer,
        IDeadLetterRepository deadLetterRepository,
        IIdempotencyRepository idempotencyRepository,
        ISecretProtector secretProtector,
        IPipelineChainTrigger chainTrigger,
        IPipelineRunCancellationRegistry cancellationRegistry,
        ILogger<PipelineExecutor> logger)
    {
        _pipelineRepository = pipelineRepository;
        _runRepository = runRepository;
        _connectorRepository = connectorRepository;
        _pluginFactory = pluginFactory;
        _transformer = transformer;
        _deadLetterRepository = deadLetterRepository;
        _idempotencyRepository = idempotencyRepository;
        _secretProtector = secretProtector;
        _chainTrigger = chainTrigger;
        _cancellationRegistry = cancellationRegistry;
        _logger = logger;
    }

    public async Task<Guid> ExecuteAsync(Guid pipelineId, string triggerSource, CancellationToken ct = default)
        => await ExecuteAsync(pipelineId, triggerSource, dryRun: false, seedPayloadJson: null, ct);

    public async Task<Guid> ExecuteAsync(Guid pipelineId, string triggerSource, bool dryRun, string? seedPayloadJson, CancellationToken ct = default)
    {
        var pipeline = await _pipelineRepository.GetByIdWithVersionsAsync(pipelineId, ct)
            ?? throw new InvalidOperationException($"Pipeline '{pipelineId}' não encontrado.");

        if (!pipeline.IsEnabled)
            throw new InvalidOperationException($"Pipeline '{pipeline.Name}' está desabilitado.");

        var version = pipeline.Versions.FirstOrDefault(v => v.VersionNumber == pipeline.ActiveVersionNumber)
            ?? throw new InvalidOperationException($"Pipeline '{pipeline.Name}' não possui versão ativa publicada.");

        var definition = JsonConvert.DeserializeObject<PipelineDefinition>(version.DefinitionJson)
            ?? throw new InvalidOperationException("Definição de pipeline inválida.");

        var run = new PipelineRun
        {
            PipelineId = pipeline.Id,
            PipelineVersionNumber = version.VersionNumber,
            Status = PipelineRunStatus.Running,
            TriggerSource = triggerSource,
            IsDryRun = dryRun
        };
        await _runRepository.AddAsync(run, ct);
        await _runRepository.SaveChangesAsync(ct);

        var cts = _cancellationRegistry.Register(run.Id);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

        try
        {
            await RunWithResilienceAsync(pipeline, definition, run, dryRun, seedPayloadJson, linkedCts.Token);
            run.Status = run.RecordsFailed > 0 ? PipelineRunStatus.PartiallySucceeded : PipelineRunStatus.Succeeded;

            if (!dryRun && pipeline.NextPipelineId.HasValue && run.Status != PipelineRunStatus.Failed)
            {
                AddLog(run, "Information", "chain", $"Disparando pipeline encadeado {pipeline.NextPipelineId.Value}.");
                _chainTrigger.TriggerNext(pipeline.NextPipelineId.Value);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            run.Status = PipelineRunStatus.Cancelled;
            AddLog(run, "Warning", "pipeline", "Execução cancelada pelo usuário.");
        }
        catch (Exception ex)
        {
            run.Status = PipelineRunStatus.Failed;
            run.ErrorMessage = ex.Message;
            run.ErrorStackTrace = ex.ToString();
            AddLog(run, "Error", "pipeline", $"Execução falhou definitivamente: {ex.Message}");
            _logger.LogError(ex, "Falha ao executar pipeline {PipelineId}", pipelineId);
        }
        finally
        {
            run.FinishedAt = DateTime.UtcNow;
            // Não chamar _runRepository.Update(run) aqui: "run" já está rastreado pelo mesmo
            // DbContext desde o AddAsync inicial (mesmo escopo/execução). Forçar Update() no grafo
            // faria o EF Core marcar os PipelineRunLog recém-adicionados (com Guid gerado no
            // cliente) como "Modified" em vez de "Added", gerando um UPDATE para uma linha
            // inexistente (DbUpdateConcurrencyException: 0 linhas afetadas) — e quebrando TODA
            // execução, com ou sem falha, já que logs de "read"/"transform"/"write" são sempre
            // adicionados. As mudanças de propriedades escalares e os novos logs já são detectados
            // automaticamente pelo change tracker, sem necessidade de Update() explícito.
            await _runRepository.SaveChangesAsync(ct);
            _cancellationRegistry.Remove(run.Id);
        }

        return run.Id;
    }

    private async Task RunWithResilienceAsync(Pipeline pipeline, PipelineDefinition definition, PipelineRun run, bool dryRun, string? seedPayloadJson, CancellationToken ct)
    {
        var policySpec = definition.RetryPolicy;

        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: Math.Max(0, policySpec.MaxAttempts - 1),
                sleepDurationProvider: attempt => policySpec.ExponentialBackoff
                    ? TimeSpan.FromSeconds(policySpec.BaseDelaySeconds * Math.Pow(2, attempt - 1))
                    : TimeSpan.FromSeconds(policySpec.BaseDelaySeconds),
                onRetry: (ex, delay, attempt, _) =>
                {
                    run.Status = PipelineRunStatus.Retrying;
                    AddLog(run, "Warning", "retry", $"Tentativa {attempt} falhou ({ex.Message}). Nova tentativa em {delay.TotalSeconds:0}s.");
                });

        IAsyncPolicy resiliencePolicy = retryPolicy;

        if (policySpec.CircuitBreakerEnabled)
        {
            // O circuit breaker é cacheado por pipeline em um dicionário estático (necessário para
            // que o estado "aberto" realmente persista entre execuções distintas). Por isso, seus
            // callbacks NUNCA podem capturar objetos de uma execução específica (como "run" ou,
            // transitivamente, o DbContext desta chamada via AddLog/_runRepository) — na primeira
            // vez que o circuito abrir numa execução POSTERIOR, esses objetos já estariam
            // descartados (o escopo de DI daquele job já terminou), quebrando com
            // ObjectDisposedException. Por isso usamos apenas o _logger (seguro entre escopos) aqui;
            // o registro do erro na execução atual acontece no catch genérico de ExecuteAsync.
            var pipelineId = pipeline.Id;
            var breaker = CircuitBreakers.GetOrAdd(pipeline.Id, _ => Policy
                .Handle<Exception>()
                .CircuitBreakerAsync(
                    policySpec.CircuitBreakerFailureThreshold,
                    TimeSpan.FromSeconds(policySpec.CircuitBreakerBreakDurationSeconds),
                    onBreak: (ex, breakDelay) => _logger.LogError(ex, "Circuito aberto para o pipeline {PipelineId} por {BreakSeconds}s após falhas consecutivas.", pipelineId, breakDelay.TotalSeconds),
                    onReset: () => _logger.LogInformation("Circuito fechado novamente para o pipeline {PipelineId}.", pipelineId),
                    onHalfOpen: () => _logger.LogInformation("Circuito em teste (half-open) para o pipeline {PipelineId}.", pipelineId)));

            resiliencePolicy = Policy.WrapAsync(retryPolicy, breaker);
        }

        if (policySpec.TimeoutSeconds > 0)
        {
            var timeoutPolicy = Policy.TimeoutAsync(TimeSpan.FromSeconds(policySpec.TimeoutSeconds));
            resiliencePolicy = Policy.WrapAsync(resiliencePolicy, timeoutPolicy);
        }

        await resiliencePolicy.ExecuteAsync(async token =>
        {
            run.AttemptCount++;
            await ExecuteOnceAsync(pipeline, definition, run, dryRun, seedPayloadJson, token);
        }, ct);
    }

    private async Task ExecuteOnceAsync(Pipeline pipeline, PipelineDefinition definition, PipelineRun run, bool dryRun, string? seedPayloadJson, CancellationToken ct)
    {
        var sourceJson = seedPayloadJson;

        if (sourceJson is null)
        {
            var sourceConnector = await LoadConnectorAsync(definition.SourceConnectorId, ct);
            var sourcePlugin = _pluginFactory.Resolve(sourceConnector.Type);

            AddLog(run, "Information", "read", $"Lendo dados de '{sourceConnector.Name}'.");
            var raw = await sourcePlugin.ReadAsync(sourceConnector, definition.SourceOperation, ct);
            sourceJson = FormatConverter.ToCanonicalJson(raw, definition.SourceOperation.Format);
        }
        else
        {
            AddLog(run, "Information", "read", "Payload recebido via webhook usado como origem.");
        }

        if (definition.SecondarySource is not null)
            sourceJson = await JoinSecondarySourceAsync(definition.SecondarySource, sourceJson, ct);

        var recordsRead = CountRecords(sourceJson);
        run.RecordsRead = recordsRead;
        AddLog(run, "Information", "read", $"{recordsRead} registro(s) lido(s).");

        if (definition.Aggregations.Count > 0)
        {
            var aggregates = AggregationEngine.Aggregate(sourceJson, definition.Aggregations);
            run.AggregationResultJson = aggregates.ToString(Newtonsoft.Json.Formatting.None);
            AddLog(run, "Information", "aggregate", $"Agregações calculadas: {aggregates}");
        }

        AddLog(run, "Information", "transform", "Aplicando regras de transformação.");
        var transformedJson = _transformer.Transform(sourceJson, definition.Mappings);

        if (dryRun)
        {
            AddLog(run, "Information", "dry-run", $"Modo dry-run: {CountRecords(transformedJson)} registro(s) transformado(s), nada foi gravado.");
            run.RecordsWritten = 0;
            return;
        }

        var targetConnector = await LoadConnectorAsync(definition.TargetConnectorId, ct);
        var targetPlugin = _pluginFactory.Resolve(targetConnector.Type);

        await WriteRecordsAsync(pipeline, definition, run, targetConnector, targetPlugin, transformedJson, ct);
    }

    private async Task<string> JoinSecondarySourceAsync(SecondarySource secondary, string primaryJson, CancellationToken ct)
    {
        var secondaryConnector = await LoadConnectorAsync(secondary.ConnectorId, ct);
        var secondaryPlugin = _pluginFactory.Resolve(secondaryConnector.Type);
        var secondaryRaw = await secondaryPlugin.ReadAsync(secondaryConnector, secondary.Operation, ct);
        var secondaryJson = FormatConverter.ToCanonicalJson(secondaryRaw, secondary.Operation.Format);

        var secondaryToken = JToken.Parse(string.IsNullOrWhiteSpace(secondaryJson) ? "[]" : secondaryJson);
        var secondaryArray = secondaryToken as JArray ?? new JArray(secondaryToken);

        var primaryToken = JToken.Parse(string.IsNullOrWhiteSpace(primaryJson) ? "[]" : primaryJson);
        var primaryPath = NormalizeJsonPath(secondary.PrimaryJoinPath);
        var secondaryPath = NormalizeJsonPath(secondary.SecondaryJoinPath);

        void Join(JToken primaryItem)
        {
            var key = primaryItem.SelectToken(primaryPath)?.ToString();
            var match = secondaryArray.FirstOrDefault(s => s.SelectToken(secondaryPath)?.ToString() == key);
            if (primaryItem is JObject obj)
                obj[secondary.TargetPropertyName] = match?.DeepClone() ?? JValue.CreateNull();
        }

        if (primaryToken is JArray primaryArray)
            foreach (var item in primaryArray) Join(item);
        else
            Join(primaryToken);

        return primaryToken.ToString(Newtonsoft.Json.Formatting.None);
    }

    private async Task WriteRecordsAsync(
        Pipeline pipeline,
        PipelineDefinition definition,
        PipelineRun run,
        Connector targetConnector,
        Core.Interfaces.IConnectorPlugin targetPlugin,
        string transformedJson,
        CancellationToken ct)
    {
        var token = JToken.Parse(string.IsNullOrWhiteSpace(transformedJson) ? "[]" : transformedJson);
        var records = (token as JArray)?.ToList() ?? new List<JToken> { token };
        var degreeOfParallelism = Math.Max(1, definition.Execution.MaxDegreeOfParallelism);

        AddLog(run, "Information", "write", $"Gravando dados em '{targetConnector.Name}' (paralelismo={degreeOfParallelism}).");

        if (degreeOfParallelism <= 1)
        {
            // Comportamento clássico: um único WriteAsync com o lote inteiro (necessário para conectores
            // que fazem upsert em lote, como o de banco de dados).
            var validRecords = new JArray();
            foreach (var record in records)
            {
                if (await IsDuplicateAsync(pipeline.Id, definition, record, ct)) continue;
                if (!await ValidateRecordAsync(pipeline.Id, run, definition, record, ct)) continue;
                validRecords.Add(record);
            }

            if (validRecords.Count > 0)
                await targetPlugin.WriteAsync(targetConnector, definition.TargetOperation, validRecords.ToString(Newtonsoft.Json.Formatting.None), ct);

            run.RecordsWritten = validRecords.Count;
            await MarkIdempotencyProcessedAsync(pipeline.Id, definition, validRecords, ct);
            AddLog(run, "Information", "write", $"{run.RecordsWritten} registro(s) gravado(s) com sucesso.");
            return;
        }

        using var semaphore = new SemaphoreSlim(degreeOfParallelism);
        var writtenCount = 0;
        var failedCount = 0;
        var lockObj = new object();

        var tasks = records.Select(async record =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (await IsDuplicateAsync(pipeline.Id, definition, record, ct)) return;
                if (!await ValidateRecordAsync(pipeline.Id, run, definition, record, ct)) { lock (lockObj) failedCount++; return; }

                await targetPlugin.WriteAsync(targetConnector, definition.TargetOperation, record.ToString(Newtonsoft.Json.Formatting.None), ct);
                await MarkIdempotencyProcessedAsync(pipeline.Id, definition, new JArray(record), ct);
                lock (lockObj) writtenCount++;
            }
            catch (Exception ex)
            {
                lock (lockObj) failedCount++;
                await SendToDeadLetterAsync(pipeline.Id, run.Id, record, ex.Message, ct);
                if (!definition.Execution.ContinueOnRecordError) throw;
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        run.RecordsWritten = writtenCount;
        run.RecordsFailed = failedCount;
        AddLog(run, "Information", "write", $"{writtenCount} registro(s) gravado(s), {failedCount} falharam (dead-letter).");
    }

    private async Task<bool> ValidateRecordAsync(Guid pipelineId, PipelineRun run, PipelineDefinition definition, JToken record, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(definition.TargetJsonSchema)) return true;

        var recordJson = record.ToString(Newtonsoft.Json.Formatting.None);
        if (TargetSchemaValidator.TryValidate(definition.TargetJsonSchema, recordJson, out var errors)) return true;

        run.RecordsFailed++;
        await SendToDeadLetterAsync(pipelineId, run.Id, record, string.Join("; ", errors), ct);
        AddLog(run, "Warning", "validation", $"Registro rejeitado pelo schema: {string.Join("; ", errors)}");
        return false;
    }

    private async Task<bool> IsDuplicateAsync(Guid pipelineId, PipelineDefinition definition, JToken record, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(definition.IdempotencyKeyPath)) return false;

        var keyToken = record.SelectToken(NormalizeJsonPath(definition.IdempotencyKeyPath));
        if (keyToken is null) return false;

        var hash = ComputeHash($"{pipelineId}:{keyToken}");
        return await _idempotencyRepository.ExistsAsync(pipelineId, hash, ct);
    }

    private async Task MarkIdempotencyProcessedAsync(Guid pipelineId, PipelineDefinition definition, JArray records, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(definition.IdempotencyKeyPath)) return;

        foreach (var record in records)
        {
            var keyToken = record.SelectToken(NormalizeJsonPath(definition.IdempotencyKeyPath));
            if (keyToken is null) continue;
            var hash = ComputeHash($"{pipelineId}:{keyToken}");
            await _idempotencyRepository.MarkProcessedAsync(pipelineId, hash, ct);
        }
        await _idempotencyRepository.SaveChangesAsync(ct);
    }

    private async Task SendToDeadLetterAsync(Guid pipelineId, Guid runId, JToken record, string errorMessage, CancellationToken ct)
    {
        await _deadLetterRepository.AddAsync(new DeadLetterRecord
        {
            PipelineId = pipelineId,
            PipelineRunId = runId,
            RecordJson = record.ToString(Newtonsoft.Json.Formatting.None),
            ErrorMessage = errorMessage
        }, ct);
        await _deadLetterRepository.SaveChangesAsync(ct);
    }

    private async Task<Connector> LoadConnectorAsync(Guid connectorId, CancellationToken ct)
    {
        var connector = await _connectorRepository.GetByIdAsync(connectorId, ct)
            ?? throw new InvalidOperationException($"Conector '{connectorId}' não encontrado.");

        // Retorna uma cópia não rastreada com o config decifrado — nunca mutar a entidade rastreada
        // (veja ConnectorExtensions.WithDecryptedConfig): o SaveChanges do PipelineRun mais adiante
        // no mesmo DbContext gravaria o segredo em texto plano de volta no banco.
        return connector.WithDecryptedConfig(_secretProtector);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeJsonPath(string path)
        => path.StartsWith("$", StringComparison.Ordinal) ? path : "$." + path;

    private static int CountRecords(string json)
    {
        try
        {
            var token = JToken.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return token is JArray array ? array.Count : (token.Type == JTokenType.Null ? 0 : 1);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Adiciona um log tanto à coleção em memória (para uso imediato, ex.: contagem) quanto o rastreia
    /// explicitamente como "Added" no repositório — necessário porque seu Id é um Guid gerado no
    /// cliente; sem o rastreamento explícito, o EF Core o marcaria como "Modified" ao descobri-lo via
    /// a coleção de navegação de um PipelineRun já rastreado, gerando um UPDATE para uma linha inexistente.
    /// </summary>
    private void AddLog(PipelineRun run, string level, string step, string message)
    {
        var log = new PipelineRunLog { PipelineRunId = run.Id, Level = level, Step = step, Message = message };
        run.Logs.Add(log);
        _runRepository.AddLog(log);
    }
}

public interface IPipelineExecutor
{
    Task<Guid> ExecuteAsync(Guid pipelineId, string triggerSource, CancellationToken ct = default);
    Task<Guid> ExecuteAsync(Guid pipelineId, string triggerSource, bool dryRun, string? seedPayloadJson, CancellationToken ct = default);
}
