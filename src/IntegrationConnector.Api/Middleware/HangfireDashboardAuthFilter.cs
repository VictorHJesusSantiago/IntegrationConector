using Hangfire.Dashboard;

/// <summary>
/// Filtro de acesso ao painel do Hangfire. Em desenvolvimento libera acesso local;
/// em produção deve ser combinado com autenticação (ex.: reverse proxy com Basic/OIDC).
/// </summary>
public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.Request.Host.Host is "localhost" or "127.0.0.1"
            || httpContext.User.Identity?.IsAuthenticated == true;
    }
}
