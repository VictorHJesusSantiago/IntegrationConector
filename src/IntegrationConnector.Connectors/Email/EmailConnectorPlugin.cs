using System.Diagnostics;
using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IntegrationConnector.Connectors.Email;

/// <summary>
/// Conector de e-mail: leitura via IMAP (mensagens não lidas da pasta configurada, com anexos salvos
/// localmente e referenciados no payload) e escrita via SMTP (envio de relatório/notificação).
/// Read: Target é ignorado (usa MailboxFolder da config). Write: Target = destinatário; PayloadTemplateJson
/// pode conter {"subject": "..."} mesclado com o payload transformado (campo "body").
/// </summary>
public class EmailConnectorPlugin : IConnectorPlugin
{
    public async Task<string> ReadAsync(Connector connector, ConnectorOperation operation, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        using var client = new ImapClient();
        await client.ConnectAsync(config.ImapHost, config.ImapPort, config.ImapUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(config.Username, config.Password, ct);

        var folder = await client.GetFolderAsync(config.MailboxFolder, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        var uids = await folder.SearchAsync(MailKit.Search.SearchQuery.NotSeen, ct);
        var messages = new JArray();

        Directory.CreateDirectory(config.AttachmentDownloadDirectory);

        foreach (var uid in uids)
        {
            var message = await folder.GetMessageAsync(uid, ct);
            var attachmentPaths = new JArray();

            foreach (var attachment in message.Attachments)
            {
                var fileName = attachment.ContentDisposition?.FileName ?? attachment.ContentType.Name ?? $"{Guid.NewGuid()}.bin";
                var path = Path.Combine(config.AttachmentDownloadDirectory, fileName);
                await using var fileStream = File.Create(path);
                if (attachment is MimePart part) await part.Content.DecodeToAsync(fileStream, ct);
                attachmentPaths.Add(path);
            }

            messages.Add(new JObject
            {
                ["subject"] = message.Subject,
                ["from"] = message.From.ToString(),
                ["date"] = message.Date.UtcDateTime,
                ["body"] = message.TextBody ?? message.HtmlBody,
                ["attachments"] = attachmentPaths
            });

            await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
        }

        await client.DisconnectAsync(true, ct);
        return messages.ToString(Newtonsoft.Json.Formatting.None);
    }

    public async Task WriteAsync(Connector connector, ConnectorOperation operation, string payloadJson, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var payload = JObject.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
        var template = string.IsNullOrWhiteSpace(operation.PayloadTemplateJson) ? new JObject() : JObject.Parse(operation.PayloadTemplateJson);

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(config.FromAddress));
        message.To.Add(MailboxAddress.Parse(operation.Target));
        message.Subject = template["subject"]?.ToString() ?? payload["subject"]?.ToString() ?? "Notificação da Plataforma de Integração";
        message.Body = new TextPart("plain") { Text = payload["body"]?.ToString() ?? payload.ToString() };

        using var client = new SmtpClient();
        await client.ConnectAsync(config.SmtpHost, config.SmtpPort, config.SmtpUseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        await client.AuthenticateAsync(config.Username, config.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    public ConnectorType Type => ConnectorType.Email;

    public async Task<ConnectorTestResult> TestConnectionAsync(Connector connector, CancellationToken ct)
    {
        var config = ParseConfig(connector);
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(config.ImapHost, config.ImapPort, config.ImapUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(config.Username, config.Password, ct);
            await client.DisconnectAsync(true, ct);
            sw.Stop();
            return new ConnectorTestResult { Success = true, Message = "Autenticado via IMAP", LatencyMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectorTestResult { Success = false, Message = ex.Message, LatencyMs = sw.ElapsedMilliseconds };
        }
    }

    private static EmailConnectorConfig ParseConfig(Connector connector)
        => JsonConvert.DeserializeObject<EmailConnectorConfig>(connector.ConfigurationJson) ?? new EmailConnectorConfig();
}
