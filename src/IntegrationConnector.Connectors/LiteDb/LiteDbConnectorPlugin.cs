using System.Diagnostics;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using LiteDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IntegrationConnector.Connectors.LiteDb;

/// <summary>
/// Conector para banco NoSQL embarcado (LiteDB): arquivo .db local, sem processo de servidor.
/// Read: Target é a coleção a consultar (retorna todos os documentos); Write: Target é a coleção
/// onde os documentos transformados são inseridos/atualizados (upsert por campo "_id" quando presente).
/// </summary>
public class LiteDbConnectorPlugin : IConnectorPlugin
{
    public ConnectorType Type => ConnectorType.LiteDb;

    public Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        Directory.CreateDirectory(Path.GetDirectoryName(config.DatabasePath) ?? ".");

        using var db = new LiteDatabase(config.DatabasePath);
        var collectionName = string.IsNullOrWhiteSpace(operation.Target) ? config.CollectionName : operation.Target;
        var collection = db.GetCollection(collectionName);

        var results = new JArray();
        foreach (var doc in collection.FindAll())
            results.Add(JObject.Parse(LiteDB.JsonSerializer.Serialize(doc)));

        return Task.FromResult(results.ToString(Newtonsoft.Json.Formatting.None));
    }

    public Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        Directory.CreateDirectory(Path.GetDirectoryName(config.DatabasePath) ?? ".");

        using var db = new LiteDatabase(config.DatabasePath);
        var collectionName = string.IsNullOrWhiteSpace(operation.Target) ? config.CollectionName : operation.Target;
        var collection = db.GetCollection(collectionName);

        var token = JToken.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
        var items = token is JArray arr ? arr.ToList() : new List<JToken> { token };

        foreach (var item in items)
        {
            var bsonDoc = LiteDB.JsonSerializer.Deserialize(item.ToString(Newtonsoft.Json.Formatting.None)).AsDocument;
            collection.Upsert(bsonDoc);
        }

        return Task.CompletedTask;
    }

    public Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var sw = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(config.DatabasePath) ?? ".");
            using var db = new LiteDatabase(config.DatabasePath);
            _ = db.GetCollectionNames().ToList();
            sw.Stop();
            return Task.FromResult(new ConnectorTestResult { Success = true, Message = "Arquivo LiteDB acessível", LatencyMs = sw.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(new ConnectorTestResult { Success = false, Message = ex.Message, LatencyMs = sw.ElapsedMilliseconds });
        }
    }

    private static LiteDbConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<LiteDbConnectorConfig>(connector.ConfigurationJson) ?? new LiteDbConnectorConfig();
}
