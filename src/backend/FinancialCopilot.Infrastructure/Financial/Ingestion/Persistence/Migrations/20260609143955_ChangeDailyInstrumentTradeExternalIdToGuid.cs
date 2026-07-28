using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeDailyInstrumentTradeExternalIdToGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The daily-trade source moved from Tse.InstTrade (bigint Id) to Tse.TradeRefined
            // (uniqueidentifier Id), so ExternalTradeId becomes uuid. PostgreSQL cannot cast
            // bigint -> uuid, and there are no daily-trade rows yet (the previous source mapping
            // never persisted any), so the column is dropped and recreated rather than altered.
            // Guard first: refuse to run destructively if any rows somehow exist.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "DailyInstrumentTrades") THEN
                        RAISE EXCEPTION 'DailyInstrumentTrades is not empty; ExternalTradeId bigint->uuid change must be migrated with explicit data conversion.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_DailyInstrumentTrades_ProviderName_ExternalTradeId",
                table: "DailyInstrumentTrades");

            migrationBuilder.DropColumn(
                name: "ExternalTradeId",
                table: "DailyInstrumentTrades");

            migrationBuilder.AddColumn<System.Guid>(
                name: "ExternalTradeId",
                table: "DailyInstrumentTrades",
                type: "uuid",
                nullable: false,
                defaultValue: System.Guid.Empty);

            migrationBuilder.CreateIndex(
                name: "IX_DailyInstrumentTrades_ProviderName_ExternalTradeId",
                table: "DailyInstrumentTrades",
                columns: new[] { "ProviderName", "ExternalTradeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyInstrumentTrades_ProviderName_ExternalTradeId",
                table: "DailyInstrumentTrades");

            migrationBuilder.DropColumn(
                name: "ExternalTradeId",
                table: "DailyInstrumentTrades");

            migrationBuilder.AddColumn<long>(
                name: "ExternalTradeId",
                table: "DailyInstrumentTrades",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_DailyInstrumentTrades_ProviderName_ExternalTradeId",
                table: "DailyInstrumentTrades",
                columns: new[] { "ProviderName", "ExternalTradeId" },
                unique: true);
        }
    }
}
