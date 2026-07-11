using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IntegrationConnector.Connectors.GraphQl;

/// <summary>
/// Conector GraphQL: envia POST { query, variables } ao endpoint único e devolve o campo "data" da
/// resposta como payload JSON canônico. Reaproveita a semântica de operação do REST: Action é ignorado
/// (sempre POST), Target é ignorado, PayloadTemplateJson deve conter {"query": "...", "variables": {...}}.
/// </summary>
public class GraphQlConnectorPlugin : IConnectorPlugin
{
    private readonly IHttpClientFactory _httpClientFactory;

    public GraphQlConnectorPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public ConnectorType Type => ConnectorType.GraphQl;

    public async Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var requestBody = BuildRequestBody(operation, null);
        var response = await SendAsync(config, requestBody, ct);
        return ExtractData(response);
    }

    public async Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var requestBody = BuildRequestBody(operation, payloadJson);
        await SendAsync(config, requestBody, ct);
    }

    public async Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var sw = Stopwatch.StartNew();
        try
        {
            var body = JsonConvert.SerializeObject(new { query = "{ __typename }" });
            var response = await SendAsync(config, body, ct);
            sw.Stop();
            return new ConnectorTestResult { Success = true, Message = "Endpoint respondeu", LatencyMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectorTestResult { Success = false, Message = ex.Message, LatencyMs = sw.ElapsedMilliseconds };
        }
    }

    private async Task<string> SendAsync(GraphQlConnectorConfig config, string requestBody, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("graphql-connector");
        client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

        switch (config.AuthType)
        {
            case "Bearer":
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.BearerToken);
                break;
            case "ApiKey":
                if (!string.IsNullOrWhiteSpace(config.ApiKeyHeaderName))
                    client.DefaultRequestHeaders.TryAddWithoutValidation(config.ApiKeyHeaderName, config.ApiKeyValue);
                break;
        }

        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(config.EndpointUrl, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        return body;
    }

    private static string BuildRequestBody(ConnectorOperation operation, string? payloadJson)
    {
        var template = string.IsNullOrWhiteSpace(operation.PayloadTemplateJson)
            ? new JObject { ["query"] = operation.Action, ["variables"] = new JObject() }
            : JObject.Parse(operation.PayloadTemplateJson);

        if (payloadJson is not null)
        {
            var variables = template["variables"] as JObject ?? new JObject();
            var payload = JObject.Parse(payloadJson);
            foreach (var prop in payload.Properties()) variables[prop.Name] = prop.Value;
            template["variables"] = variables;
        }

        return template.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string ExtractData(string responseJson)
    {
        var response = JObject.Parse(responseJson);
        var errors = response["errors"];
        if (errors is JArray { Count: > 0 })
            throw new InvalidOperationException($"Erro GraphQL: {errors}");

        return (response["data"] ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None);
    }

    private static GraphQlConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<GraphQlConnectorConfig>(connector.ConfigurationJson) ?? new GraphQlConnectorConfig();
}
