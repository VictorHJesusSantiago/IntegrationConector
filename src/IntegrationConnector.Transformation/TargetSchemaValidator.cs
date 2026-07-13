using System.Text.Json.Nodes;
using Json.Schema;

namespace IntegrationConnector.Transformation;

/// <summary>
/// Valida um registro transformado contra um JSON Schema (draft 2020-12) opcional antes da escrita
/// no destino. Registros inválidos são desviados para dead-letter em vez de interromper o lote.
/// </summary>
public static class TargetSchemaValidator
{
    public static bool TryValidate(string? schemaJson, string instanceJson, out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(schemaJson)) return true;

        var schema = JsonSchema.FromText(schemaJson);
        var instance = JsonNode.Parse(instanceJson);
        var results = schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (results.IsValid) return true;

        foreach (var detail in results.Details.Where(d => !d.IsValid && d.Errors is { Count: > 0 }))
            foreach (var error in detail.Errors!)
                errors.Add($"{detail.InstanceLocation}: {error.Value}");

        if (errors.Count == 0) errors.Add("Registro não corresponde ao schema configurado.");
        return false;
    }
}
