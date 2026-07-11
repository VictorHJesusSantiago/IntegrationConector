using System.Diagnostics;
using FluentFTP;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using Newtonsoft.Json;

namespace IntegrationConnector.Connectors.Ftp;

/// <summary>
/// Conector para servidores FTP/FTPS via FluentFTP. A operação define o arquivo remoto
/// (Target). Para leitura o conteúdo textual do arquivo é devolvido como payload (JSON, CSV etc.);
/// para escrita o payload é gravado como o conteúdo do arquivo remoto.
/// </summary>
public class FtpConnectorPlugin : IConnectorPlugin
{
    public ConnectorType Type => ConnectorType.Ftp;

    public async Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        using var client = CreateClient(config);
        await client.Connect(ct);

        var remotePath = CombinePath(config.RemoteDirectory, operation.Target);
        using var stream = new MemoryStream();
        await client.DownloadStream(stream, remotePath, token: ct);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        using var client = CreateClient(config);
        await client.Connect(ct);

        var remotePath = CombinePath(config.RemoteDirectory, operation.Target);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payloadJson));
        await client.UploadStream(stream, remotePath, FtpRemoteExists.Overwrite, createRemoteDir: true, token: ct);
    }

    public async Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = CreateClient(config);
            await client.Connect(ct);
            sw.Stop();
            return new ConnectorTestResult { Success = true, Message = "Conectado com sucesso", LatencyMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectorTestResult { Success = false, Message = ex.Message, LatencyMs = sw.ElapsedMilliseconds };
        }
    }

    private static AsyncFtpClient CreateClient(FtpConnectorConfig config)
    {
        return new AsyncFtpClient(config.Host, config.Username, config.Password, config.Port)
        {
            Config = { EncryptionMode = config.UseSftp ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None }
        };
    }

    private static string CombinePath(string dir, string file)
        => $"{dir.TrimEnd('/')}/{file.TrimStart('/')}";

    private static FtpConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<FtpConnectorConfig>(connector.ConfigurationJson) ?? new FtpConnectorConfig();
}
