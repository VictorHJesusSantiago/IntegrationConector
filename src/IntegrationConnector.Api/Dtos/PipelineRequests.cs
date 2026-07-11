using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Api.Dtos;

public record CreatePipelineRequest(
    string Name,
    string? Description,
    PipelineTriggerType TriggerType,
    string? CronExpression,
    int? IntervalSeconds,
    PipelineDefinition Definition);

public record UpdatePipelineRequest(
    string Name,
    string? Description,
    bool IsEnabled,
    PipelineTriggerType TriggerType,
    string? CronExpression,
    int? IntervalSeconds);

public record PublishPipelineVersionRequest(PipelineDefinition Definition, string? ChangeNotes);
