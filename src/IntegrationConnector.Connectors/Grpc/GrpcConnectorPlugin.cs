using System.Diagnostics;
using System.Text;
using Grpc.Core;
using Grpc.Net.Client;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Newtonsoft.Json;

namespace IntegrationConnector.Connectors.Grpc;

/// <summary>
/// Conector gRPC simplificado: em vez de exigir contratos .proto compilados (inviável para integração
/// genérica plugável), assume que o método unário alvo aceita e retorna uma única mensagem de texto
/// (JSON serializado como string UTF-8) — um padrão comum em serviços internos "JSON-over-gRPC".
/// Target = nome completo do método no formato "/pacote.Servico/Metodo".
/// </summary>
public class GrpcConnectorPlugin : IConnectorPlugin
{
    private static readonly Marshaller<string> JsonMarshaller = new(
        serializer: s => Encoding.UTF8.GetBytes(s),
        deserializer: bytes => Encoding.UTF8.GetString(bytes));

    public ConnectorType Type => ConnectorType.Grpc;

    public async Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        using var channel = GrpcChannel.ForAddress(config.Address);
        var invoker = channel.CreateCallInvoker();

        var method = BuildMethod(operation.Target);
        var requestPayload = operation.PayloadTemplateJson ?? "{}";

        using var call = invoker.AsyncUnaryCall(method, null, new CallOptions(deadline: DateTime.UtcNow.AddSeconds(config.TimeoutSeconds), cancellationToken: ct), requestPayload);
        return await call.ResponseAsync;
    }

    public async Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        using var channel = GrpcChannel.ForAddress(config.Address);
        var invoker = channel.CreateCallInvoker();

        var method = BuildMethod(operation.Target);
        using var call = invoker.AsyncUnaryCall(method, null, new CallOptions(deadline: DateTime.UtcNow.AddSeconds(config.TimeoutSeconds), cancellationToken: ct), payloadJson);
        await call.ResponseAsync;
    }

    public async Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var sw = Stopwatch.StartNew();
        try
        {
            using var channel = GrpcChannel.ForAddress(config.Address);
            await channel.ConnectAsync(cancellationToken: ct);
            sw.Stop();
            return new ConnectorTestResult { Success = true, Message = "Canal gRPC conectado", LatencyMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectorTestResult { Success = false, Message = ex.Message, LatencyMs = sw.ElapsedMilliseconds };
        }
    }

    private static Method<string, string> BuildMethod(string fullMethodName)
    {
        var trimmed = fullMethodName.Trim('/');
        var parts = trimmed.Split('/');
        var serviceName = parts.Length > 1 ? parts[0] : "GenericService";
        var methodName = parts.Length > 1 ? parts[1] : trimmed;

        return new Method<string, string>(MethodType.Unary, serviceName, methodName, JsonMarshaller, JsonMarshaller);
    }

    private static GrpcConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<GrpcConnectorConfig>(connector.ConfigurationJson) ?? new GrpcConnectorConfig();
}
