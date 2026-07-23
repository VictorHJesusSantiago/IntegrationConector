using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Api.Dtos;

/// <summary>
/// Resumo de uma execução para telas de listagem (grade, dashboard).
///
/// Existe para que a entidade <c>PipelineRun</c> nunca seja serializada em uma listagem: ela carrega
/// <c>ErrorStackTrace</c> e a mensagem de erro completa, que frequentemente contêm host interno,
/// trecho de SQL, URL com credencial embutida ou corpo de resposta de um sistema parceiro. Nada disso
/// tem utilidade numa grade, e todo ele é material de reconhecimento para um atacante.
/// O detalhe completo continua disponível em <c>GET /api/pipeline-runs/{id}</c>, que exige papel.
/// </summary>
public record PipelineRunSummaryResponse(
    Guid Id,
    Guid PipelineId,
    int PipelineVersionNumber,
    PipelineRunStatus Status,
    string TriggerSource,
    bool IsDryRun,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int RecordsRead,
    int RecordsWritten,
    int RecordsFailed,
    int AttemptCount,
    string? ErrorSummary)
{
    /// <summary>Limite de caracteres da mensagem de erro exposta em listagem.</summary>
    private const int ErrorSummaryMaxLength = 200;

    public static PipelineRunSummaryResponse FromEntity(PipelineRun run) => new(
        run.Id,
        run.PipelineId,
        run.PipelineVersionNumber,
        run.Status,
        run.TriggerSource,
        run.IsDryRun,
        run.StartedAt,
        run.FinishedAt,
        run.RecordsRead,
        run.RecordsWritten,
        run.RecordsFailed,
        run.AttemptCount,
        Truncate(run.ErrorMessage));

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= ErrorSummaryMaxLength ? value : value[..ErrorSummaryMaxLength] + "…";
    }
}
