using System.Globalization;
using System.Text.RegularExpressions;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Enums;
using Newtonsoft.Json.Linq;

namespace IntegrationConnector.Transformation;

/// <summary>
/// Motor de transformação de dados. Aplica um conjunto de <see cref="MappingRule"/> sobre um
/// payload JSON de origem (localizado via JSONPath) produzindo um payload JSON de destino
/// (construído via notação de ponto, com suporte a índices de array).
/// </summary>
public class DataTransformer : IDataTransformer
{
    public string Transform(string sourceJson, IReadOnlyList<MappingRule> mappings)
    {
        var token = JToken.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "{}" : sourceJson);

        if (token is JArray array)
        {
            var results = new JArray();
            foreach (var item in array)
            {
                results.Add(TransformSingle(item, mappings));
            }
            return results.ToString(Newtonsoft.Json.Formatting.None);
        }

        return TransformSingle(token, mappings).ToString(Newtonsoft.Json.Formatting.None);
    }

    private static JObject TransformSingle(JToken source, IReadOnlyList<MappingRule> mappings)
    {
        var target = new JObject();

        foreach (var rule in mappings)
        {
            JToken? sourceValue = null;
            if (rule.Function != TransformFunction.Constant && !string.IsNullOrWhiteSpace(rule.SourcePath))
            {
                sourceValue = source.SelectToken(NormalizeJsonPath(rule.SourcePath));
            }

            JToken? secondValue = null;
            if (!string.IsNullOrWhiteSpace(rule.SecondSourcePath))
            {
                secondValue = source.SelectToken(NormalizeJsonPath(rule.SecondSourcePath));
            }

            var finalValue = ApplyFunction(sourceValue, secondValue, rule.Function, rule.FunctionArgument);
            SetByPath(target, rule.TargetPath, finalValue);
        }

        return target;
    }

    private static string NormalizeJsonPath(string path)
        => path.StartsWith('$') ? path : "$." + path;

    private static JToken ApplyFunction(JToken? value, JToken? secondValue, TransformFunction function, string? arg)
    {
        switch (function)
        {
            case TransformFunction.Constant:
                return arg ?? string.Empty;

            case TransformFunction.Default:
                return IsNullOrEmpty(value) ? (JToken)(arg ?? string.Empty) : value!.DeepClone();

            case TransformFunction.ToUpper:
                return AsString(value).ToUpperInvariant();

            case TransformFunction.ToLower:
                return AsString(value).ToLowerInvariant();

            case TransformFunction.Trim:
                return AsString(value).Trim();

            // Cultura invariante em TODA conversão texto<->número/data: o payload de origem é JSON
            // canônico, não texto localizado. Sem isso, o resultado passaria a depender do locale do
            // servidor — "1.234" viraria 1234 numa máquina pt-BR e 1,234 numa en-US, e uma data
            // "03/04/2026" mudaria de mês conforme o host. É corretude, não estilo.
            case TransformFunction.DateFormat:
                if (value is null) return string.Empty;
                var parsed = DateTime.Parse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                return parsed.ToString(string.IsNullOrWhiteSpace(arg) ? "yyyy-MM-dd" : arg, CultureInfo.InvariantCulture);

            case TransformFunction.Concat:
                return AsString(value) + (arg ?? string.Empty);

            case TransformFunction.Number:
                return value is null ? 0 : (JToken)decimal.Parse(value.ToString(), CultureInfo.InvariantCulture);

            case TransformFunction.Split:
            {
                // arg = "delimitador|indice" (ex.: ",|0")
                var parts = (arg ?? ",|0").Split('|');
                var delimiter = parts[0];
                var index = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
                var segments = AsString(value).Split(delimiter);
                return index >= 0 && index < segments.Length ? segments[index] : string.Empty;
            }

            case TransformFunction.Join:
            {
                // combina value + secondValue com o separador em arg (default: espaço)
                var separator = arg ?? " ";
                return AsString(value) + separator + AsString(secondValue);
            }

            case TransformFunction.RegexReplace:
            {
                // arg = "padrao|substituicao"
                var parts = (arg ?? "|").Split('|', 2);
                var pattern = parts[0];
                var replacement = parts.Length > 1 ? parts[1] : string.Empty;
                return Regex.Replace(AsString(value), pattern, replacement);
            }

            case TransformFunction.Lookup:
            {
                // arg = tabela de-para em JSON: {"chaveOrigem": "valorDestino", ...}
                if (string.IsNullOrWhiteSpace(arg)) return value?.DeepClone() ?? JValue.CreateNull();
                var table = JObject.Parse(arg);
                var key = AsString(value);
                return table[key]?.DeepClone() ?? table["_default"]?.DeepClone() ?? (JToken)key;
            }

            case TransformFunction.Math:
            {
                // arg = "+", "-", "*" ou "/" entre value e secondValue
                var left = value is null ? 0m : decimal.Parse(value.ToString(), CultureInfo.InvariantCulture);
                var right = secondValue is null ? 0m : decimal.Parse(secondValue.ToString(), CultureInfo.InvariantCulture);
                return (arg ?? "+") switch
                {
                    "-" => left - right,
                    "*" => left * right,
                    "/" => right == 0 ? 0 : left / right,
                    _ => left + right
                };
            }

            case TransformFunction.Conditional:
            {
                // arg = "operador:valorComparado|valorSeVerdadeiro|valorSeFalso" (operador: eq, ne, gt, lt, contains)
                var parts = (arg ?? "eq:|1|0").Split('|');
                var conditionPart = parts.Length > 0 ? parts[0] : "eq:";
                var thenValue = parts.Length > 1 ? parts[1] : "true";
                var elseValue = parts.Length > 2 ? parts[2] : "false";

                var opSplit = conditionPart.Split(':', 2);
                var op = opSplit[0];
                var compareTo = opSplit.Length > 1 ? opSplit[1] : string.Empty;
                var actual = AsString(value);

                var matched = op switch
                {
                    "ne" => actual != compareTo,
                    "gt" => decimal.TryParse(actual, out var a1) && decimal.TryParse(compareTo, out var b1) && a1 > b1,
                    "lt" => decimal.TryParse(actual, out var a2) && decimal.TryParse(compareTo, out var b2) && a2 < b2,
                    "contains" => actual.Contains(compareTo),
                    _ => actual == compareTo
                };

                return matched ? thenValue : elseValue;
            }

            case TransformFunction.None:
            default:
                return value?.DeepClone() ?? JValue.CreateNull();
        }
    }

    private static string AsString(JToken? value)
        => value?.Type == JTokenType.String ? value.Value<string>()! : value?.ToString() ?? string.Empty;

    private static bool IsNullOrEmpty(JToken? value)
        => value is null || value.Type == JTokenType.Null || (value.Type == JTokenType.String && string.IsNullOrEmpty(value.Value<string>()));

    /// <summary>Constrói caminhos aninhados em notação de ponto, criando objetos e arrays intermediários.</summary>
    private static void SetByPath(JObject root, string path, JToken value)
    {
        var segments = ParseSegments(path);
        JToken current = root;

        for (int i = 0; i < segments.Count; i++)
        {
            var (name, index) = segments[i];
            bool isLast = i == segments.Count - 1;

            if (current is JObject currentObj)
            {
                if (isLast && index is null)
                {
                    currentObj[name] = value;
                    return;
                }

                if (index is null)
                {
                    if (currentObj[name] is not JObject)
                        currentObj[name] = new JObject();
                    current = currentObj[name]!;
                }
                else
                {
                    if (currentObj[name] is not JArray)
                        currentObj[name] = new JArray();
                    var arr = (JArray)currentObj[name]!;
                    EnsureArraySize(arr, index.Value + 1);

                    if (isLast)
                    {
                        arr[index.Value] = value;
                        return;
                    }

                    if (arr[index.Value].Type != JTokenType.Object)
                        arr[index.Value] = new JObject();
                    current = arr[index.Value];
                }
            }
        }
    }

    private static void EnsureArraySize(JArray arr, int size)
    {
        while (arr.Count < size) arr.Add(JValue.CreateNull());
    }

    private static List<(string Name, int? Index)> ParseSegments(string path)
    {
        var result = new List<(string, int?)>();
        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var openIdx = raw.IndexOf('[');
            if (openIdx >= 0 && raw.EndsWith(']'))
            {
                var name = raw[..openIdx];
                var idxStr = raw[(openIdx + 1)..^1];
                result.Add((name, int.Parse(idxStr, CultureInfo.InvariantCulture)));
            }
            else
            {
                result.Add((raw, null));
            }
        }
        return result;
    }
}

public interface IDataTransformer
{
    string Transform(string sourceJson, IReadOnlyList<MappingRule> mappings);
}
