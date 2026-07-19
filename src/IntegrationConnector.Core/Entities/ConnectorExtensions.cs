using IntegrationConnector.Core.Interfaces;

namespace IntegrationConnector.Core.Entities;

public static class ConnectorExtensions
{
    /// <summary>
    /// Retorna uma cópia *não rastreada* do conector com o ConfigurationJson decifrado, para uso
    /// pelos plugins. Nunca decifre em cima da instância original vinda de um repositório: como ela
    /// permanece rastreada pelo mesmo DbContext (mesmo escopo), qualquer SaveChanges subsequente
    /// (mesmo que para persistir outra entidade, como um PipelineRun) gravaria o segredo em texto
    /// plano de volta no banco, sobrescrevendo a versão criptografada permanentemente.
    /// </summary>
    public static Connector WithDecryptedConfig(this Connector connector, ISecretProtector secretProtector) => new()
    {
        Id = connector.Id,
        Name = connector.Name,
        Description = connector.Description,
        Type = connector.Type,
        ConfigurationJson = secretProtector.Unprotect(connector.ConfigurationJson),
        IsActive = connector.IsActive,
        CreatedAt = connector.CreatedAt,
        UpdatedAt = connector.UpdatedAt
    };
}
