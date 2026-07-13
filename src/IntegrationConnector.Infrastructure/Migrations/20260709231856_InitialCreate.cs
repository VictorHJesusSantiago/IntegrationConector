using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IntegrationConnector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "connectors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connectors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    CronExpression = table.Column<string>(type: "text", nullable: true),
                    IntervalSeconds = table.Column<int>(type: "integer", nullable: true),
                    ActiveVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipelines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    RecordsRead = table.Column<int>(type: "integer", nullable: false),
                    RecordsWritten = table.Column<int>(type: "integer", nullable: false),
                    RecordsFailed = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ErrorStackTrace = table.Column<string>(type: "text", nullable: true),
                    TriggerSource = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pipeline_runs_pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "pipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false),
                    ChangeNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pipeline_versions_pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "pipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_run_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false),
                    Step = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_run_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pipeline_run_logs_pipeline_runs_PipelineRunId",
                        column: x => x.PipelineRunId,
                        principalTable: "pipeline_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_connectors_Name",
                table: "connectors",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_run_logs_PipelineRunId",
                table: "pipeline_run_logs",
                column: "PipelineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_PipelineId",
                table: "pipeline_runs",
                column: "PipelineId");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_StartedAt",
                table: "pipeline_runs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_Status",
                table: "pipeline_runs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_versions_PipelineId_VersionNumber",
                table: "pipeline_versions",
                columns: new[] { "PipelineId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pipelines_Name",
                table: "pipelines",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connectors");

            migrationBuilder.DropTable(
                name: "pipeline_run_logs");

            migrationBuilder.DropTable(
                name: "pipeline_versions");

            migrationBuilder.DropTable(
                name: "pipeline_runs");

            migrationBuilder.DropTable(
                name: "pipelines");
        }
    }
}
