using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IntegrationConnector.Infrastructure.Data;

/// <summary>Permite ao `dotnet ef` criar migrations sem precisar subir a API completa.</summary>
public class IntegrationDbContextFactory : IDesignTimeDbContextFactory<IntegrationDbContext>
{
    public IntegrationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("INTEGRATIONCONNECTOR_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=integrationconnector;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<IntegrationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new IntegrationDbContext(optionsBuilder.Options);
    }
}
