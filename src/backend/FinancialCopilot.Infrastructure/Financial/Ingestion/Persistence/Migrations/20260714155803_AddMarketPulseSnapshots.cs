using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketPulseSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketPulseSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GeneratedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Segment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CadenceSlot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsPartial = table.Column<bool>(type: "boolean", nullable: false),
                    IsFinal = table.Column<bool>(type: "boolean", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SupersedesSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefinitionVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceWatermarkUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TransactionValue = table.Column<decimal>(type: "numeric", nullable: true),
                    RetailTradeValue = table.Column<decimal>(type: "numeric", nullable: true),
                    EquityRealMoneyFlow = table.Column<decimal>(type: "numeric", nullable: true),
                    FixedIncomeFundRealMoneyFlow = table.Column<decimal>(type: "numeric", nullable: true),
                    BuyQueueCount = table.Column<int>(type: "integer", nullable: true),
                    BuyQueueValue = table.Column<decimal>(type: "numeric", nullable: true),
                    SellQueueCount = table.Column<int>(type: "integer", nullable: true),
                    SellQueueValue = table.Column<decimal>(type: "numeric", nullable: true),
                    FactsJson = table.Column<string>(type: "text", nullable: false),
                    BreadthJson = table.Column<string>(type: "text", nullable: false),
                    LeadingIndustriesJson = table.Column<string>(type: "text", nullable: false),
                    LaggingIndustriesJson = table.Column<string>(type: "text", nullable: false),
                    ComparisonsJson = table.Column<string>(type: "text", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketPulseSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketPulseSnapshots_MarketPulseSnapshots_SupersedesSnapsho~",
                        column: x => x.SupersedesSnapshotId,
                        principalTable: "MarketPulseSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketPulseSnapshots_Segment_IsCurrent_GeneratedAtUtc",
                table: "MarketPulseSnapshots",
                columns: new[] { "Segment", "IsCurrent", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketPulseSnapshots_Segment_IsFinal_IsCurrent_TradingDate",
                table: "MarketPulseSnapshots",
                columns: new[] { "Segment", "IsFinal", "IsCurrent", "TradingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketPulseSnapshots_SupersedesSnapshotId",
                table: "MarketPulseSnapshots",
                column: "SupersedesSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPulseSnapshots_TradingDate_Segment_IsCurrent",
                table: "MarketPulseSnapshots",
                columns: new[] { "TradingDate", "Segment", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketPulseSnapshots_TradingDate_Segment_SessionState_Cade~1",
                table: "MarketPulseSnapshots",
                columns: new[] { "TradingDate", "Segment", "SessionState", "CadenceSlot", "DefinitionVersion", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketPulseSnapshots_TradingDate_Segment_SessionState_Caden~",
                table: "MarketPulseSnapshots",
                columns: new[] { "TradingDate", "Segment", "SessionState", "CadenceSlot", "DefinitionVersion" },
                unique: true,
                filter: "\"IsCurrent\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketPulseSnapshots");
        }
    }
}
