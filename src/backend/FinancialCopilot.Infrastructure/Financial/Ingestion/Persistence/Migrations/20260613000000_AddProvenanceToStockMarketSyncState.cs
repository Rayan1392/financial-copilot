using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProvenanceToStockMarketSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogicalVendor",
                table: "StockMarketSyncStates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhysicalSource",
                table: "StockMarketSyncStates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceMode",
                table: "StockMarketSyncStates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogicalVendor",
                table: "StockMarketSyncStates");

            migrationBuilder.DropColumn(
                name: "PhysicalSource",
                table: "StockMarketSyncStates");

            migrationBuilder.DropColumn(
                name: "SourceMode",
                table: "StockMarketSyncStates");
        }
    }
}
