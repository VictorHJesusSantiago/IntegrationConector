using System.Diagnostics;
using System.Text;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Transformation;
using Newtonsoft.Json;

namespace IntegrationConnector.Connectors.Files;

/// <summary>
/// Conector para arquivos locais ou em diretórios de rede já montados (CSV, JSON, XML, Excel).
/// <see cref="ConnectorOperation.Target"/> é o caminho relativo do arquivo dentro de RootDirectory.
/// </summary>
public class FileConnectorPlugin : IConnectorPlugin
{
    public ConnectorType Type => ConnectorType.File;

    public async Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var path = ResolvePath(config, operation.Target);

        return config.FileFormat.ToLowerInvariant() switch
        {
            "csv" => FormatConverter.CsvToJson(await File.ReadAllTextAsync(path, ct), config.CsvDelimiter, config.CsvHasHeader),
            "xml" => FormatConverter.XmlToJson(await File.ReadAllTextAsync(path, ct)),
            "excel" => await ReadExcelAsync(path, config.ExcelSheetName, ct),
            _ => await File.ReadAllTextAsync(path, ct)
        };
    }

    public async Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var path = ResolvePath(config, operation.Target);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        switch (config.FileFormat.ToLowerInvariant())
        {
            case "csv":
                await File.WriteAllTextAsync(path, FormatConverter.JsonToCsv(payloadJson, config.CsvDelimiter, config.CsvHasHeader), Encoding.UTF8, ct);
                break;
            case "xml":
                await File.WriteAllTextAsync(path, FormatConverter.JsonToXml(payloadJson), Encoding.UTF8, ct);
                break;
            case "excel":
                await File.WriteAllBytesAsync(path, FormatConverter.JsonToExcel(payloadJson, config.ExcelSheetName), ct);
                break;
            default:
                await File.WriteAllTextAsync(path, payloadJson, Encoding.UTF8, ct);
                break;
        }
    }

    public Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var sw = Stopwatch.StartNew();
        var exists = Directory.Exists(config.RootDirectory);
        sw.Stop();
        return Task.FromResult(new ConnectorTestResult
        {
            Success = exists,
            Message = exists ? "Diretório acessível" : $"Diretório '{config.RootDirectory}' não encontrado",
            LatencyMs = sw.ElapsedMilliseconds
        });
    }

    private static async Task<string> ReadExcelAsync(string path, string sheetName, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return FormatConverter.ExcelToJson(stream, sheetName);
    }

    private static string ResolvePath(FileConnectorConfig config, string target)
        => Path.GetFullPath(Path.Combine(config.RootDirectory, target));

    private static FileConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<FileConnectorConfig>(connector.ConfigurationJson) ?? new FileConnectorConfig();
}
