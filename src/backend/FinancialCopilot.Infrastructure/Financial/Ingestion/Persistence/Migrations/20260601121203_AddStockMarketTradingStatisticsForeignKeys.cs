using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMarketTradingStatisticsForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LatestMarketQuotes_TradingInstrumentId",
                table: "LatestMarketQuotes",
                column: "TradingInstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyIndexSnapshots_TradingInstrumentId",
                table: "DailyIndexSnapshots",
                column: "TradingInstrumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_DailyIndexSnapshots_TradingInstruments_TradingInstrumentId",
                table: "DailyIndexSnapshots",
                column: "TradingInstrumentId",
                principalTable: "TradingInstruments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DailyInstrumentTrades_TradingInstruments_TradingInstrumentId",
                table: "DailyInstrumentTrades",
                column: "TradingInstrumentId",
                principalTable: "TradingInstruments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_IntradayIndexSnapshots_TradingInstruments_TradingInstrument~",
                table: "IntradayIndexSnapshots",
                column: "TradingInstrumentId",
                principalTable: "TradingInstruments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_IntradayTradeSnapshots_TradingInstruments_TradingInstrument~",
                table: "IntradayTradeSnapshots",
                column: "TradingInstrumentId",
                principalTable: "TradingInstruments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LatestMarketQuotes_TradingInstruments_TradingInstrumentId",
                table: "LatestMarketQuotes",
                column: "TradingInstrumentId",
                principalTable: "TradingInstruments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TradingInstruments_Companies_NormalizedCompanyId",
                table: "TradingInstruments",
                column: "NormalizedCompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyIndexSnapshots_TradingInstruments_TradingInstrumentId",
                table: "DailyIndexSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_DailyInstrumentTrades_TradingInstruments_TradingInstrumentId",
                table: "DailyInstrumentTrades");

            migrationBuilder.DropForeignKey(
                name: "FK_IntradayIndexSnapshots_TradingInstruments_TradingInstrument~",
                table: "IntradayIndexSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_IntradayTradeSnapshots_TradingInstruments_TradingInstrument~",
                table: "IntradayTradeSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_LatestMarketQuotes_TradingInstruments_TradingInstrumentId",
                table: "LatestMarketQuotes");

            migrationBuilder.DropForeignKey(
                name: "FK_TradingInstruments_Companies_NormalizedCompanyId",
                table: "TradingInstruments");

            migrationBuilder.DropIndex(
                name: "IX_LatestMarketQuotes_TradingInstrumentId",
                table: "LatestMarketQuotes");

            migrationBuilder.DropIndex(
                name: "IX_DailyIndexSnapshots_TradingInstrumentId",
                table: "DailyIndexSnapshots");
        }
    }
}
