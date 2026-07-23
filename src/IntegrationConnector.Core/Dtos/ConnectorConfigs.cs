namespace IntegrationConnector.Core.Dtos;

/// <summary>Configuração de um conector REST, desserializada de Connector.ConfigurationJson.</summary>
public class RestConnectorConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string AuthType { get; set; } = "None"; // None | Basic | Bearer | ApiKey | OAuth2ClientCredentials
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? BearerToken { get; set; }
    public string? ApiKeyHeaderName { get; set; }
    public string? ApiKeyValue { get; set; }
    public int TimeoutSeconds { get; set; } = 30;

    // OAuth2 client-credentials
    public string? OAuthTokenEndpoint { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthClientSecret { get; set; }
    public string? OAuthScope { get; set; }
}

/// <summary>Configuração de um conector SOAP.</summary>
public class SoapConnectorConfig
{
    public string EndpointUrl { get; set; } = string.Empty;
    public string SoapAction { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Configuração de um conector FTP.</summary>
public class FtpConnectorConfig
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 21;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSftp { get; set; }
    public string RemoteDirectory { get; set; } = "/";
}

/// <summary>Configuração de um conector SFTP nativo (autenticação por senha e/ou chave privada).</summary>
public class SftpConnectorConfig
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }

    /// <summary>Conteúdo PEM da chave privada (OpenSSH/PuTTY/PEM), opcional se Password for usado.</summary>
    public string? PrivateKeyContent { get; set; }
    public string? PrivateKeyPassphrase { get; set; }
    public string RemoteDirectory { get; set; } = "/";
}

/// <summary>Configuração de um conector de banco de dados relacional.</summary>
public class DatabaseConnectorConfig
{
    /// <summary>"Postgres" ou "SqlServer".</summary>
    public string Provider { get; set; } = "Postgres";
    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 30;
}

/// <summary>Configuração de um conector de fila de mensagens (RabbitMQ).</summary>
public class QueueConnectorConfig
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public bool Durable { get; set; } = true;
}

/// <summary>Configuração de um conector de arquivo local/rede (CSV, JSON, XML, Excel).</summary>
public class FileConnectorConfig
{
    /// <summary>Diretório raiz (montado localmente ou compartilhamento de rede já mapeado) onde os arquivos residem.</summary>
    public string RootDirectory { get; set; } = ".";

    /// <summary>"Csv" | "Json" | "Xml" | "Excel".</summary>
    public string FileFormat { get; set; } = "Json";

    /// <summary>Delimitador usado quando FileFormat = Csv.</summary>
    public string CsvDelimiter { get; set; } = ",";
    public bool CsvHasHeader { get; set; } = true;

    /// <summary>Nome da planilha usada quando FileFormat = Excel.</summary>
    public string ExcelSheetName { get; set; } = "Sheet1";
}

/// <summary>Configuração de um conector de e-mail (IMAP para leitura, SMTP para envio).</summary>
public class EmailConnectorConfig
{
    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; } = 993;
    public bool ImapUseSsl { get; set; } = true;

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Pasta IMAP usada na leitura (padrão INBOX).</summary>
    public string MailboxFolder { get; set; } = "INBOX";

    /// <summary>Diretório local onde anexos lidos são salvos temporariamente.</summary>
    public string AttachmentDownloadDirectory { get; set; } = "./attachments";
}

/// <summary>Configuração de um conector GraphQL (variação do REST, usando POST com {query, variables}).</summary>
public class GraphQlConnectorConfig
{
    public string EndpointUrl { get; set; } = string.Empty;
    public string AuthType { get; set; } = "None"; // None | Bearer | ApiKey
    public string? BearerToken { get; set; }
    public string? ApiKeyHeaderName { get; set; }
    public string? ApiKeyValue { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Configuração de um conector gRPC simplificado: assume que o serviço alvo expõe métodos unários
/// que recebem e retornam uma única string JSON (marshaller customizado), evitando a necessidade
/// de compilar contratos .proto especificos para integração genérica.
/// </summary>
public class GrpcConnectorConfig
{
    public string Address { get; set; } = string.Empty; // ex.: https://localhost:5001
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Configuração de um conector de banco NoSQL embarcado (LiteDB), sem dependência de servidor externo.</summary>
public class LiteDbConnectorConfig
{
    /// <summary>Caminho do arquivo .db local.</summary>
    public string DatabasePath { get; set; } = "./data/connector.db";
    public string CollectionName { get; set; } = "documents";
}
