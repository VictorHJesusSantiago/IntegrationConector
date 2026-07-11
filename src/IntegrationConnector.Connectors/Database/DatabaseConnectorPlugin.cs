using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace IntegrationConnector.Connectors.Database;

/// <summary>
/// Conector genérico para bancos relacionais (Postgres ou SQL Server) via ADO.NET.
/// Read: <see cref="ConnectorOperation.Target"/> contém a instrução SQL (SELECT) ou o nome da stored
/// procedure (quando Action = "StoredProcedure") a executar.
/// Write: idem, para INSERT/UPDATE/MERGE ou stored procedure, com parâmetros nomeados (@campo)
/// preenchidos a partir do payload.
/// Conexões Postgres reutilizam um <see cref="NpgsqlDataSource"/> em pool por conector (API recomendada
/// pelo driver), evitando reabrir o pool subjacente a cada execução.
/// </summary>
public class DatabaseConnectorPlugin : IConnectorPlugin
{
    private static readonly ConcurrentDictionary<Guid, NpgsqlDataSource> PostgresPools = new();

    public ConnectorType Type => ConnectorType.Database;

    public async Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        await using var conn = await OpenConnectionAsync(connector.Id, config, ct);

        await using var cmd = conn.CreateCommand();
        ConfigureCommand(cmd, operation, config);

        var results = new JArray();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new JObject();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[reader.GetName(i)] = value is null ? JValue.CreateNull() : JToken.FromObject(value);
            }
            results.Add(row);
        }

        return results.ToString(Newtonsoft.Json.Formatting.None);
    }

    public async Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        await using var conn = await OpenConnectionAsync(connector.Id, config, ct);

        var token = JToken.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
        var rows = token is JArray arr ? arr.Cast<JObject>().ToList() : new List<JObject> { (JObject)token };

        await using var transaction = await conn.BeginTransactionAsync(ct);
        foreach (var row in rows)
        {
            await using var cmd = conn.CreateCommand();
            ConfigureCommand(cmd, operation, config);
            cmd.Transaction = transaction;

            foreach (var prop in row.Properties())
            {
                var param = cmd.CreateParameter();
                param.ParameterName = "@" + prop.Name;
                param.Value = prop.Value.Type == JTokenType.Null ? DBNull.Value : prop.Value.ToObject<object>() ?? DBNull.Value;
                cmd.Parameters.Add(param);
            }

            await cmd.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = await OpenConnectionAsync(connector.Id, config, ct);
            sw.Stop();
            return new ConnectorTestResult { Success = true, Message = "Conexão estabelecida (pool reutilizável)", LatencyMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectorTestResult { Success = false, Message = ex.Message, LatencyMs = sw.ElapsedMilliseconds };
        }
    }

    private static void ConfigureCommand(DbCommand cmd, ConnectorOperation operation, DatabaseConnectorConfig config)
    {
        cmd.CommandText = operation.Target;
        cmd.CommandTimeout = config.CommandTimeoutSeconds;
        cmd.CommandType = string.Equals(operation.Action, "StoredProcedure", StringComparison.OrdinalIgnoreCase)
            ? CommandType.StoredProcedure
            : CommandType.Text;
    }

    /// <summary>
    /// Postgres: reutiliza um NpgsqlDataSource (pool nativo) por conector. SQL Server: o driver já
    /// pool a nível de connection string por padrão (Pooling=true), então uma conexão simples é suficiente.
    /// </summary>
    private static async Task<DbConnection> OpenConnectionAsync(Guid connectorId, DatabaseConnectorConfig config, CancellationToken ct)
    {
        if (config.Provider == "SqlServer")
        {
            var sqlConn = new SqlConnection(config.ConnectionString);
            await sqlConn.OpenAsync(ct);
            return sqlConn;
        }

        var dataSource = PostgresPools.GetOrAdd(connectorId, _ => NpgsqlDataSource.Create(config.ConnectionString));
        return await dataSource.OpenConnectionAsync(ct);
    }

    private static DatabaseConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<DatabaseConnectorConfig>(connector.ConfigurationJson) ?? new DatabaseConnectorConfig();
}
