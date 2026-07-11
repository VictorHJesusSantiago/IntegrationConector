using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Core.Entities;

/// <summary>
/// Snapshot imutável da definição de um pipeline em um dado momento.
/// Cada publicação gera uma nova versão; versões anteriores são preservadas para auditoria/rollback.
/// </summary>
public class PipelineVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineId { get; set; }
    public Pipeline? Pipeline { get; set; }

    public int VersionNumber { get; set; }

    /// <summary>JSON serializado de <see cref="Dtos.PipelineDefinition"/>: steps, conectores, mapeamentos e retry policy.</summary>
    public string DefinitionJson { get; set; } = "{}";

    public string? ChangeNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";

    /// <summary>Workflow simples de governança: Draft -> InReview -> Published.</summary>
    public PipelineVersionStatus Status { get; set; } = PipelineVersionStatus.Published;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
