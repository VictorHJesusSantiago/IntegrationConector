using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IntegrationConnector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtendedFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NextPipelineId",
                table: "pipelines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookToken",
                table: "pipelines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "pipeline_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "pipeline_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "pipeline_versions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDryRun",
                table: "pipeline_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "audit_log_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "connector_health_checks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_health_checks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dead_letter_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordJson = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reprocessed = table.Column<bool>(type: "boolean", nullable: false),
                    ReprocessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letter_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyHash = table.Column<string>(type: "text", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_alert_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsecutiveFailuresThreshold = table.Column<int>(type: "integer", nullable: false),
                    NotifyEmail = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_alert_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_entries_Timestamp",
                table: "audit_log_entries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_connector_health_checks_ConnectorId_CheckedAt",
                table: "connector_health_checks",
                columns: new[] { "ConnectorId", "CheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_dead_letter_records_PipelineId",
                table: "dead_letter_records",
                column: "PipelineId");

            migrationBuilder.CreateIndex(
                name: "IX_dead_letter_records_PipelineRunId",
                table: "dead_letter_records",
                column: "PipelineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_PipelineId_KeyHash",
                table: "idempotency_records",
                columns: new[] { "PipelineId", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_alert_rules_PipelineId",
                table: "pipeline_alert_rules",
                column: "PipelineId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log_entries");

            migrationBuilder.DropTable(
                name: "connector_health_checks");

            migrationBuilder.DropTable(
                name: "dead_letter_records");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "pipeline_alert_rules");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropColumn(
                name: "NextPipelineId",
                table: "pipelines");

            migrationBuilder.DropColumn(
                name: "WebhookToken",
                table: "pipelines");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "pipeline_versions");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "pipeline_versions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "pipeline_versions");

            migrationBuilder.DropColumn(
                name: "IsDryRun",
                table: "pipeline_runs");
        }
    }
}
