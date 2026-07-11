using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Interfaces;

namespace IntegrationConnector.Api.Validation;

/// <summary>Valida a definição de um pipeline antes da criação/publicação: conectores existentes/ativos e mapeamentos coerentes.</summary>
public static class PipelineDefinitionValidator
{
    public static async Task<List<string>> ValidateAsync(PipelineDefinition definition, IConnectorRepository connectorRepository, CancellationToken ct)
    {
        var errors = new List<string>();

        var source = await connectorRepository.GetByIdAsync(definition.SourceConnectorId, ct);
        if (source is null) errors.Add($"Conector de origem '{definition.SourceConnectorId}' não existe.");
        else if (!source.IsActive) errors.Add($"Conector de origem '{source.Name}' está inativo.");

        var target = await connectorRepository.GetByIdAsync(definition.TargetConnectorId, ct);
        if (target is null) errors.Add($"Conector de destino '{definition.TargetConnectorId}' não existe.");
        else if (!target.IsActive) errors.Add($"Conector de destino '{target.Name}' está inativo.");

        if (definition.Mappings.Count == 0)
            errors.Add("A definição deve conter ao menos uma regra de mapeamento.");

        foreach (var mapping in definition.Mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.TargetPath))
                errors.Add("Toda regra de mapeamento deve ter um TargetPath preenchido.");
        }

        if (definition.SecondarySource is not null)
        {
            var secondary = await connectorRepository.GetByIdAsync(definition.SecondarySource.ConnectorId, ct);
            if (secondary is null) errors.Add($"Conector secundário '{definition.SecondarySource.ConnectorId}' não existe.");
        }

        return errors;
    }
}
