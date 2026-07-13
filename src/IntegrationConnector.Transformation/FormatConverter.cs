using System.Globalization;
using System.Xml.Linq;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IntegrationConnector.Transformation;

/// <summary>
/// Conversores de formato reutilizáveis (JSON canônico &lt;-&gt; CSV / XML / Excel), usados pelo
/// conector de arquivo e como etapa de conversão de wire-format (<see cref="Core.Enums.PayloadFormat"/>)
/// em outros conectores (REST, SOAP, FTP, SFTP).
/// </summary>
public static class FormatConverter
{
    public static string CsvToJson(string csv, string delimiter, bool hasHeader)
    {
        using var reader = new StringReader(csv);
        using var csvReader = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = hasHeader
        });

        var result = new JArray();
        if (hasHeader)
        {
            csvReader.Read();
            csvReader.ReadHeader();
            while (csvReader.Read())
            {
                var row = new JObject();
                foreach (var header in csvReader.HeaderRecord ?? Array.Empty<string>())
                    row[header] = csvReader.GetField(header);
                result.Add(row);
            }
        }
        else
        {
            while (csvReader.Read())
            {
                var row = new JArray();
                int i = 0;
                while (csvReader.TryGetField<string>(i, out var value)) { row.Add(value); i++; }
                result.Add(row);
            }
        }

        return result.ToString(Formatting.None);
    }

    public static string JsonToCsv(string json, string delimiter, bool includeHeader)
    {
        var token = JToken.Parse(json);
        var array = token is JArray arr ? arr : new JArray(token);

        using var writer = new StringWriter();
        using var csvWriter = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = delimiter });

        var columns = array.OfType<JObject>()
            .SelectMany(o => o.Properties().Select(p => p.Name))
            .Distinct()
            .ToList();

        if (includeHeader)
        {
            foreach (var col in columns) csvWriter.WriteField(col);
            csvWriter.NextRecord();
        }

        foreach (var item in array.OfType<JObject>())
        {
            foreach (var col in columns) csvWriter.WriteField(item[col]?.ToString() ?? string.Empty);
            csvWriter.NextRecord();
        }

        return writer.ToString();
    }

    public static string XmlToJson(string xml)
    {
        var doc = XDocument.Parse(xml);
        return JsonConvert.SerializeXNode(doc, Formatting.None);
    }

    public static string JsonToXml(string json, string rootElementName = "Root")
    {
        var token = JToken.Parse(json);
        var wrapped = token is JArray ? new JObject { [rootElementName] = new JObject { ["Item"] = token } } : (JObject)token;
        var doc = JsonConvert.DeserializeXNode(wrapped.ToString(), rootElementName);
        return doc!.ToString();
    }

    public static string ExcelToJson(Stream excelStream, string sheetName)
    {
        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.Contains(sheetName) ? workbook.Worksheet(sheetName) : workbook.Worksheet(1);
        var usedRange = worksheet.RangeUsed();
        if (usedRange is null) return "[]";

        var rows = usedRange.RowsUsed().ToList();
        if (rows.Count == 0) return "[]";

        var headerRow = rows[0];
        var headers = headerRow.CellsUsed().Select(c => c.GetString()).ToList();

        var result = new JArray();
        foreach (var row in rows.Skip(1))
        {
            var obj = new JObject();
            for (int i = 0; i < headers.Count; i++)
                obj[headers[i]] = row.Cell(i + 1).GetString();
            result.Add(obj);
        }

        return result.ToString(Formatting.None);
    }

    public static byte[] JsonToExcel(string json, string sheetName)
    {
        var token = JToken.Parse(json);
        var array = token is JArray arr ? arr : new JArray(token);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName);

        var columns = array.OfType<JObject>()
            .SelectMany(o => o.Properties().Select(p => p.Name))
            .Distinct()
            .ToList();

        for (int c = 0; c < columns.Count; c++)
            worksheet.Cell(1, c + 1).Value = columns[c];

        int r = 2;
        foreach (var item in array.OfType<JObject>())
        {
            for (int c = 0; c < columns.Count; c++)
                worksheet.Cell(r, c + 1).Value = item[columns[c]]?.ToString() ?? string.Empty;
            r++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Converte um payload no formato indicado para o JSON canônico usado internamente pelo motor.</summary>
    public static string ToCanonicalJson(string raw, Core.Enums.PayloadFormat format) => format switch
    {
        Core.Enums.PayloadFormat.Csv => CsvToJson(raw, ",", true),
        Core.Enums.PayloadFormat.Xml => XmlToJson(raw),
        _ => raw
    };

    /// <summary>Converte um JSON canônico para o formato de saída indicado (usado antes de gravar no destino).</summary>
    public static string FromCanonicalJson(string json, Core.Enums.PayloadFormat format) => format switch
    {
        Core.Enums.PayloadFormat.Csv => JsonToCsv(json, ",", true),
        Core.Enums.PayloadFormat.Xml => JsonToXml(json),
        _ => json
    };
}
