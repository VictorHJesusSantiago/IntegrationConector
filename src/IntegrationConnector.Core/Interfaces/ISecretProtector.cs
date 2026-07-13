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
}
