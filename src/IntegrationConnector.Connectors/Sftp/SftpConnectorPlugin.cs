using System.Diagnostics;
using System.Text;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Newtonsoft.Json;
using Renci.SshNet;

namespace IntegrationConnector.Connectors.Sftp;

/// <summary>Conector SFTP nativo (SSH.NET), com suporte a autenticação por senha e/ou chave privada.</summary>
public class SftpConnectorPlugin : IConnectorPlugin
{
    public ConnectorType Type => ConnectorType.Sftp;

    public Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        using var client = CreateClient(config);
        client.Connect();

        var remotePath = CombinePath(config.RemoteDirectory, operation.Target);
        using var stream = new MemoryStream();
        client.DownloadFile(remotePath, stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return Task.FromResult(reader.ReadToEnd());
    }

    public Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        using var client = CreateClient(config);
        client.Connect();

        var remotePath = CombinePath(config.RemoteDirectory, operation.Target);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payloadJson));
        client.UploadFile(stream, remotePath, canOverride: true);
        return Task.CompletedTask;
    }

    public Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = CreateClient(config);
            client.Connect();
            sw.Stop();
            return Task.FromResult(new ConnectorTestResult { Success = client.IsConnected, Message = "Conectado via SFTP", LatencyMs = sw.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(new ConnectorTestResult { Success = false, Message = ex.Message, LatencyMs = sw.ElapsedMilliseconds });
        }
    }

    private static SftpClient CreateClient(SftpConnectorConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.PrivateKeyContent))
        {
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(config.PrivateKeyContent));
            var keyFile = string.IsNullOrWhiteSpace(config.PrivateKeyPassphrase)
                ? new PrivateKeyFile(keyStream)
                : new PrivateKeyFile(keyStream, config.PrivateKeyPassphrase);

            var authMethods = new List<AuthenticationMethod> { new PrivateKeyAuthenticationMethod(config.Username, keyFile) };
            if (!string.IsNullOrWhiteSpace(config.Password))
                authMethods.Add(new PasswordAuthenticationMethod(config.Username, config.Password));

            var connectionInfo = new ConnectionInfo(config.Host, config.Port, config.Username, authMethods.ToArray());
            return new SftpClient(connectionInfo);
        }

        return new SftpClient(config.Host, config.Port, config.Username, config.Password ?? string.Empty);
    }

    private static string CombinePath(string dir, string file) => $"{dir.TrimEnd('/')}/{file.TrimStart('/')}";

    private static SftpConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<SftpConnectorConfig>(connector.ConfigurationJson) ?? new SftpConnectorConfig();
}
