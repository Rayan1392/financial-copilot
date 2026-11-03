using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature148CyclicalWavesV2Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cyclicalwaves");

            migrationBuilder.CreateTable(
                name: "Analyses",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    PlainTextSummary = table.Column<string>(type: "text", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "char(64)", nullable: false),
                    FirstSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Analyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IncomeStatementLineDefinitions",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    NameEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeStatementLineDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetricDefinitions",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    FieldKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricDefinitions", x => x.Id);
                    table.CheckConstraint("CK_CyclicalWavesV2MetricDefinitions_Category", "\"Category\" IN ('MonthlySales', 'QuarterlySales', 'Profit', 'ProfitMargin', 'Valuation')");
                    table.CheckConstraint("CK_CyclicalWavesV2MetricDefinitions_Unit", "\"Unit\" IN ('Irr', 'Percent', 'Ratio')");
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TickerFilter = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AnalysisMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    TriggerSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    ProcessedItems = table.Column<int>(type: "integer", nullable: false),
                    UpdatedItems = table.Column<int>(type: "integer", nullable: false),
                    UnchangedItems = table.Column<int>(type: "integer", nullable: false),
                    FailedItems = table.Column<int>(type: "integer", nullable: false),
                    ErrorSummary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    LockOwner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LockLeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                    table.CheckConstraint("CK_CyclicalWavesV2SyncRuns_Scope", "\"Scope\" IN ('Catalog', 'Ticker', 'Screener', 'IncomeStatements', 'Analysis', 'Full')");
                    table.CheckConstraint("CK_CyclicalWavesV2SyncRuns_Status", "\"Status\" IN ('Requested', 'Running', 'Succeeded', 'CompletedWithErrors', 'Failed', 'SkippedAlreadyRunning')");
                });

            migrationBuilder.CreateTable(
                name: "Tickers",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedTicker = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enticker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenInCatalogAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisCategories",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    AnalysisId = table.Column<long>(type: "bigint", nullable: false),
                    ProviderCategoryId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisCategories", x => new { x.AnalysisId, x.ProviderCategoryId });
                    table.ForeignKey(
                        name: "FK_AnalysisCategories_Analyses_AnalysisId",
                        column: x => x.AnalysisId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "Analyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisTags",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    AnalysisId = table.Column<long>(type: "bigint", nullable: false),
                    ProviderTagId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisTags", x => new { x.AnalysisId, x.ProviderTagId });
                    table.ForeignKey(
                        name: "FK_AnalysisTags_Analyses_AnalysisId",
                        column: x => x.AnalysisId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "Analyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinancialValues",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    TickerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricDefinitionId = table.Column<short>(type: "smallint", nullable: false),
                    NumericValue = table.Column<decimal>(type: "numeric(38,6)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialValues", x => new { x.TickerId, x.MetricDefinitionId });
                    table.ForeignKey(
                        name: "FK_FinancialValues_MetricDefinitions_MetricDefinitionId",
                        column: x => x.MetricDefinitionId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "MetricDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialValues_Tickers_TickerId",
                        column: x => x.TickerId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "Tickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncomeStatements",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TickerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodEndingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FiscalYear = table.Column<int>(type: "integer", nullable: false),
                    FiscalMonth = table.Column<int>(type: "integer", nullable: false),
                    Audited = table.Column<bool>(type: "boolean", nullable: true),
                    PublishDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CodalUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeStatements", x => x.Id);
                    table.CheckConstraint("CK_CyclicalWavesV2IncomeStatements_FiscalMonth", "\"FiscalMonth\" BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "FK_IncomeStatements_Tickers_TickerId",
                        column: x => x.TickerId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "Tickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScreenerMetadata",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    TickerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastMonthSaleDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastQuarterDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProviderUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreenerMetadata", x => x.TickerId);
                    table.ForeignKey(
                        name: "FK_ScreenerMetadata_Tickers_TickerId",
                        column: x => x.TickerId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "Tickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncomeStatementValues",
                schema: "cyclicalwaves",
                columns: table => new
                {
                    StatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Quarter = table.Column<decimal>(type: "numeric(38,6)", nullable: true),
                    YearToDate = table.Column<decimal>(type: "numeric(38,6)", nullable: true),
                    TrailingYear = table.Column<decimal>(type: "numeric(38,6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeStatementValues", x => new { x.StatementId, x.LineDefinitionId });
                    table.ForeignKey(
                        name: "FK_IncomeStatementValues_IncomeStatementLineDefinitions_LineDe~",
                        column: x => x.LineDefinitionId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "IncomeStatementLineDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncomeStatementValues_IncomeStatements_StatementId",
                        column: x => x.StatementId,
                        principalSchema: "cyclicalwaves",
                        principalTable: "IncomeStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "cyclicalwaves",
                table: "MetricDefinitions",
                columns: new[] { "Id", "Category", "FieldKey", "NameEn", "NameFa", "SortOrder", "Unit" },
                values: new object[,]
                {
                    { (short)1, "QuarterlySales", "lastQuarterSale", "Last quarter sales", "فروش آخرین فصل مالی", (short)1, "Irr" },
                    { (short)2, "QuarterlySales", "penultimateQuarterSale", "Penultimate quarter sales", "فروش فصل ماقبل آخر", (short)2, "Irr" },
                    { (short)3, "QuarterlySales", "lastYearSameQuarterSale", "Same quarter last year sales", "فروش فصل مشابه سال قبل", (short)3, "Irr" },
                    { (short)4, "QuarterlySales", "average4QuarterSale", "Average sales of the four quarters before the last quarter", "میانگین فروش چهار فصل پیش از فصل آخر", (short)4, "Irr" },
                    { (short)5, "Profit", "lastQuarterNetProfit", "Last quarter net profit", "سود خالص آخرین فصل", (short)5, "Irr" },
                    { (short)6, "Profit", "penultimateQuarterNetProfit", "Penultimate quarter net profit", "سود خالص فصل ماقبل آخر", (short)6, "Irr" },
                    { (short)7, "Profit", "lastYearSameQuarterNetProfit", "Same quarter last year net profit", "سود خالص فصل مشابه سال قبل", (short)7, "Irr" },
                    { (short)8, "Profit", "lastQuarterGrossProfit", "Last quarter gross profit", "سود ناخالص آخرین فصل", (short)8, "Irr" },
                    { (short)9, "Profit", "penultimateQuarterGrossProfit", "Penultimate quarter gross profit", "سود ناخالص فصل ماقبل آخر", (short)9, "Irr" },
                    { (short)10, "Profit", "lastYearSameQuarterGrossProfit", "Same quarter last year gross profit", "سود ناخالص فصل مشابه سال قبل", (short)10, "Irr" },
                    { (short)11, "Profit", "lastQuarterOperatingProfit", "Last quarter operating profit", "سود عملیاتی آخرین فصل", (short)11, "Irr" },
                    { (short)12, "Profit", "penultimateQuarterOperatingProfit", "Penultimate quarter operating profit", "سود عملیاتی فصل ماقبل آخر", (short)12, "Irr" },
                    { (short)13, "Profit", "lastYearSameQuarterOperatingProfit", "Same quarter last year operating profit", "سود عملیاتی فصل مشابه سال قبل", (short)13, "Irr" },
                    { (short)14, "ProfitMargin", "lastQuarterNetProfitMargin", "Last quarter net profit margin", "حاشیه سود خالص آخرین فصل", (short)14, "Percent" },
                    { (short)15, "ProfitMargin", "penultimateQuarterNetProfitMargin", "Penultimate quarter net profit margin", "حاشیه سود خالص فصل ماقبل آخر", (short)15, "Percent" },
                    { (short)16, "ProfitMargin", "lastYearSameQuarterNetProfitMargin", "Same quarter last year net profit margin", "حاشیه سود خالص فصل مشابه سال قبل", (short)16, "Percent" },
                    { (short)17, "ProfitMargin", "lastQuarterGrossProfitMargin", "Last quarter gross profit margin", "حاشیه سود ناخالص آخرین فصل", (short)17, "Percent" },
                    { (short)18, "ProfitMargin", "penultimateQuarterGrossProfitMargin", "Penultimate quarter gross profit margin", "حاشیه سود ناخالص فصل ماقبل آخر", (short)18, "Percent" },
                    { (short)19, "ProfitMargin", "lastYearSameQuarterGrossProfitMargin", "Same quarter last year gross profit margin", "حاشیه سود ناخالص فصل مشابه سال قبل", (short)19, "Percent" },
                    { (short)20, "ProfitMargin", "lastQuarterOperatingProfitMargin", "Last quarter operating profit margin", "حاشیه سود عملیاتی آخرین فصل", (short)20, "Percent" },
                    { (short)21, "ProfitMargin", "penultimateQuarterOperatingProfitMargin", "Penultimate quarter operating profit margin", "حاشیه سود عملیاتی فصل ماقبل آخر", (short)21, "Percent" },
                    { (short)22, "ProfitMargin", "lastYearSameQuarterOperatingProfitMargin", "Same quarter last year operating profit margin", "حاشیه سود عملیاتی فصل مشابه سال قبل", (short)22, "Percent" },
                    { (short)23, "MonthlySales", "average12MonthSale", "Average monthly sales over 12 months, excluding the latest month", "میانگین فروش ۱۲ ماه، بدون احتساب تازه‌ترین ماه", (short)23, "Irr" },
                    { (short)24, "MonthlySales", "lastYearAverage12MonthSale", "Average monthly sales of the previous fiscal year", "میانگین فروش ماهانه سال مالی قبل", (short)24, "Irr" },
                    { (short)25, "MonthlySales", "lastMonthSale", "Last month sales", "فروش آخرین ماه", (short)25, "Irr" },
                    { (short)26, "MonthlySales", "penultimateMonthSale", "Penultimate month sales", "فروش ماه ماقبل آخر", (short)26, "Irr" },
                    { (short)27, "MonthlySales", "lastYearSameMonthSale", "Same month last year sales", "فروش ماه مشابه سال قبل", (short)27, "Irr" },
                    { (short)28, "Valuation", "pe", "Price to earnings (P/E)", "نسبت قیمت به سود (P/E)", (short)28, "Ratio" },
                    { (short)29, "Valuation", "ps", "Price to sales (P/S)", "نسبت قیمت به فروش (P/S)", (short)29, "Ratio" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesV2Analyses_PublishedAt",
                schema: "cyclicalwaves",
                table: "Analyses",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesV2AnalysisTags_NormalizedName",
                schema: "cyclicalwaves",
                table: "AnalysisTags",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesV2FinancialValues_Metric",
                schema: "cyclicalwaves",
                table: "FinancialValues",
                column: "MetricDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_CyclicalWavesV2IncomeStatements_Period",
                schema: "cyclicalwaves",
                table: "IncomeStatements",
                columns: new[] { "TickerId", "PeriodEndingDate", "FiscalMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesV2IncomeStatementValues_Line",
                schema: "cyclicalwaves",
                table: "IncomeStatementValues",
                column: "LineDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_CyclicalWavesV2MetricDefinitions_FieldKey",
                schema: "cyclicalwaves",
                table: "MetricDefinitions",
                column: "FieldKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesV2SyncRuns_RequestedAt",
                schema: "cyclicalwaves",
                table: "SyncRuns",
                column: "RequestedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "UX_CyclicalWavesV2SyncRuns_SingleRunning",
                schema: "cyclicalwaves",
                table: "SyncRuns",
                column: "Status",
                unique: true,
                filter: "\"Status\" = 'Running'");

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesV2Tickers_NormalizedTicker",
                schema: "cyclicalwaves",
                table: "Tickers",
                column: "NormalizedTicker");

            migrationBuilder.CreateIndex(
                name: "UX_CyclicalWavesV2Tickers_Enticker",
                schema: "cyclicalwaves",
                table: "Tickers",
                column: "Enticker",
                unique: true,
                filter: "\"Enticker\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CyclicalWavesV2Tickers_Ticker",
                schema: "cyclicalwaves",
                table: "Tickers",
                column: "Ticker",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisCategories",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "AnalysisTags",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "FinancialValues",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "IncomeStatementValues",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "ScreenerMetadata",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "SyncRuns",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "Analyses",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "MetricDefinitions",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "IncomeStatementLineDefinitions",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "IncomeStatements",
                schema: "cyclicalwaves");

            migrationBuilder.DropTable(
                name: "Tickers",
                schema: "cyclicalwaves");

            // Non-cascading: fails rather than dropping anything this migration did not create.
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS cyclicalwaves;");
        }
    }
}
