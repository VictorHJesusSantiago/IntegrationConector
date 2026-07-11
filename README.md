# Plataforma de Integração / iPaaS Leve

Plataforma de integração (iPaaS leve) para conectar sistemas legados e modernos via **conectores plugáveis**
(REST, SOAP, FTP, SFTP, Banco de Dados, Fila de Mensagens, Arquivo, E-mail, GraphQL, gRPC, LiteDB), com
**transformação de dados** avançada, **agendamento**, **retry/circuit breaker/timeout**, **dead-letter e
reprocessamento**, **versionamento e governança de fluxo**, **segurança (JWT/roles/auditoria)** e
**observabilidade completa** (dashboard, alertas, métricas Prometheus, saúde de conectores).

## Arquitetura

Solução .NET 8 organizada em camadas, cada uma um projeto independente:

| Projeto | Responsabilidade |
|---|---|
| `IntegrationConnector.Core` | Entidades de domínio, enums, DTOs e contratos (interfaces) — não depende de nada. |
| `IntegrationConnector.Connectors` | Conectores plugáveis: REST, SOAP, FTP, SFTP, Banco (Postgres/SqlServer), Fila (RabbitMQ), Arquivo (CSV/JSON/XML/Excel), E-mail (IMAP/SMTP), GraphQL, gRPC (simplificado) e LiteDB (NoSQL embarcado). |
| `IntegrationConnector.Transformation` | Motor de transformação (mapeamento via JSONPath + 14 funções), agregações, validação de payload por JSON Schema e conversão de formatos (JSON/CSV/XML/Excel). |
| `IntegrationConnector.Engine` | `PipelineExecutor`: orquestra leitura → join secundário → transformação → agregação → validação → escrita (sequencial ou paralela), com retry, circuit breaker, timeout, dead-letter, idempotência e encadeamento. |
| `IntegrationConnector.Infrastructure` | EF Core (Postgres), repositórios, migrations. |
| `IntegrationConnector.Api` | Web API (ASP.NET Core), Hangfire, JWT, rate limiting, Prometheus, dashboard HTML, Swagger. |
| `IntegrationConnector.Tests` | Testes unitários (xUnit + Moq) do motor de transformação e do executor de pipelines. |

### Conceitos principais

- **Connector**: configuração de um sistema externo, com segredos (senha, token, connection string, chave
  privada) **criptografados em repouso** via Data Protection nativo do ASP.NET Core e validados contra um
  schema mínimo por tipo antes de salvar.
- **Pipeline**: fluxo de integração com origem, destino, origem secundária opcional (join), mapeamentos,
  agregações, política de retry/circuit breaker/timeout, paralelismo de escrita, idempotência e encadeamento
  (`NextPipelineId`) para outro pipeline. Gatilhos: `Manual`, `Cron`, `Interval` ou `Webhook`.
- **PipelineVersion**: cada publicação gera uma versão imutável, com workflow de governança
  (`Draft` → `InReview` → `Published`), diff entre versões, rollback, clonagem e export/import como bundle JSON.
- **PipelineRun** / **PipelineRunLog**: histórico de execuções (inclui dry-run e cancelamento cooperativo),
  exportável em JSON/CSV.
- **DeadLetterRecord**: registros que falharam na validação/escrita, disponíveis para reprocessamento
  individual ou em lote sem repetir a leitura da origem.

## Como executar (Docker Compose)

```bash
docker compose up --build
```

Sobe Postgres, RabbitMQ e a API (porta `8080`). Migrations do EF Core são aplicadas automaticamente na
inicialização, e um usuário administrador padrão é criado (`DefaultAdmin` em `appsettings.json`).

- Swagger: http://localhost:8080/swagger
- Dashboard de observabilidade: http://localhost:8080/dashboard.html
- Painel do Hangfire: http://localhost:8080/hangfire
- Métricas Prometheus: http://localhost:8080/metrics
- Health check (Postgres + RabbitMQ): http://localhost:8080/health
- RabbitMQ management: http://localhost:15672 (guest/guest)

**Antes de ir para produção**: troque `Jwt:SigningKey` e `DefaultAdmin:Password` em `appsettings.json` (ou via
variáveis de ambiente `Jwt__SigningKey` / `DefaultAdmin__Password`).

## Como executar localmente (sem Docker)

```bash
dotnet restore
dotnet run --project src/IntegrationConnector.Api
```

## Autenticação

