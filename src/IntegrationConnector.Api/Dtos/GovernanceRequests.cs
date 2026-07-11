namespace IntegrationConnector.Api.Dtos;

public record SubmitForReviewRequest(int VersionNumber);
public record ApproveVersionRequest(int VersionNumber, string ReviewedBy);

/// <summary>Pacote portável de um pipeline (definição da versão ativa), usado para exportar/importar entre ambientes.</summary>
public record PipelineExportBundle(
    string Name,
    string? Description,
    Core.Enums.PipelineTriggerType TriggerType,
    string? CronExpression,
    int? IntervalSeconds,
    Core.Dtos.PipelineDefinition Definition);
