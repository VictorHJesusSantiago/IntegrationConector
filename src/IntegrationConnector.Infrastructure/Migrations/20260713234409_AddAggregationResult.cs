using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IntegrationConnector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAggregationResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AggregationResultJson",
                table: "pipeline_runs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AggregationResultJson",
                table: "pipeline_runs");
        }
    }
}