A API exige um JWT válido em todos os endpoints, exceto `POST /api/auth/login` e `POST /api/webhooks/...`
(estes usam um token próprio por pipeline). Fluxo:

```bash
curl -X POST http://localhost:8080/api/auth/login -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin@12345"}'
```

Use o `token` retornado no cabeçalho `Authorization: Bearer {token}`. Papéis (`Viewer`, `Operator`, `Admin`)
controlam o acesso; crie novos usuários via `POST /api/auth/users` (somente `Admin`).

## Fluxo de uso típico

1. **Criar conectores** (`POST /api/connectors`) — validados contra o schema do tipo e com segredos cifrados.
2. **Testar conectividade** (`POST /api/connectors/{id}/test`).
3. **Criar um pipeline** (`POST /api/pipelines`), ou começar de um modelo pronto (`GET /api/pipeline-templates`).
4. **Executar manualmente** (`POST /api/pipelines/{id}/run`), em **dry-run** (`POST /api/pipelines/{id}/dry-run`)
   ou aguardar o agendamento/webhook.
5. **Monitorar**: `GET /api/pipeline-runs/search`, `/stats`, `/failures`, cancelar com
   `POST /api/pipeline-runs/{id}/cancel`, exportar logs com `GET /api/pipeline-runs/{id}/logs/export`.
6. **Reprocessar falhas**: `GET /api/dead-letters/by-run/{runId}` e `POST /api/dead-letters/{id}/reprocess`
   (ou em lote com `.../reprocess-all`).
7. **Publicar nova versão** (`POST /api/pipelines/{id}/versions`), comparar (`GET .../versions/{v1}/diff/{v2}`),
   aprovar (`POST .../versions/approve`) ou reverter (`POST .../versions/{n}/activate`).
8. **Exportar/clonar** um pipeline entre ambientes: `GET /api/pipelines/{id}/export`,
   `POST /api/pipelines/import`, `POST /api/pipelines/{id}/clone`.

## Recursos de execução avançados

- **Retry + circuit breaker + timeout**: configuráveis por pipeline em `RetryPolicySpec`.
- **Paralelismo controlado**: `ExecutionOptions.MaxDegreeOfParallelism` grava registros individualmente com
  N workers simultâneos (acima de 1) ou em lote único (padrão, compatível com upsert em banco).
- **Idempotência**: `PipelineDefinition.IdempotencyKeyPath` evita gravação duplicada em reprocessamentos.
- **Join com origem secundária**: `SecondarySource` combina dois conectores de leitura antes do mapeamento.
- **Agregações**: `Aggregations` calcula soma/contagem/média/mín/máx sobre arrays de origem.
- **Validação por JSON Schema**: `TargetJsonSchema` rejeita registros inválidos para dead-letter sem
  interromper o lote.
- **Encadeamento**: `PUT /api/pipelines/{id}/chain` dispara outro pipeline após o sucesso do atual.
- **Webhook de entrada**: `POST /api/webhooks/{pipelineId}/{token}` usa o corpo da requisição como origem,
  pulando a leitura do conector configurado.

## Observabilidade

- Dashboard HTML (`/dashboard.html`), painel Hangfire, métricas Prometheus (`/metrics`).
- Alertas por e-mail (SMTP local, seção `Smtp` em `appsettings.json`) configurados por
  `POST /api/pipeline-alert-rules` (N falhas consecutivas).
- Checagem periódica de saúde de conectores (job a cada 15 min) via `GET /api/connectors/health`.
- Retenção configurável (`Retention:PipelineRunRetentionDays`) purga execuções antigas automaticamente.
- Logs mascaram CPF e e-mail antes de irem para o console (evita vazamento de PII).
- Auditoria (`AuditLogEntry`) registra criação/edição/remoção/publicação/execução manual.

## Testes

```bash
dotnet test
```

## Adicionando um novo tipo de conector

1. Implemente `IConnectorPlugin` em `IntegrationConnector.Connectors`.
2. Registre a implementação no DI (`Program.cs`): `builder.Services.AddSingleton<IConnectorPlugin, MeuNovoConectorPlugin>();`.
3. Adicione o novo valor em `ConnectorType` (`IntegrationConnector.Core.Enums`) e, se necessário, valide sua
   configuração em `ConnectorConfigValidator`.

Nenhuma mudança é necessária no `PipelineExecutor` — a resolução do plugin é feita via `IConnectorPluginFactory`
(padrão Strategy/Factory).
