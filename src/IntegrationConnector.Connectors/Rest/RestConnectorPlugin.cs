using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IntegrationConnector.Connectors.Rest;

/// <summary>
/// Conector genérico para APIs REST (JSON), com suporte a autenticação Basic/Bearer/ApiKey/OAuth2
/// (client-credentials, com cache e renovação automática de token) e paginação automática na leitura
/// (modo "NextLink" segue uma URL indicada no corpo da resposta; modo "PageNumber" incrementa um
/// parâmetro de página até a página parar de retornar itens ou atingir o limite configurado).
/// </summary>
public class RestConnectorPlugin : IConnectorPlugin
{
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly ConcurrentDictionary<Guid, (string Token, DateTime ExpiresAtUtc)> OAuthTokenCache = new();

    public RestConnectorPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public ConnectorType Type => ConnectorType.Rest;

    public async Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        using var client = await BuildClientAsync(connector.Id, config, ct);

        if (operation.Pagination is not null)
            return await ReadPaginatedAsync(client, config, operation, ct);

        var method = new HttpMethod(string.IsNullOrWhiteSpace(operation.Action) ? "GET" : operation.Action);
        using var request = new HttpRequestMessage(method, BuildUrl(config.BaseUrl, operation.Target));
        ApplyHeaders(request, operation);

        if (!string.IsNullOrWhiteSpace(operation.PayloadTemplateJson) && method != HttpMethod.Get)
        {
            request.Content = new StringContent(operation.PayloadTemplateJson, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        return string.IsNullOrWhiteSpace(content) ? "{}" : content;
    }

    private static async Task<string> ReadPaginatedAsync(HttpClient client, RestConnectorConfig config, ConnectorOperation operation, CancellationToken ct)
    {
        var pagination = operation.Pagination!;
        var allItems = new JArray();
        var currentUrl = BuildUrl(config.BaseUrl, operation.Target);
        var pageNumber = 1;

        for (int page = 0; page < pagination.MaxPages; page++)
        {
            var urlForThisPage = pagination.Mode == "PageNumber"
                ? AppendQueryParam(currentUrl, pagination.PageParam, pageNumber.ToString(CultureInfo.InvariantCulture))
                : currentUrl;

            using var request = new HttpRequestMessage(HttpMethod.Get, urlForThisPage);
            ApplyHeaders(request, operation);

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            response.EnsureSuccessStatusCode();

            var json = JToken.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var items = json.SelectToken(NormalizeJsonPath(pagination.ItemsPath));

            if (items is JArray itemsArray)
            {
                if (itemsArray.Count == 0) break;
                foreach (var item in itemsArray) allItems.Add(item);
            }
            else if (items is not null)
            {
                allItems.Add(items);
            }
            else
            {
                break;
            }

            if (pagination.Mode == "PageNumber")
            {
                pageNumber++;
            }
            else
            {
                var next = json.SelectToken(NormalizeJsonPath(pagination.NextLinkPath))?.Value<string>();
                if (string.IsNullOrWhiteSpace(next)) break;
                currentUrl = IsAbsoluteHttpUrl(next) ? next : BuildUrl(config.BaseUrl, next);
            }
        }

        return allItems.ToString(Newtonsoft.Json.Formatting.None);
    }

    public async Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        using var client = await BuildClientAsync(connector.Id, config, ct);

        var method = new HttpMethod(string.IsNullOrWhiteSpace(operation.Action) ? "POST" : operation.Action);
        using var request = new HttpRequestMessage(method, BuildUrl(config.BaseUrl, operation.Target));
        ApplyHeaders(request, operation);

        var mergedPayload = MergeTemplate(operation.PayloadTemplateJson, payloadJson);
        request.Content = new StringContent(mergedPayload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha ao escrever no conector REST ({(int)response.StatusCode}): {body}");
    }

    public async Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = await BuildClientAsync(connector.Id, config, ct);
            using var response = await client.GetAsync(config.BaseUrl, ct);
            sw.Stop();
            return new ConnectorTestResult
            {
                Success = response.IsSuccessStatusCode,
                Message = $"HTTP {(int)response.StatusCode}",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectorTestResult { Success = false, Message = ex.Message, LatencyMs = sw.ElapsedMilliseconds };
        }
    }

