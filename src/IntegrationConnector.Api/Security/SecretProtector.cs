using IntegrationConnector.Core.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Newtonsoft.Json.Linq;

namespace IntegrationConnector.Api.Security;

/// <summary>
/// Implementação de <see cref="ISecretProtector"/> usando o Data Protection nativo do ASP.NET Core
/// (chaves gerenciadas localmente, sem cofre externo). Percorre o JSON de configuração e cifra/decifra
/// apenas as propriedades cujo nome é conhecido como sensível.
/// </summary>
public class SecretProtector : ISecretProtector
{
    private const string ProtectedPrefix = "protected:";
    private const string RedactedMarker = "***";

    private static readonly HashSet<string> SensitiveFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "BearerToken", "ApiKeyValue", "OAuthClientSecret", "PrivateKeyContent",
        "PrivateKeyPassphrase", "ConnectionString"
    };

    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("IntegrationConnector.ConnectorSecrets.v1");
    }

    public string Protect(string configurationJson) => Transform(configurationJson, isProtect: true);

    public string Unprotect(string configurationJson) => Transform(configurationJson, isProtect: false);

    public string Redact(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson)) return configurationJson;

        JObject root;
        try { root = JObject.Parse(configurationJson); }
        catch { return "{}"; } // JSON inválido nunca deve ser ecoado cru para o cliente.

        foreach (var property in root.Properties())
        {
            if (!SensitiveFieldNames.Contains(property.Name)) continue;
            if (property.Value.Type != JTokenType.String) continue;
            if (string.IsNullOrEmpty(property.Value.Value<string>())) continue;

            property.Value = RedactedMarker;
        }

        return root.ToString(Newtonsoft.Json.Formatting.None);
    }

    public string MergeRedactedSecrets(string incomingJson, string existingProtectedJson)
    {
        if (string.IsNullOrWhiteSpace(incomingJson)) return incomingJson;

        JObject incoming;
        try { incoming = JObject.Parse(incomingJson); }
        catch { return incomingJson; }

        JObject existing;
        try { existing = JObject.Parse(string.IsNullOrWhiteSpace(existingProtectedJson) ? "{}" : existingProtectedJson); }
        catch { existing = new JObject(); }

        foreach (var property in incoming.Properties())
        {
            if (!SensitiveFieldNames.Contains(property.Name)) continue;
            if (property.Value.Type != JTokenType.String) continue;
            if (property.Value.Value<string>() != RedactedMarker) continue;

            // O cliente devolveu o marcador: mantém o valor já persistido (cifrado) intacto.
            // Se não havia valor anterior, o campo simplesmente sai da configuração.
            var existingValue = existing[property.Name];
            if (existingValue is not null)
                property.Value = existingValue.DeepClone();
            else
                property.Value = string.Empty;
        }

        return incoming.ToString(Newtonsoft.Json.Formatting.None);
    }

    private string Transform(string configurationJson, bool isProtect)
    {
        if (string.IsNullOrWhiteSpace(configurationJson)) return configurationJson;

        JObject root;
        try { root = JObject.Parse(configurationJson); }
        catch { return configurationJson; }

        foreach (var property in root.Properties())
        {
            if (!SensitiveFieldNames.Contains(property.Name)) continue;
            var value = property.Value.Type == JTokenType.String ? property.Value.Value<string>() : null;
            if (string.IsNullOrEmpty(value)) continue;

            if (isProtect)
            {
                if (value.StartsWith(ProtectedPrefix, StringComparison.Ordinal)) continue;
                property.Value = ProtectedPrefix + _protector.Protect(value);
            }
            else
            {
                if (!value.StartsWith(ProtectedPrefix, StringComparison.Ordinal)) continue;
                property.Value = _protector.Unprotect(value[ProtectedPrefix.Length..]);
            }
        }

        return root.ToString(Newtonsoft.Json.Formatting.None);
    }
}
