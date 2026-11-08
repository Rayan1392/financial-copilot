using System;
using System.Numerics;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature148GaugeEquilibrium : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CyclicalWavesV2SyncRuns_Scope",
                schema: "cyclicalwaves",
                table: "SyncRuns");

            migrationBuilder.CreateTable(
                name: "EquilibriumRanges",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    TickerId = table.Column<Guid>(type: "uuid", nullable: false),
                    HasRange = table.Column<bool>(type: "boolean", nullable: false),
                    MinHigh = table.Column<BigInteger>(type: "numeric", nullable: true),
                    MinMiddle = table.Column<BigInteger>(type: "numeric", nullable: true),
                    MinLow = table.Column<BigInteger>(type: "numeric", nullable: true),
                    MinSign = table.Column<short>(type: "smallint", nullable: true),
                    MinScale = table.Column<short>(type: "smallint", nullable: true),
                    AvgHigh = table.Column<BigInteger>(type: "numeric", nullable: true),
                    AvgMiddle = table.Column<BigInteger>(type: "numeric", nullable: true),
                    AvgLow = table.Column<BigInteger>(type: "numeric", nullable: true),
                    AvgSign = table.Column<short>(type: "smallint", nullable: true),
                    AvgScale = table.Column<short>(type: "smallint", nullable: true),
                    MaxHigh = table.Column<BigInteger>(type: "numeric", nullable: true),
                    MaxMiddle = table.Column<BigInteger>(type: "numeric", nullable: true),
                    MaxLow = table.Column<BigInteger>(type: "numeric", nullable: true),
                    MaxSign = table.Column<short>(type: "smallint", nullable: true),
                    MaxScale = table.Column<short>(type: "smallint", nullable: true),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquilibriumRanges", x => x.TickerId);
                    table.ForeignKey(
                        name: "FK_EquilibriumRanges_Tickers_TickerId",
                        column: x => x.TickerId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "Tickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GaugeAvailability",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    TickerId = table.Column<Guid>(type: "uuid", nullable: false),
                    HasPe = table.Column<bool>(type: "boolean", nullable: false),
                    HasPs = table.Column<bool>(type: "boolean", nullable: false),
                    LastFetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GaugeAvailability", x => x.TickerId);
                    table.ForeignKey(
                        name: "FK_GaugeAvailability_Tickers_TickerId",
                        column: x => x.TickerId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "Tickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GaugeDetails",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    TickerId = table.Column<Guid>(type: "uuid", nullable: false),
                    GaugeType = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    A = table.Column<int>(type: "integer", nullable: false),
                    B = table.Column<int>(type: "integer", nullable: false),
                    C = table.Column<int>(type: "integer", nullable: false),
                    D = table.Column<int>(type: "integer", nullable: false),
                    E = table.Column<int>(type: "integer", nullable: false),
                    F = table.Column<int>(type: "integer", nullable: false),
                    CloseHigh = table.Column<BigInteger>(type: "numeric", nullable: true),
                    CloseMiddle = table.Column<BigInteger>(type: "numeric", nullable: true),
                    CloseLow = table.Column<BigInteger>(type: "numeric", nullable: true),
                    CloseSign = table.Column<short>(type: "smallint", nullable: true),
                    CloseScale = table.Column<short>(type: "smallint", nullable: true),
                    StartHigh = table.Column<BigInteger>(type: "numeric", nullable: false),
                    StartMiddle = table.Column<BigInteger>(type: "numeric", nullable: false),
                    StartLow = table.Column<BigInteger>(type: "numeric", nullable: false),
                    StartSign = table.Column<short>(type: "smallint", nullable: false),
                    StartScale = table.Column<short>(type: "smallint", nullable: false),
                    EndHigh = table.Column<BigInteger>(type: "numeric", nullable: false),
                    EndMiddle = table.Column<BigInteger>(type: "numeric", nullable: false),
                    EndLow = table.Column<BigInteger>(type: "numeric", nullable: false),
                    EndSign = table.Column<short>(type: "smallint", nullable: false),
                    EndScale = table.Column<short>(type: "smallint", nullable: false),
                    MinHigh = table.Column<BigInteger>(type: "numeric", nullable: false),
                    MinMiddle = table.Column<BigInteger>(type: "numeric", nullable: false),
                    MinLow = table.Column<BigInteger>(type: "numeric", nullable: false),
                    MinSign = table.Column<short>(type: "smallint", nullable: false),
                    MinScale = table.Column<short>(type: "smallint", nullable: false),
                    AvgHigh = table.Column<BigInteger>(type: "numeric", nullable: false),
                    AvgMiddle = table.Column<BigInteger>(type: "numeric", nullable: false),
                    AvgLow = table.Column<BigInteger>(type: "numeric", nullable: false),
                    AvgSign = table.Column<short>(type: "smallint", nullable: false),
                    AvgScale = table.Column<short>(type: "smallint", nullable: false),
                    MaxHigh = table.Column<BigInteger>(type: "numeric", nullable: false),
                    MaxMiddle = table.Column<BigInteger>(type: "numeric", nullable: false),
                    MaxLow = table.Column<BigInteger>(type: "numeric", nullable: false),
                    MaxSign = table.Column<short>(type: "smallint", nullable: false),
                    MaxScale = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GaugeDetails", x => new { x.TickerId, x.GaugeType });
                    table.CheckConstraint("CK_CyclicalWavesV2GaugeDetails_CloseParts", "(\"CloseHigh\" IS NULL) = (\"CloseMiddle\" IS NULL) AND (\"CloseHigh\" IS NULL) = (\"CloseLow\" IS NULL) AND (\"CloseHigh\" IS NULL) = (\"CloseSign\" IS NULL) AND (\"CloseHigh\" IS NULL) = (\"CloseScale\" IS NULL)");
                    table.CheckConstraint("CK_CyclicalWavesV2GaugeDetails_DayCounts", "\"A\" >= 0 AND \"B\" >= 0 AND \"C\" >= 0 AND \"D\" >= 0 AND \"E\" >= 0 AND \"F\" >= 0");
                    table.CheckConstraint("CK_CyclicalWavesV2GaugeDetails_Scales", "(\"CloseScale\" IS NULL OR \"CloseScale\" BETWEEN 0 AND 16383) AND \"StartScale\" BETWEEN 0 AND 16383 AND \"EndScale\" BETWEEN 0 AND 16383 AND \"MinScale\" BETWEEN 0 AND 16383 AND \"AvgScale\" BETWEEN 0 AND 16383 AND \"MaxScale\" BETWEEN 0 AND 16383");
                    table.CheckConstraint("CK_CyclicalWavesV2GaugeDetails_Signs", "(\"CloseSign\" IS NULL OR \"CloseSign\" BETWEEN -1 AND 1) AND \"StartSign\" BETWEEN -1 AND 1 AND \"EndSign\" BETWEEN -1 AND 1 AND \"MinSign\" BETWEEN -1 AND 1 AND \"AvgSign\" BETWEEN -1 AND 1 AND \"MaxSign\" BETWEEN -1 AND 1");
                    table.CheckConstraint("CK_CyclicalWavesV2GaugeDetails_Status", "\"Status\" IS NULL OR \"Status\" BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_CyclicalWavesV2GaugeDetails_Type", "\"GaugeType\" IN ('PE', 'PS')");
                    table.ForeignKey(
                        name: "FK_GaugeDetails_Tickers_TickerId",
                        column: x => x.TickerId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "Tickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CyclicalWavesV2SyncRuns_Scope",
                schema: "cyclicalwaves",
                table: "SyncRuns",
                sql: "\"Scope\" IN ('Catalog', 'Ticker', 'Screener', 'IncomeStatements', 'Analysis', 'GaugeCatalog', 'GaugeDetails', 'EquilibriumCatalog', 'EquilibriumDetails', 'TickerGaugeEquilibrium', 'Full')");

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesV2EquilibriumRanges_HasRange",
                schema: "cyclicalwaves",
                table: "EquilibriumRanges",
                column: "HasRange");

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesV2GaugeAvailability_HasPe",
                schema: "cyclicalwaves",
                table: "GaugeAvailability",
                column: "HasPe");

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesV2GaugeAvailability_HasPs",
                schema: "cyclicalwaves",
                table: "GaugeAvailability",
                column: "HasPs");

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesV2GaugeDetails_TypeStatus",
                schema: "cyclicalwaves",
                table: "GaugeDetails",
                columns: new[] { "GaugeType", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquilibriumRanges",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "GaugeAvailability",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "GaugeDetails",
                schema: "cyclicalwaves");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CyclicalWavesV2SyncRuns_Scope",
                schema: "cyclicalwaves",
                table: "SyncRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CyclicalWavesV2SyncRuns_Scope",
                schema: "cyclicalwaves",
                table: "SyncRuns",
                sql: "\"Scope\" IN ('Catalog', 'Ticker', 'Screener', 'IncomeStatements', 'Analysis', 'Full')");
        }
    }
}
