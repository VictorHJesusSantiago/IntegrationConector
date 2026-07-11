using System.Text;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using IntegrationConnector.Api.Jobs;
using IntegrationConnector.Api.Security;
using IntegrationConnector.Connectors.Abstractions;
using IntegrationConnector.Connectors.Database;
using IntegrationConnector.Connectors.Email;
using IntegrationConnector.Connectors.Files;
using IntegrationConnector.Connectors.Ftp;
using IntegrationConnector.Connectors.GraphQl;
using IntegrationConnector.Connectors.Grpc;
using IntegrationConnector.Connectors.LiteDb;
using IntegrationConnector.Connectors.Queue;
using IntegrationConnector.Connectors.Rest;
using IntegrationConnector.Connectors.Sftp;
using IntegrationConnector.Connectors.Soap;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Enums;
using IntegrationConnector.Core.Interfaces;
using IntegrationConnector.Engine;
using IntegrationConnector.Infrastructure.Data;
using IntegrationConnector.Infrastructure.Repositories;
using IntegrationConnector.Transformation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging (Serilog, com mascaramento de PII em console) ----------
builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration)
          .WriteTo.Console(new MaskingTextFormatter());
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=integrationconnector;Username=postgres;Password=postgres";

// ---------- Persistência ----------
builder.Services.AddDbContext<IntegrationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IConnectorRepository, ConnectorRepository>();
builder.Services.AddScoped<IPipelineRepository, PipelineRepository>();
builder.Services.AddScoped<IPipelineRunRepository, PipelineRunRepository>();
builder.Services.AddScoped<IDeadLetterRepository, DeadLetterRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IConnectorHealthRepository, ConnectorHealthRepository>();
builder.Services.AddScoped<IPipelineAlertRuleRepository, PipelineAlertRuleRepository>();
builder.Services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

// ---------- Conectores plugáveis ----------
builder.Services.AddHttpClient("rest-connector");
builder.Services.AddHttpClient("soap-connector");
builder.Services.AddHttpClient("graphql-connector");
builder.Services.AddHttpClient("oauth2-token");
builder.Services.AddSingleton<IConnectorPlugin, RestConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPlugin, SoapConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPlugin, FtpConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPlugin, DatabaseConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPlugin, QueueConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPlugin, FileConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPlugin, SftpConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPlugin, EmailConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPlugin, GraphQlConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPlugin, GrpcConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPlugin, LiteDbConnectorPlugin>();
builder.Services.AddSingleton<IConnectorPluginFactory, ConnectorPluginFactory>();

// ---------- Segurança: criptografia de segredos de conector (Data Protection local) ----------
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ISecretProtector, SecretProtector>();

// ---------- Transformação e engine de execução ----------
builder.Services.AddSingleton<IDataTransformer, DataTransformer>();
builder.Services.AddScoped<IPipelineExecutor, PipelineExecutor>();
builder.Services.AddScoped<PipelineJob>();
builder.Services.AddScoped<DeadLetterReprocessJob>();
builder.Services.AddScoped<ConnectorHealthCheckJob>();
builder.Services.AddScoped<PipelineAlertCheckJob>();
builder.Services.AddScoped<RetentionPurgeJob>();
builder.Services.AddSingleton<IPipelineSchedulerService, PipelineSchedulerService>();
builder.Services.AddScoped<IPipelineChainTrigger, HangfireChainTrigger>();
builder.Services.AddSingleton<IPipelineRunCancellationRegistry, PipelineRunCancellationRegistry>();

// ---------- Agendamento, retry e monitoramento (Hangfire) ----------
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));

builder.Services.AddHangfireServer(options => options.WorkerCount = Environment.ProcessorCount * 2);

// ---------- Autenticação/Autorização (JWT local, sem IdP externo) ----------
builder.Services.AddSingleton<JwtTokenService>();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"]!))
    };
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ---------- Rate limiting (nativo do ASP.NET Core, sem serviço externo) ----------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---------- API ----------
builder.Services.AddControllers(options =>
{
    options.Filters.Add(new AuthorizeFilter());
}).AddNewtonsoftJson();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Plataforma de Integração / iPaaS Leve",
        Version = "v1",
        Description = "Conectores plugáveis (REST, SOAP, FTP, banco, fila, arquivo, SFTP, e-mail, GraphQL, gRPC, LiteDB), " +
                      "transformação de dados, agendamento, retry, circuit breaker, dead-letter, idempotência, " +
                      "encadeamento, versionamento e monitoramento de falhas."
    });

    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Informe: Bearer {seu token JWT}",
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres")
    .AddRabbitMQ(rabbitConnectionString: $"amqp://{builder.Configuration["RabbitMq:Username"] ?? "guest"}:{builder.Configuration["RabbitMq:Password"] ?? "guest"}@{builder.Configuration["RabbitMq:Host"] ?? "localhost"}:{builder.Configuration["RabbitMq:Port"] ?? "5672"}", name: "rabbitmq");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// ---------- Migrações automáticas e seed do usuário admin padrão ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
    db.Database.Migrate();

    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var adminSection = app.Configuration.GetSection("DefaultAdmin");
    var adminUsername = adminSection["Username"] ?? "admin";

    if (await userRepository.GetByUsernameAsync(adminUsername) is null)
    {
        var admin = new User { Username = adminUsername, Role = UserRole.Admin };
        var hasher = new PasswordHasher<User>();
        admin.PasswordHash = hasher.HashPassword(admin, adminSection["Password"] ?? "Admin@12345");
        await userRepository.AddAsync(admin);
        await userRepository.SaveChangesAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Integration Connector API v1"));
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseRateLimiter();

app.UseHttpMetrics();

app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Painel de Monitoramento - Pipelines",
    Authorization = new[] { new HangfireDashboardAuthFilter() }
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapMetrics("/metrics").AllowAnonymous();

// ---------- Re-sincroniza agendamentos existentes e agenda jobs recorrentes de observabilidade ----------
using (var scope = app.Services.CreateScope())
{
    var pipelineRepository = scope.ServiceProvider.GetRequiredService<IPipelineRepository>();
    var scheduler = scope.ServiceProvider.GetRequiredService<IPipelineSchedulerService>();
    var pipelines = await pipelineRepository.GetAllAsync();
    foreach (var pipeline in pipelines)
        scheduler.Sync(pipeline);
}

RecurringJob.AddOrUpdate<ConnectorHealthCheckJob>("connector-health-check", job => job.RunAsync(CancellationToken.None), "*/15 * * * *");
RecurringJob.AddOrUpdate<PipelineAlertCheckJob>("pipeline-alert-check", job => job.RunAsync(CancellationToken.None), "*/5 * * * *");
RecurringJob.AddOrUpdate<RetentionPurgeJob>("retention-purge", job => job.RunAsync(CancellationToken.None), Cron.Daily);

app.Run();

public partial class Program { }
