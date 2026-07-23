namespace IntegrationConnector.Core.Interfaces;

/// <summary>
/// Criptografa/descriptografa campos sensíveis (senha, token, connection string, chave privada) dentro
/// do ConfigurationJson de um conector, usando Data Protection local do ASP.NET Core (sem cofre externo).
/// </summary>
public interface ISecretProtector
{
    /// <summary>Substitui os valores de campos sensíveis conhecidos por seu equivalente criptografado.</summary>
    string Protect(string configurationJson);

    /// <summary>Reverte <see cref="Protect"/>, decifrando os campos sensíveis para uso em tempo de execução.</summary>
    string Unprotect(string configurationJson);

    /// <summary>
    /// Substitui os valores de campos sensíveis por um marcador fixo, para exposição em APIs.
    /// Diferente de <see cref="Protect"/>, é irreversível e não vaza nem o texto cifrado — que ainda
    /// revela comprimento aproximado e permite comparar segredos entre conectores.
    /// </summary>
    string Redact(string configurationJson);

    /// <summary>
    /// Reconstrói o JSON de entrada preservando os segredos já armazenados onde o cliente devolveu o
    /// marcador de redação. Sem isso, o fluxo natural "GET → editar um campo → PUT" gravaria o
    /// literal "***" por cima do segredo real, inutilizando o conector de forma irreversível.
    /// </summary>
    /// <param name="incomingJson">Configuração enviada pelo cliente (pode conter marcadores de redação).</param>
    /// <param name="existingProtectedJson">Configuração atualmente persistida, com os segredos cifrados.</param>
    string MergeRedactedSecrets(string incomingJson, string existingProtectedJson);
}
