using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature126LeaseFencingMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentRunId",
                table: "IndustryRelativeValuationSourceLeases",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersededRunId",
                table: "IndustryRelativeValuationSourceLeases",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentRunId",
                table: "IndustryRelativeValuationSourceLeases");

            migrationBuilder.DropColumn(
                name: "SupersededRunId",
                table: "IndustryRelativeValuationSourceLeases");
        }
    }
}
