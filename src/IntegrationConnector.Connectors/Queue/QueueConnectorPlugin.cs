using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Newtonsoft.Json;
using RabbitMQ.Client;

namespace IntegrationConnector.Connectors.Queue;

/// <summary>
/// Conector para filas de mensagens RabbitMQ. Read consome uma mensagem (Target = nome da fila);
/// Write publica o payload transformado na fila indicada. A conexão TCP com o broker é reutilizada
/// por conector (pool simples em memória), abrindo apenas um canal novo por operação.
/// </summary>
public class QueueConnectorPlugin : IConnectorPlugin, IDisposable
{
    private static readonly ConcurrentDictionary<Guid, IConnection> ConnectionPool = new();

    public ConnectorType Type => ConnectorType.Queue;

    public Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var conn = GetOrCreateConnection(connector);
        using var channel = conn.CreateModel();
        channel.QueueDeclare(operation.Target, ParseConfig(connector).Durable, false, false, null);

        var result = channel.BasicGet(operation.Target, autoAck: true);
        if (result is null) return Task.FromResult("null");

        var body = Encoding.UTF8.GetString(result.Body.ToArray());
        return Task.FromResult(body);
    }

    public Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var conn = GetOrCreateConnection(connector);
        using var channel = conn.CreateModel();
        channel.QueueDeclare(operation.Target, config.Durable, false, false, null);

        var properties = channel.CreateBasicProperties();
        properties.Persistent = config.Durable;
        properties.ContentType = "application/json";

        var body = Encoding.UTF8.GetBytes(payloadJson);
        channel.BasicPublish(string.Empty, operation.Target, properties, body);
        return Task.CompletedTask;
    }

    public Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var conn = GetOrCreateConnection(connector);
            sw.Stop();
            return Task.FromResult(new ConnectorTestResult { Success = conn.IsOpen, Message = "Conectado ao broker (pool reutilizável)", LatencyMs = sw.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(new ConnectorTestResult { Success = false, Message = ex.Message, LatencyMs = sw.ElapsedMilliseconds });
        }
    }

    private static IConnection GetOrCreateConnection(Connector connector)
    {
        return ConnectionPool.AddOrUpdate(
            connector.Id,
            _ => CreateFactory(ParseConfig(connector)).CreateConnection(),
            (_, existing) => existing.IsOpen ? existing : CreateFactory(ParseConfig(connector)).CreateConnection());
    }

    private static ConnectionFactory CreateFactory(QueueConnectorConfig config) => new()
    {
        HostName = config.HostName,
        Port = config.Port,
        UserName = config.Username,
        Password = config.Password,
        VirtualHost = config.VirtualHost,
        AutomaticRecoveryEnabled = true
    };

    private static QueueConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<QueueConnectorConfig>(connector.ConfigurationJson) ?? new QueueConnectorConfig();

    public void Dispose()
    {
        foreach (var conn in ConnectionPool.Values)
        {
            try { if (conn.IsOpen) conn.Close(); conn.Dispose(); } catch { /* melhor esforço no encerramento do pool */ }
        }
        ConnectionPool.Clear();
    }
}
