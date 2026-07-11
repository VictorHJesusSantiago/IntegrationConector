using IntegrationConnector.Core.Dtos;
using IntegrationConnector.Core.Enums;
using Newtonsoft.Json;

namespace IntegrationConnector.Api.Validation;

/// <summary>
/// Valida a configuração de um conector contra o "schema" mínimo esperado para seu tipo, antes de
/// persistir. Evita salvar conectores com JSON estruturalmente inválido ou faltando campos obrigatórios.
/// </summary>
public static class ConnectorConfigValidator
{
    public static bool TryValidate(ConnectorType type, string configurationJson, out List<string> errors)
    {
        errors = new List<string>();

        object? parsed;
        try
        {
            parsed = type switch
            {
                ConnectorType.Rest => JsonConvert.DeserializeObject<RestConnectorConfig>(configurationJson),
                ConnectorType.Soap => JsonConvert.DeserializeObject<SoapConnectorConfig>(configurationJson),
                ConnectorType.Ftp => JsonConvert.DeserializeObject<FtpConnectorConfig>(configurationJson),
                ConnectorType.Sftp => JsonConvert.DeserializeObject<SftpConnectorConfig>(configurationJson),
                ConnectorType.Database => JsonConvert.DeserializeObject<DatabaseConnectorConfig>(configurationJson),
                ConnectorType.Queue => JsonConvert.DeserializeObject<QueueConnectorConfig>(configurationJson),
                ConnectorType.File => JsonConvert.DeserializeObject<FileConnectorConfig>(configurationJson),
                ConnectorType.Email => JsonConvert.DeserializeObject<EmailConnectorConfig>(configurationJson),
                ConnectorType.GraphQl => JsonConvert.DeserializeObject<GraphQlConnectorConfig>(configurationJson),
                ConnectorType.Grpc => JsonConvert.DeserializeObject<GrpcConnectorConfig>(configurationJson),
                ConnectorType.LiteDb => JsonConvert.DeserializeObject<LiteDbConnectorConfig>(configurationJson),
                _ => null
            };
        }
        catch (JsonException ex)
        {
            errors.Add($"JSON de configuração inválido: {ex.Message}");
            return false;
        }

        if (parsed is null)
        {
            errors.Add("Configuração vazia ou tipo de conector não reconhecido.");
            return false;
        }

        switch (parsed)
        {
            case RestConnectorConfig rest:
                RequireNonEmpty(rest.BaseUrl, nameof(rest.BaseUrl), errors);
                if (rest.AuthType == "OAuth2ClientCredentials")
                {
                    RequireNonEmpty(rest.OAuthTokenEndpoint, nameof(rest.OAuthTokenEndpoint), errors);
                    RequireNonEmpty(rest.OAuthClientId, nameof(rest.OAuthClientId), errors);
                    RequireNonEmpty(rest.OAuthClientSecret, nameof(rest.OAuthClientSecret), errors);
                }
                break;
            case SoapConnectorConfig soap:
                RequireNonEmpty(soap.EndpointUrl, nameof(soap.EndpointUrl), errors);
                break;
            case FtpConnectorConfig ftp:
                RequireNonEmpty(ftp.Host, nameof(ftp.Host), errors);
                RequireNonEmpty(ftp.Username, nameof(ftp.Username), errors);
                break;
            case SftpConnectorConfig sftp:
                RequireNonEmpty(sftp.Host, nameof(sftp.Host), errors);
                RequireNonEmpty(sftp.Username, nameof(sftp.Username), errors);
                if (string.IsNullOrWhiteSpace(sftp.Password) && string.IsNullOrWhiteSpace(sftp.PrivateKeyContent))
                    errors.Add("Informe Password ou PrivateKeyContent para autenticação SFTP.");
                break;
            case DatabaseConnectorConfig db:
                RequireNonEmpty(db.ConnectionString, nameof(db.ConnectionString), errors);
                if (db.Provider is not ("Postgres" or "SqlServer"))
                    errors.Add("Provider deve ser 'Postgres' ou 'SqlServer'.");
                break;
            case QueueConnectorConfig queue:
                RequireNonEmpty(queue.HostName, nameof(queue.HostName), errors);
                break;
            case FileConnectorConfig file:
                RequireNonEmpty(file.RootDirectory, nameof(file.RootDirectory), errors);
                if (file.FileFormat is not ("Csv" or "Json" or "Xml" or "Excel"))
                    errors.Add("FileFormat deve ser 'Csv', 'Json', 'Xml' ou 'Excel'.");
                break;
            case EmailConnectorConfig email:
                RequireNonEmpty(email.ImapHost, nameof(email.ImapHost), errors);
                RequireNonEmpty(email.SmtpHost, nameof(email.SmtpHost), errors);
                RequireNonEmpty(email.Username, nameof(email.Username), errors);
                break;
            case GraphQlConnectorConfig graphQl:
                RequireNonEmpty(graphQl.EndpointUrl, nameof(graphQl.EndpointUrl), errors);
                break;
            case GrpcConnectorConfig grpc:
                RequireNonEmpty(grpc.Address, nameof(grpc.Address), errors);
                break;
            case LiteDbConnectorConfig liteDb:
                RequireNonEmpty(liteDb.DatabasePath, nameof(liteDb.DatabasePath), errors);
                break;
        }

        return errors.Count == 0;
    }

    private static void RequireNonEmpty(string? value, string fieldName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"Campo obrigatório ausente: {fieldName}.");
    }
}
