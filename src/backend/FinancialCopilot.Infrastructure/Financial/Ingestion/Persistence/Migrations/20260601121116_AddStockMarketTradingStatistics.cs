using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMarketTradingStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyIndexSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: true),
                    High = table.Column<decimal>(type: "numeric", nullable: true),
                    Low = table.Column<decimal>(type: "numeric", nullable: true),
                    ChangePercent = table.Column<decimal>(type: "numeric", nullable: true),
                    SourceKind = table.Column<string>(type: "text", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyIndexSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyInstrumentTrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalTradeId = table.Column<long>(type: "bigint", nullable: false),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosingPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    LastTradedPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    PriceChange = table.Column<decimal>(type: "numeric", nullable: false),
                    PriceYesterday = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalTransactions = table.Column<decimal>(type: "numeric", nullable: false),
                    Volume = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalCapital = table.Column<decimal>(type: "numeric", nullable: false),
                    MarketValue = table.Column<decimal>(type: "numeric", nullable: false),
                    SourceInsertedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyInstrumentTrades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntradayIndexSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TradingTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Value = table.Column<decimal>(type: "numeric", nullable: false),
                    ChangePercent = table.Column<decimal>(type: "numeric", nullable: true),
                    SourceChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntradayIndexSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntradayTradeSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TradingTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ClosingPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    LastTradedPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    PriceChange = table.Column<decimal>(type: "numeric", nullable: false),
                    PriceYesterday = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalTransactions = table.Column<decimal>(type: "numeric", nullable: false),
                    Volume = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalCapital = table.Column<decimal>(type: "numeric", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntradayTradeSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LatestMarketQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LatestPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    PriceChangePercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    SourceKind = table.Column<string>(type: "text", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LatestMarketQuotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockMarketSyncStates",
                columns: table => new
                {
                    Dataset = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Watermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMarketSyncStates", x => x.Dataset);
                });

            migrationBuilder.CreateTable(
                name: "TradingInstruments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentCode = table.Column<long>(type: "bigint", nullable: false),
                    InstrumentIsin = table.Column<string>(type: "text", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    InstrumentKind = table.Column<string>(type: "text", nullable: false),
                    NormalizedCompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SourceChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingInstruments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyIndexSnapshots_ProviderName_TradingInstrumentId_Tradin~",
                table: "DailyIndexSnapshots",
                columns: new[] { "ProviderName", "TradingInstrumentId", "TradingDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyInstrumentTrades_ProviderName_ExternalTradeId",
                table: "DailyInstrumentTrades",
                columns: new[] { "ProviderName", "ExternalTradeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyInstrumentTrades_TradingInstrumentId_TradingDate",
                table: "DailyInstrumentTrades",
                columns: new[] { "TradingInstrumentId", "TradingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_IntradayIndexSnapshots_ProviderName_ExternalSnapshotId",
                table: "IntradayIndexSnapshots",
                columns: new[] { "ProviderName", "ExternalSnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntradayIndexSnapshots_TradingInstrumentId_TradingDate",
                table: "IntradayIndexSnapshots",
                columns: new[] { "TradingInstrumentId", "TradingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_IntradayTradeSnapshots_ProviderName_ExternalSnapshotId",
                table: "IntradayTradeSnapshots",
                columns: new[] { "ProviderName", "ExternalSnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntradayTradeSnapshots_TradingInstrumentId_ReceivedAt",
                table: "IntradayTradeSnapshots",
                columns: new[] { "TradingInstrumentId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LatestMarketQuotes_ProviderName_TradingInstrumentId",
                table: "LatestMarketQuotes",
                columns: new[] { "ProviderName", "TradingInstrumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradingInstruments_NormalizedCompanyId",
                table: "TradingInstruments",
                column: "NormalizedCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TradingInstruments_ProviderName_ExternalInstrumentId",
                table: "TradingInstruments",
                columns: new[] { "ProviderName", "ExternalInstrumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradingInstruments_ProviderName_InstrumentCode",
                table: "TradingInstruments",
                columns: new[] { "ProviderName", "InstrumentCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyIndexSnapshots");

            migrationBuilder.DropTable(
                name: "DailyInstrumentTrades");

            migrationBuilder.DropTable(
                name: "IntradayIndexSnapshots");

            migrationBuilder.DropTable(
                name: "IntradayTradeSnapshots");

            migrationBuilder.DropTable(
                name: "LatestMarketQuotes");

            migrationBuilder.DropTable(
                name: "StockMarketSyncStates");

            migrationBuilder.DropTable(
                name: "TradingInstruments");
        }
    }
}
