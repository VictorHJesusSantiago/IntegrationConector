using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Core.Dtos;

/// <summary>
/// Definição executável de um pipeline (armazenada como JSON dentro de PipelineVersion.DefinitionJson).
/// Descreve o conector de origem, o conector de destino, as regras de transformação e a política de retry.
/// </summary>
public class PipelineDefinition
{
    public Guid SourceConnectorId { get; set; }
    public Guid TargetConnectorId { get; set; }

    /// <summary>Operação a executar no conector de origem (ex.: "GET /pedidos", "SELECT ...", nome da fila).</summary>
    public ConnectorOperation SourceOperation { get; set; } = new();

    /// <summary>Operação a executar no conector de destino.</summary>
    public ConnectorOperation TargetOperation { get; set; } = new();

    /// <summary>Origem secundária opcional, combinada (join) com a origem principal antes da transformação.</summary>
    public SecondarySource? SecondarySource { get; set; }

    public List<MappingRule> Mappings { get; set; } = new();

    public List<AggregationRule> Aggregations { get; set; } = new();

    public RetryPolicySpec RetryPolicy { get; set; } = new();

    public ExecutionOptions Execution { get; set; } = new();

    /// <summary>JSONPath sobre o registro já transformado que identifica sua chave de idempotência (opcional).</summary>
    public string? IdempotencyKeyPath { get; set; }

    /// <summary>JSON Schema (draft 2020-12) opcional para validar cada registro transformado antes da escrita.</summary>
    public string? TargetJsonSchema { get; set; }
}

public class ConnectorOperation
{
    /// <summary>Método/verbo (GET, POST, SELECT, INSERT, SEND, RECEIVE, UPLOAD, DOWNLOAD, StoredProcedure, dependendo do tipo de conector).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Caminho, endpoint, tabela, fila ou arquivo remoto alvo da operação.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Corpo/parâmetros estáticos serializados em JSON, usados como template (mesclados com o payload transformado).</summary>
    public string? PayloadTemplateJson { get; set; }

    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>Formato de dados na "fiação" (wire format); é convertido para/de JSON canônico internamente.</summary>
    public PayloadFormat Format { get; set; } = PayloadFormat.Json;

    /// <summary>Configuração de paginação (somente conector REST, na leitura).</summary>
    public PaginationSpec? Pagination { get; set; }
}

public class PaginationSpec
{
    /// <summary>"NextLink" (segue URL informada em um campo da resposta) ou "PageNumber" (incrementa parâmetro de página).</summary>
    public string Mode { get; set; } = "NextLink";

    /// <summary>JSONPath para a URL da próxima página (modo NextLink) ou para o array de itens (ambos os modos).</summary>
    public string NextLinkPath { get; set; } = "$.next";
    public string ItemsPath { get; set; } = "$.items";

    /// <summary>Nome do parâmetro de página (modo PageNumber).</summary>
    public string PageParam { get; set; } = "page";
    public int MaxPages { get; set; } = 50;
}

/// <summary>Origem secundária combinada com a principal via join simples chave-a-chave.</summary>
public class SecondarySource
{
    public Guid ConnectorId { get; set; }
    public ConnectorOperation Operation { get; set; } = new();
    public string PrimaryJoinPath { get; set; } = string.Empty;
    public string SecondaryJoinPath { get; set; } = string.Empty;

    /// <summary>Nome do campo onde o registro secundário casado será anexado ao registro primário antes do mapeamento.</summary>
    public string TargetPropertyName { get; set; } = "joined";
}

/// <summary>Regra de mapeamento campo-a-campo usada pelo motor de transformação.</summary>
public class MappingRule
{
    /// <summary>Expressão JSONPath sobre o payload de origem (ex.: "$.cliente.nome").</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Caminho no payload de destino, usando notação de ponto (ex.: "customer.name").</summary>
    public string TargetPath { get; set; } = string.Empty;

    public TransformFunction Function { get; set; } = TransformFunction.None;

    /// <summary>Argumento da função (ex.: valor default, formato de data, valor constante, regex, tabela de lookup em JSON).</summary>
    public string? FunctionArgument { get; set; }

    /// <summary>Segundo caminho de origem, usado pelas funções Join/Math/Conditional.</summary>
    public string? SecondSourcePath { get; set; }
}

/// <summary>Regra de agregação aplicada sobre um payload de origem em array, produzindo campos-resumo no destino.</summary>
public class AggregationRule
{
    public AggregationOperation Operation { get; set; }

    /// <summary>JSONPath do campo numérico dentro de cada item do array de origem (ignorado em Count).</summary>
    public string FieldPath { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;
}

public class RetryPolicySpec
{
    public int MaxAttempts { get; set; } = 3;
    public int BaseDelaySeconds { get; set; } = 5;
    public bool ExponentialBackoff { get; set; } = true;

    /// <summary>Habilita circuit breaker: após N falhas seguidas, para de tentar por BreakDurationSeconds.</summary>
    public bool CircuitBreakerEnabled { get; set; }
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    /// <summary>Timeout total da execução (leitura+transformação+escrita), em segundos. 0 = sem timeout.</summary>
    public int TimeoutSeconds { get; set; }
}

public class ExecutionOptions
{
    /// <summary>Grau de paralelismo ao gravar registros individualmente no destino (1 = sequencial, como antes).</summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>Se true, uma falha em um registro não interrompe os demais (registro vai para dead-letter).</summary>
    public bool ContinueOnRecordError { get; set; } = true;
}
