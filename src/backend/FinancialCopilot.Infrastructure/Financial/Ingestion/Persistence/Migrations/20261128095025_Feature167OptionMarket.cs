using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature167OptionMarket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OptionContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnderlyingInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    StrikePrice = table.Column<decimal>(type: "numeric", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BeginDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ContractSize = table.Column<int>(type: "integer", nullable: false),
                    OpenInterest = table.Column<long>(type: "bigint", nullable: false),
                    PreviousOpenInterest = table.Column<long>(type: "bigint", nullable: false),
                    OpenInterestObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptionContracts", x => x.Id);
                    table.CheckConstraint("CK_OptionContracts_Side", "\"Side\" IN ('Call', 'Put')");
                    table.CheckConstraint("CK_OptionContracts_Terms", "\"StrikePrice\" > 0 AND \"ContractSize\" > 0 AND \"ExpiryDate\" >= \"BeginDate\"");
                    table.ForeignKey(
                        name: "FK_OptionContracts_TradingInstruments_TradingInstrumentId",
                        column: x => x.TradingInstrumentId,
                        principalTable: "TradingInstruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OptionContracts_TradingInstruments_UnderlyingInstrumentId",
                        column: x => x.UnderlyingInstrumentId,
                        principalTable: "TradingInstruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OptionOrderBookLevels",
                columns: table => new
                {
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<short>(type: "smallint", nullable: false),
                    BidPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    BidVolume = table.Column<long>(type: "bigint", nullable: true),
                    BidOrderCount = table.Column<int>(type: "integer", nullable: true),
                    AskPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    AskVolume = table.Column<long>(type: "bigint", nullable: true),
                    AskOrderCount = table.Column<int>(type: "integer", nullable: true),
                    ProviderTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptionOrderBookLevels", x => new { x.TradingInstrumentId, x.Level });
                    table.CheckConstraint("CK_OptionOrderBookLevels_Level", "\"Level\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_OptionOrderBookLevels_TradingInstruments_TradingInstrumentId",
                        column: x => x.TradingInstrumentId,
                        principalTable: "TradingInstruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OptionContracts_TradingInstrumentId",
                table: "OptionContracts",
                column: "TradingInstrumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptionContracts_UnderlyingInstrumentId_ExpiryDate_StrikePri~",
                table: "OptionContracts",
                columns: new[] { "UnderlyingInstrumentId", "ExpiryDate", "StrikePrice" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OptionContracts");

            migrationBuilder.DropTable(
                name: "OptionOrderBookLevels");
        }
    }
}
