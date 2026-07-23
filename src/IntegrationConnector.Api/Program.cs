using System.Text;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using IntegrationConnector.Api.Jobs;
using IntegrationConnector.Api.Middleware;
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
using Microsoft.AspNetCore.DataProtection;
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
// As chaves precisam ser persistidas fora do filesystem efêmero do container: sem isso, todo
// segredo de conector já cifrado fica permanentemente indecifrável após qualquer restart/redeploy.
var keysDirectory = builder.Configuration["DataProtection:KeysDirectory"] ?? "/keys";
Directory.CreateDirectory(keysDirectory);
builder.Services.AddDataProtection()
    .SetApplicationName("IntegrationConnector")
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
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

// Uma SigningKey ausente, curta ou ainda com o valor placeholder permitiria a QUALQUER pessoa forjar
// tokens HS256 válidos — comprometimento total da autenticação. Fora de Development a aplicação se
// recusa a iniciar; em Development gera-se uma chave aleatória efêmera para que `docker compose up` e
// `dotnet run` continuem funcionando sem configuração, mas sem nenhuma chave fraca conhecida existir.
// Uma chave HS256 forte precisa de >= 32 bytes.
const string PlaceholderSigningKey = "CHANGE_ME_TO_A_LONG_RANDOM_SECRET_AT_LEAST_32_CHARS";
var signingKey = jwtSection["SigningKey"];

if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32 || signingKey == PlaceholderSigningKey)
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey ausente, curta demais (< 32 caracteres) ou ainda com o valor placeholder. " +
            "Defina uma chave aleatória forte via a variável de ambiente Jwt__SigningKey antes de iniciar a aplicação.");
    }

    signingKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
    Console.WriteLine(
        "[AVISO] Jwt:SigningKey não configurada. Uma chave aleatória EFÊMERA foi gerada para este " +
        "processo (somente Development): os tokens emitidos deixam de valer a cada reinício.");
}

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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
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

    // Limite dedicado (bem mais estrito) para o login: o teto global de 200/min é frouxo demais para
    // conter força bruta de senha. 10 tentativas por minuto por IP tornam ataque online inviável sem
    // penalizar o uso legítimo.
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---------- API ----------
builder.Services.AddControllers(options =>
{
    options.Filters.Add(new AuthorizeFilter());
}).AddNewtonsoftJson(options =>
{
    // Entidades como Pipeline <-> PipelineVersion e PipelineRun <-> PipelineRunLog têm referências
    // de navegação bidirecionais (EF Core); sem isso, a serialização quebra com StackOverflow/loop.
    options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
});

// ---------- Erros padronizados (RFC 7807 Problem Details) ----------
// Todo erro da API — de validação, de status code sem corpo, ou exceção não tratada — sai no mesmo
// formato, sempre com traceId para correlacionar com os logs e traces.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});

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

// ---------- CORS ----------
// Allowlist explícita via configuração (Cors:AllowedOrigins). O dashboard embutido é servido pela
// própria API (mesma origem), portanto não precisa de CORS: a política só existe para clientes
// externos declarados. Sem origens configuradas, nenhuma origem cruzada é liberada — "AllowAnyOrigin"
// como padrão transformava qualquer site em cliente da API.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            // Nenhuma origem cruzada permitida (mesma origem continua funcionando normalmente).
            policy.WithOrigins(Array.Empty<string>());
            return;
        }

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
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
        // Mesma classe de problema da chave JWT: uma senha de admin padrão e pública equivale a não
        // ter autenticação. Fora de Development exigimos uma senha explícita e não-trivial; em
        // Development geramos uma aleatória e a imprimimos uma única vez, no boot.
        var adminPassword = adminSection["Password"] ?? string.Empty;
        var isWeakAdminPassword = string.IsNullOrWhiteSpace(adminPassword)
            || adminPassword.Length < 12
            || adminPassword == "Admin@12345";

        if (isWeakAdminPassword)
        {
            if (!app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "DefaultAdmin:Password ausente, curta demais (< 12 caracteres) ou ainda com o valor padrão. " +
                    "Defina uma senha forte via a variável de ambiente DefaultAdmin__Password antes de iniciar a aplicação.");
            }

            adminPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
            Console.WriteLine(
                $"[AVISO] DefaultAdmin:Password não configurada. Senha aleatória gerada para '{adminUsername}' " +
                $"(somente Development, exibida apenas agora): {adminPassword}");
        }

        var admin = new User { Username = adminUsername, Role = UserRole.Admin };
        var hasher = new PasswordHasher<User>();
        admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
        await userRepository.AddAsync(admin);
        await userRepository.SaveChangesAsync();
    }
}

// Converte exceções não tratadas em ProblemDetails (RFC 7807) em vez de vazar stack trace, e
// preenche o corpo de respostas de status "vazias" (401/403/404 sem payload) com o mesmo formato.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Integration Connector API v1"));
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseRateLimiter();

app.UseHttpMetrics();

// Arquivos estáticos (dashboard.html) e o painel do Hangfire (autorização própria via
// HangfireDashboardAuthFilter) ficam ANTES da autenticação JWT da API — do contrário, o
// FallbackPolicy global bloquearia essas requisições com 401 mesmo sem endpoint de controller.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Painel de Monitoramento - Pipelines",
    Authorization = new[] { new HangfireDashboardAuthFilter() }
});

app.UseAuthentication();
app.UseAuthorization();

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
