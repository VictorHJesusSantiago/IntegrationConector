using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Core.Entities;

/// <summary>Usuário da plataforma, autenticado via JWT emitido localmente (sem IdP externo).</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Viewer;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
