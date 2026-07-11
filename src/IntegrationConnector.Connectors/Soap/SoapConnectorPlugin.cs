using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IntegrationConnector.Connectors.Soap;

/// <summary>
/// Conector genérico para serviços SOAP 1.1 via HTTP. Envia o corpo (payload transformado)
/// dentro do envelope SOAP e converte a resposta XML para JSON para uso no restante do pipeline.
/// </summary>
public class SoapConnectorPlugin : IConnectorPlugin
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SoapConnectorPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public ConnectorType Type => ConnectorType.Soap;

    public async Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var envelope = BuildEnvelope(operation.PayloadTemplateJson, operation.Action);
        var responseXml = await SendAsync(config, envelope, ct);
        return XmlToJson(responseXml);
    }

    public async Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var envelope = BuildEnvelopeFromJson(payloadJson, operation.Action);
        await SendAsync(config, envelope, ct);
    }

    public async Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = _httpClientFactory.CreateClient("soap-connector");
            using var response = await client.GetAsync(config.EndpointUrl + "?wsdl", ct);
            sw.Stop();
            return new ConnectorTestResult { Success = response.IsSuccessStatusCode, Message = $"HTTP {(int)response.StatusCode}", LatencyMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectorTestResult { Success = false, Message = ex.Message, LatencyMs = sw.ElapsedMilliseconds };
        }
    }

    private async Task<string> SendAsync(SoapConnectorConfig config, string envelopeXml, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("soap-connector");
        client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Post, config.EndpointUrl)
        {
            Content = new StringContent(envelopeXml, Encoding.UTF8, "text/xml")
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
        if (!string.IsNullOrWhiteSpace(config.SoapAction))
            request.Headers.TryAddWithoutValidation("SOAPAction", config.SoapAction);

        if (!string.IsNullOrWhiteSpace(config.Username))
        {
            var bytes = Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        return body;
    }

    private static string BuildEnvelope(string? bodyContentJson, string action)
        => BuildEnvelopeFromJson(bodyContentJson ?? "{}", action);

    private static string BuildEnvelopeFromJson(string payloadJson, string action)
    {
        var obj = JsonConvert.DeserializeObject<Dictionary<string, object?>>(payloadJson) ?? new();
        var bodyElement = new XElement(XName.Get(string.IsNullOrWhiteSpace(action) ? "Request" : action));
        foreach (var kv in obj)
            bodyElement.Add(new XElement(XName.Get(kv.Key), kv.Value?.ToString()));

        XNamespace soapNs = "http://schemas.xmlsoap.org/soap/envelope/";
        var envelope = new XElement(soapNs + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", soapNs),
            new XElement(soapNs + "Body", bodyElement));

        return new XDocument(envelope).ToString(SaveOptions.DisableFormatting);
    }

    private static string XmlToJson(string xml)
    {
        var doc = XDocument.Parse(xml);
        var body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");
        var content = body?.Elements().FirstOrDefault() ?? doc.Root;
        return JsonConvert.SerializeXNode(content ?? new XElement("Response"), Newtonsoft.Json.Formatting.None, omitRootObject: true);
    }

    private static SoapConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<SoapConnectorConfig>(connector.ConfigurationJson) ?? new SoapConnectorConfig();
}
