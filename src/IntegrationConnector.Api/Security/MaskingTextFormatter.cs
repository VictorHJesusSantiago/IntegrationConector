using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace IntegrationConnector.Api.Security;

/// <summary>
/// Formatter Serilog que mascara dados sensíveis (CPF, e-mail) na mensagem renderizada antes de
/// escrevê-la no sink de console, evitando vazamento de PII em logs.
/// </summary>
public class MaskingTextFormatter : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _inner = new(
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

    private static readonly Regex CpfPattern = new(@"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        _inner.Format(logEvent, buffer);

        var masked = Mask(buffer.ToString());
        output.Write(masked);
    }

    public static string Mask(string text)
    {
        text = CpfPattern.Replace(text, "***.***.***-**");
        text = EmailPattern.Replace(text, match =>
        {
            var parts = match.Value.Split('@');
            return parts.Length == 2 ? $"***@{parts[1]}" : "***";
        });
        return text;
    }
}
