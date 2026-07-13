using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Enums;
using Newtonsoft.Json.Linq;

namespace IntegrationConnector.Transformation;

/// <summary>Calcula agregações (soma, contagem, média, mínimo, máximo) sobre um payload de origem em array.</summary>
public static class AggregationEngine
{
    public static JObject Aggregate(string sourceJson, IReadOnlyList<AggregationRule> rules)
    {
        var result = new JObject();
        if (rules.Count == 0) return result;

        var token = JToken.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "[]" : sourceJson);
        var array = token as JArray ?? new JArray(token);

        foreach (var rule in rules)
        {
            JToken value = rule.Operation switch
            {
                AggregationOperation.Count => array.Count,
                AggregationOperation.Sum => Numbers(array, rule.FieldPath).Sum(),
                AggregationOperation.Avg => Numbers(array, rule.FieldPath) is { } nums && nums.Any() ? nums.Average() : 0m,
                AggregationOperation.Min => Numbers(array, rule.FieldPath) is { } minNums && minNums.Any() ? minNums.Min() : 0m,
                AggregationOperation.Max => Numbers(array, rule.FieldPath) is { } maxNums && maxNums.Any() ? maxNums.Max() : 0m,
                _ => 0m
            };

            result[rule.TargetPath] = value;
        }

        return result;
    }

    private static IEnumerable<decimal> Numbers(JArray array, string fieldPath)
    {
        var path = fieldPath.StartsWith("$", StringComparison.Ordinal) ? fieldPath : "$." + fieldPath;
        foreach (var item in array)
        {
            var token = item.SelectToken(path);
            if (token is not null && decimal.TryParse(token.ToString(), out var number))
                yield return number;
        }
    }
}