    private async Task<HttpClient> BuildClientAsync(Guid connectorId, RestConnectorConfig config, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("rest-connector");
        client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

        switch (config.AuthType)
        {
            case "Basic":
                var bytes = Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
                break;
            case "Bearer":
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.BearerToken);
                break;
            case "ApiKey":
                if (!string.IsNullOrWhiteSpace(config.ApiKeyHeaderName))
                    client.DefaultRequestHeaders.TryAddWithoutValidation(config.ApiKeyHeaderName, config.ApiKeyValue);
                break;
            case "OAuth2ClientCredentials":
                var token = await GetOrRefreshOAuthTokenAsync(connectorId, config, ct);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;
        }

        return client;
    }

    /// <summary>Obtém um token via client-credentials, reutilizando-o do cache até 60s antes de expirar.</summary>
    private async Task<string> GetOrRefreshOAuthTokenAsync(Guid connectorId, RestConnectorConfig config, CancellationToken ct)
    {
        if (OAuthTokenCache.TryGetValue(connectorId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow.AddSeconds(60))
            return cached.Token;

        using var tokenClient = _httpClientFactory.CreateClient("oauth2-token");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = config.OAuthClientId ?? string.Empty,
            ["client_secret"] = config.OAuthClientSecret ?? string.Empty,
            ["scope"] = config.OAuthScope ?? string.Empty
        });

        using var response = await tokenClient.PostAsync(config.OAuthTokenEndpoint, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        var json = JObject.Parse(body);
        var accessToken = json["access_token"]?.Value<string>() ?? throw new InvalidOperationException("Resposta OAuth2 sem access_token.");
        var expiresIn = json["expires_in"]?.Value<int>() ?? 3600;

        var expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
        OAuthTokenCache[connectorId] = (accessToken, expiresAt);
        return accessToken;
    }

    private static void ApplyHeaders(HttpRequestMessage request, ConnectorOperation operation)
    {
        foreach (var header in operation.Headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    private static string BuildUrl(string baseUrl, string target)
        => IsAbsoluteHttpUrl(target) ? target : $"{baseUrl.TrimEnd('/')}/{target.TrimStart('/')}";

    /// <summary>
    /// Verifica se a string já é uma URL http(s) absoluta. Não usar apenas
    /// <c>Uri.TryCreate(target, UriKind.Absolute, out _)</c>: em Linux, esse método considera um
    /// caminho iniciado por "/" (ex.: "/api/overview") como uma URI de arquivo absoluta válida
    /// (file:///...), fazendo com que targets relativos comuns sejam tratados como já-absolutos e
    /// o BaseUrl do conector nunca seja concatenado — quebrando toda chamada REST em produção (Linux).
    /// </summary>
    private static bool IsAbsoluteHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string AppendQueryParam(string url, string paramName, string value)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}{paramName}={Uri.EscapeDataString(value)}";
    }

    private static string NormalizeJsonPath(string path)
        => path.StartsWith('$') ? path : "$." + path;

    private static string MergeTemplate(string? templateJson, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(templateJson)) return payloadJson;

        var template = JsonConvert.DeserializeObject<Dictionary<string, object?>>(templateJson) ?? new();
        var payload = JsonConvert.DeserializeObject<Dictionary<string, object?>>(payloadJson) ?? new();
        foreach (var kv in payload) template[kv.Key] = kv.Value;
        return JsonConvert.SerializeObject(template);
    }

    private static RestConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<RestConnectorConfig>(connector.ConfigurationJson) ?? new RestConnectorConfig();
}
