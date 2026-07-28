using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketQuoteMismatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketQuoteMismatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComparedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BridgeValue = table.Column<decimal>(type: "numeric", nullable: false),
                    DirectValue = table.Column<decimal>(type: "numeric", nullable: false),
                    AbsoluteDiff = table.Column<decimal>(type: "numeric", nullable: false),
                    RelativeDiffPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    BridgeSourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DirectSourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketQuoteMismatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketQuoteMismatches_TradingInstruments_TradingInstrumentId",
                        column: x => x.TradingInstrumentId,
                        principalTable: "TradingInstruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketQuoteMismatches_ComparedAt",
                table: "MarketQuoteMismatches",
                column: "ComparedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MarketQuoteMismatches_TradingInstrumentId_ComparedAt",
                table: "MarketQuoteMismatches",
                columns: new[] { "TradingInstrumentId", "ComparedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MarketQuoteMismatches");
        }
    }
}
