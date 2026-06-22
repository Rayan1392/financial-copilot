using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Semantics.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMetricRegistryCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------
            // 1. Add capability columns to FinancialMetricDefinitions
            // ---------------------------------------------------------------
            migrationBuilder.AddColumn<string>(
                name: "PersianTitle",
                table: "FinancialMetricDefinitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "LookupEligible",
                table: "FinancialMetricDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ScannerEligible",
                table: "FinancialMetricDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMonthlyActivityMetric",
                table: "FinancialMetricDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsValuationMetric",
                table: "FinancialMetricDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsGrowthMetric",
                table: "FinancialMetricDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMarginMetric",
                table: "FinancialMetricDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFundamentalMetric",
                table: "FinancialMetricDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SuppressQuoteContext",
                table: "FinancialMetricDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ---------------------------------------------------------------
            // 2. Create MetricPeriodAliases table
            // ---------------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "MetricPeriodAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AliasText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedAliasText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PeriodType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PeriodSelector = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricPeriodAliases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetricPeriodAliases_Language_Status",
                table: "MetricPeriodAliases",
                columns: new[] { "Language", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricPeriodAliases_NormalizedAliasText_Language",
                table: "MetricPeriodAliases",
                columns: new[] { "NormalizedAliasText", "Language" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            // ---------------------------------------------------------------
            // 3. Seed FinancialMetricDefinitions rows from PhaseOneFinancialSemanticCatalog
            //    ON CONFLICT DO NOTHING so re-runs are safe.
            // ---------------------------------------------------------------
            // columns: MetricCode, MetricVersion, DisplayName, PersianTitle, Description, Category, UnitCode, EffectiveFrom
            var defRows = new[]
            {
                // Monthly activity
                ("NET_PROFIT",                       "v1", "Net Profit",                              "",  "amount",          "Profitability"),
                ("MONTHLY_SALES",                    "v1", "Monthly Sales",                           "",  "amount",          "SalesAndProduction"),
                ("MONTHLY_SALES_YTD",                "v1", "Monthly Sales Year To Date",              "",  "amount",          "SalesAndProduction"),
                ("MONTHLY_SALES_YTD_PREVIOUS_MONTH", "v1", "Monthly Sales Year To Previous Month",    "",  "amount",          "SalesAndProduction"),
                ("MONTHLY_SALES_QUANTITY",            "v1", "Monthly Sales Quantity",                  "",  "quantity",        "SalesAndProduction"),
                ("MONTHLY_PRODUCTION_QUANTITY",       "v1", "Monthly Production Quantity",             "",  "quantity",        "SalesAndProduction"),
                ("MONTHLY_SALES_RATE",                "v1", "Monthly Sales Rate",                      "",  "amount-per-unit", "SalesAndProduction"),
                // Quarterly income statement
                ("REVENUE",                          "v1", "Revenue",                                 "",  "amount",          "Profitability"),
                ("TOTAL_REVENUE",                    "v1", "Total Revenue",                           "",  "amount",          "Profitability"),
                ("GROSS_PROFIT",                     "v1", "Gross Profit",                            "",  "amount",          "Profitability"),
                ("OPERATING_PROFIT",                 "v1", "Operating Profit",                        "",  "amount",          "Profitability"),
                ("EPS",                              "v1", "Earnings per Share",                      "",  "amount-per-share","Profitability"),
                ("EPS_CONSOLIDATED",                 "v1", "Earnings per Share (Consolidated)",       "",  "amount-per-share","Profitability"),
                ("FINANCE_COSTS",                    "v1", "Finance Costs",                           "",  "amount",          "FinancialHealth"),
                ("INCOME_TAX",                       "v1", "Income Tax",                              "",  "amount",          "FinancialHealth"),
                // Balance sheet
                ("TOTAL_EQUITY",                     "v1", "Total Equity",                            "",  "amount",          "FinancialHealth"),
                ("CAPITAL",                          "v1", "Capital",                                 "",  "amount",          "FinancialHealth"),
                ("OPERATING_CASH_FLOW",              "v1", "Operating Cash Flow",                     "",  "amount",          "FinancialHealth"),
                // Market / quote
                ("LATEST_PRICE",                     "v1", "Latest Observed Price",                   "",  "amount",          "Valuation"),
                ("DAILY_CHANGE_PCT",                 "v1", "Daily Change Percent",                    "",  "percent",         "Valuation"),
                ("MARKET_CAP",                       "v1", "Market Capitalization",                   "",  "amount",          "Valuation"),
                ("SHARES_OUTSTANDING",               "v1", "Shares Outstanding",                      "",  "amount",          "FinancialHealth"),
                // Rolling averages
                ("AVG_4Q_REVENUE",                   "v1", "Average 4-Quarter Revenue",               "",  "amount",          "SalesAndProduction"),
                ("AVG_12M_MONTHLY_SALES",             "v1", "Average 12-Month Monthly Sales",          "",  "amount",          "SalesAndProduction"),
                // TTM internals
                ("TTM_SALES",                         "v1", "TTM Sales",                               "",  "amount",          "SalesAndProduction"),
                ("TTM_EARNINGS",                      "v1", "TTM Earnings",                            "",  "amount",          "Profitability"),
                ("TTM_EPS",                           "v1", "TTM EPS",                                 "",  "amount",          "Profitability"),
                // Valuation
                ("PE_TTM",                            "v1", "P/E (TTM)",                               "",  "ratio",           "Valuation"),
                ("PS_TTM",                            "v1", "P/S (TTM)",                               "",  "ratio",           "Valuation"),
                // EBIT
                ("EBIT",                              "v1", "EBIT",                                    "",  "amount",          "Profitability"),
                // YoY growth
                ("NET_PROFIT_GROWTH_YOY",             "v1", "Net Profit Growth YoY",                   "",  "percent",         "Growth"),
                ("REVENUE_GROWTH_YOY",                "v1", "Revenue Growth YoY",                      "",  "percent",         "Growth"),
                ("GROSS_PROFIT_GROWTH_YOY",           "v1", "Gross Profit Growth YoY",                 "",  "percent",         "Growth"),
                ("OPERATING_PROFIT_GROWTH_YOY",       "v1", "Operating Profit Growth YoY",             "",  "percent",         "Growth"),
                ("EPS_GROWTH_YOY",                    "v1", "EPS Growth YoY",                          "",  "percent",         "Growth"),
                ("EBIT_GROWTH_YOY",                   "v1", "EBIT Growth YoY",                         "",  "percent",         "Growth"),
                ("EQUITY_GROWTH_YOY",                 "v1", "Equity Growth YoY",                       "",  "percent",         "Growth"),
                ("MONTHLY_SALES_GROWTH_YOY",          "v1", "Monthly Sales Growth YoY",                "",  "percent",         "Growth"),
                // QoQ growth
                ("NET_PROFIT_GROWTH_QOQ",             "v1", "Net Profit Growth QoQ",                   "",  "percent",         "Growth"),
                ("REVENUE_GROWTH_QOQ",                "v1", "Revenue Growth QoQ",                      "",  "percent",         "Growth"),
                ("GROSS_PROFIT_GROWTH_QOQ",           "v1", "Gross Profit Growth QoQ",                 "",  "percent",         "Growth"),
                ("OPERATING_PROFIT_GROWTH_QOQ",       "v1", "Operating Profit Growth QoQ",             "",  "percent",         "Growth"),
                ("EPS_GROWTH_QOQ",                    "v1", "EPS Growth QoQ",                          "",  "percent",         "Growth"),
                ("EBIT_GROWTH_QOQ",                   "v1", "EBIT Growth QoQ",                         "",  "percent",         "Growth"),
                ("EQUITY_GROWTH_QOQ",                 "v1", "Equity Growth QoQ",                       "",  "percent",         "Growth"),
                ("MONTHLY_SALES_GROWTH_MOM",          "v1", "Monthly Sales Growth MoM",                "",  "percent",         "Growth"),
                // Vendor growth rates
                ("SALES_GROWTH_RATE",                 "v1", "Sales Growth Rate (Vendor)",              "",  "percent",         "Growth"),
                ("NET_PROFIT_GROWTH_RATE",            "v1", "Net Profit Growth Rate (Vendor)",         "",  "percent",         "Growth"),
                ("EQUITY_GROWTH_RATE",                "v1", "Equity Growth Rate (Vendor)",             "",  "percent",         "Growth"),
                ("TOTAL_ASSETS_GROWTH_RATE",          "v1", "Total Assets Growth Rate (Vendor)",       "",  "percent",         "Growth"),
                ("TOTAL_DEBT_GROWTH_RATE",            "v1", "Total Debt Growth Rate (Vendor)",         "",  "percent",         "Growth"),
                ("TANGIBLE_FIXED_ASSETS_GROWTH_RATE", "v1", "Tangible Fixed Assets Growth Rate (Vendor)","","percent",        "Growth"),
                // Vendor ratios / financial health
                ("CURRENT_RATIO",                     "v1", "Current Ratio",                           "",  "ratio",           "FinancialHealth"),
                ("QUICK_RATIO",                       "v1", "Quick Ratio",                             "",  "ratio",           "FinancialHealth"),
                ("NET_WORKING_CAPITAL",               "v1", "Net Working Capital",                     "",  "amount",          "FinancialHealth"),
                ("COMPREHENSIVE_LIQUIDITY_INDEX",     "v1", "Comprehensive Liquidity Index",           "",  "ratio",           "FinancialHealth"),
                ("CURRENT_ASSETS_TO_TOTAL_ASSETS",    "v1", "Current Assets to Total Assets",          "",  "ratio",           "FinancialHealth"),
                ("CURRENT_DEBT_TO_TOTAL_ASSETS",      "v1", "Current Debt to Total Assets",            "",  "ratio",           "FinancialHealth"),
                ("ASSET_TURNOVER",                    "v1", "Asset Turnover",                          "",  "ratio",           "FinancialHealth"),
                ("TANGIBLE_FIXED_ASSETS_TURNOVER",    "v1", "Tangible Fixed Assets Turnover",          "",  "ratio",           "FinancialHealth"),
                ("OPERATING_ASSETS_RATIO",            "v1", "Operating Assets Ratio",                  "",  "ratio",           "FinancialHealth"),
                ("AVERAGE_COLLECTION_PERIOD",         "v1", "Average Collection Period",               "",  "days",            "FinancialHealth"),
                ("RETURN_ON_ASSETS",                  "v1", "Return on Assets",                        "",  "percent",         "Profitability"),
                ("RETURN_ON_EQUITY",                  "v1", "Return on Equity",                        "",  "percent",         "Profitability"),
                ("RETURN_ON_INVESTMENT",              "v1", "Return on Investment",                    "",  "percent",         "Profitability"),
                ("NET_RETURN_ON_WORKING_CAPITAL",     "v1", "Net Return on Working Capital",           "",  "percent",         "Profitability"),
                ("DEBT_TO_EQUITY",                    "v1", "Debt to Equity",                          "",  "ratio",           "FinancialHealth"),
                // Margins
                ("NET_PROFIT_MARGIN",                 "v1", "Net Profit Margin",                       "",  "percent",         "Profitability"),
                ("GROSS_PROFIT_MARGIN",               "v1", "Gross Profit Margin",                     "",  "percent",         "Profitability"),
                ("OPERATING_PROFIT_MARGIN",           "v1", "Operating Profit Margin",                 "",  "percent",         "Profitability"),
            };

            var effectiveFrom = new DateOnly(2026, 1, 1);
            foreach (var (code, ver, display, persian, unit, category) in defRows)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO "FinancialMetricDefinitions"
                        ("MetricCode","MetricVersion","DisplayName","PersianTitle","Description","Category","UnitCode","EffectiveFrom",
                         "LookupEligible","ScannerEligible","IsMonthlyActivityMetric","IsValuationMetric",
                         "IsGrowthMetric","IsMarginMetric","IsFundamentalMetric","SuppressQuoteContext")
                    VALUES ('{code}','{ver}','{display}','{persian}','','{category}','{unit}','2026-01-01',
                            false,false,false,false,false,false,false,false)
                    ON CONFLICT ("MetricCode","MetricVersion") DO NOTHING;
                    """);
            }

            // ---------------------------------------------------------------
            // 4. Seed FinancialMetricDefinitions capability flags (UPDATE existing rows)
            // ---------------------------------------------------------------
            // Monthly activity metrics — lookup + scanner eligible, suppress quote context
            var monthlyMetrics = new[]
            {
                ("MONTHLY_SALES",                   "فروش ماهانه",                                  true,  true),
                ("MONTHLY_SALES_YTD",               "فروش تجمیعی سال جاری",                         true,  true),
                ("MONTHLY_SALES_YTD_PREVIOUS_MONTH","فروش تجمیعی تا ماه قبل",                       true,  true),
                ("MONTHLY_SALES_QUANTITY",          "مقدار فروش ماهانه",                             true,  true),
                ("MONTHLY_SALES_RATE",              "نرخ فروش ماهانه",                               true,  true),
                ("MONTHLY_PRODUCTION_QUANTITY",     "مقدار تولید ماهانه",                            true,  true),
                ("AVG_12M_MONTHLY_SALES",           "متوسط فروش ۱۲ ماهه",                            true,  true),
                ("MONTHLY_SALES_GROWTH_YOY",        "رشد فروش ماهانه نسبت به مدت مشابه سال قبل",    true,  true),
                ("MONTHLY_SALES_GROWTH_MOM",        "رشد فروش ماهانه نسبت به ماه قبل",              true,  true),
            };

            foreach (var (code, persian, lookup, scanner) in monthlyMetrics)
            {
                migrationBuilder.Sql($"""
                    UPDATE "FinancialMetricDefinitions"
                    SET "PersianTitle" = '{persian}',
                        "LookupEligible" = {(lookup ? "true" : "false")},
                        "ScannerEligible" = {(scanner ? "true" : "false")},
                        "IsMonthlyActivityMetric" = true,
                        "SuppressQuoteContext" = true
                    WHERE "MetricCode" = '{code}';
                    """);
            }

            // Quarterly profitability
            var profitabilityMetrics = new[]
            {
                ("REVENUE",          "درآمد عملیاتی / فروش",                   true,  true,  false, false),
                ("GROSS_PROFIT",     "سود ناخالص",                              true,  true,  false, false),
                ("OPERATING_PROFIT", "سود عملیاتی",                             true,  true,  false, false),
                ("NET_PROFIT",       "سود خالص",                                true,  true,  false, false),
                ("EBIT",             "سود قبل از بهره و مالیات",                true,  true,  false, false),
                ("AVG_4Q_REVENUE",   "متوسط فروش چهار فصل",                     true,  true,  false, false),
            };

            foreach (var (code, persian, lookup, scanner, isGrowth, isMargin) in profitabilityMetrics)
            {
                migrationBuilder.Sql($"""
                    UPDATE "FinancialMetricDefinitions"
                    SET "PersianTitle" = '{persian}',
                        "LookupEligible" = {(lookup ? "true" : "false")},
                        "ScannerEligible" = {(scanner ? "true" : "false")},
                        "IsGrowthMetric" = {(isGrowth ? "true" : "false")},
                        "IsMarginMetric" = {(isMargin ? "true" : "false")}
                    WHERE "MetricCode" = '{code}';
                    """);
            }

            // Margin metrics
            var marginMetrics = new[]
            {
                ("NET_PROFIT_MARGIN",       "حاشیه سود خالص"),
                ("GROSS_PROFIT_MARGIN",     "حاشیه سود ناخالص"),
                ("OPERATING_PROFIT_MARGIN", "حاشیه سود عملیاتی"),
            };

            foreach (var (code, persian) in marginMetrics)
            {
                migrationBuilder.Sql($"""
                    UPDATE "FinancialMetricDefinitions"
                    SET "PersianTitle" = '{persian}',
                        "LookupEligible" = true,
                        "ScannerEligible" = true,
                        "IsMarginMetric" = true
                    WHERE "MetricCode" = '{code}';
                    """);
            }

            // Valuation metrics
            migrationBuilder.Sql("""
                UPDATE "FinancialMetricDefinitions"
                SET "PersianTitle" = 'نسبت قیمت به سود',
                    "LookupEligible" = true,
                    "ScannerEligible" = true,
                    "IsValuationMetric" = true
                WHERE "MetricCode" = 'PE_TTM';
                """);

            migrationBuilder.Sql("""
                UPDATE "FinancialMetricDefinitions"
                SET "PersianTitle" = 'نسبت قیمت به فروش',
                    "LookupEligible" = true,
                    "ScannerEligible" = true,
                    "IsValuationMetric" = true
                WHERE "MetricCode" = 'PS_TTM';
                """);

            // Growth metrics (quarterly YoY)
            var growthYoyMetrics = new[]
            {
                ("REVENUE_GROWTH_YOY",          "رشد درآمد نسبت به فصل مشابه سال قبل"),
                ("GROSS_PROFIT_GROWTH_YOY",     "رشد سود ناخالص نسبت به فصل مشابه سال قبل"),
                ("OPERATING_PROFIT_GROWTH_YOY", "رشد سود عملیاتی نسبت به فصل مشابه سال قبل"),
                ("NET_PROFIT_GROWTH_YOY",       "رشد سود خالص نسبت به فصل مشابه سال قبل"),
                ("EPS_GROWTH_YOY",              "رشد سود هر سهم نسبت به فصل مشابه سال قبل"),
                ("EQUITY_GROWTH_YOY",           "رشد حقوق صاحبان سهام نسبت به فصل مشابه سال قبل"),
                ("EBIT_GROWTH_YOY",             "رشد سود قبل از بهره و مالیات نسبت به فصل مشابه سال قبل"),
            };

            foreach (var (code, persian) in growthYoyMetrics)
            {
                migrationBuilder.Sql($"""
                    UPDATE "FinancialMetricDefinitions"
                    SET "PersianTitle" = '{persian}',
                        "LookupEligible" = true,
                        "ScannerEligible" = true,
                        "IsGrowthMetric" = true
                    WHERE "MetricCode" = '{code}';
                    """);
            }

            // Growth metrics (quarterly QoQ)
            var growthQoqMetrics = new[]
            {
                ("REVENUE_GROWTH_QOQ",          "رشد درآمد نسبت به فصل قبل"),
                ("GROSS_PROFIT_GROWTH_QOQ",     "رشد سود ناخالص نسبت به فصل قبل"),
                ("OPERATING_PROFIT_GROWTH_QOQ", "رشد سود عملیاتی نسبت به فصل قبل"),
                ("NET_PROFIT_GROWTH_QOQ",       "رشد سود خالص نسبت به فصل قبل"),
                ("EPS_GROWTH_QOQ",              "رشد سود هر سهم نسبت به فصل قبل"),
                ("EQUITY_GROWTH_QOQ",           "رشد حقوق صاحبان سهام نسبت به فصل قبل"),
                ("EBIT_GROWTH_QOQ",             "رشد سود قبل از بهره و مالیات نسبت به فصل قبل"),
            };

            foreach (var (code, persian) in growthQoqMetrics)
            {
                migrationBuilder.Sql($"""
                    UPDATE "FinancialMetricDefinitions"
                    SET "PersianTitle" = '{persian}',
                        "LookupEligible" = true,
                        "ScannerEligible" = true,
                        "IsGrowthMetric" = true
                    WHERE "MetricCode" = '{code}';
                    """);
            }

            // Fundamental / financial health metrics
            var fundamentalMetrics = new[]
            {
                ("CURRENT_RATIO",                   "نسبت جاری"),
                ("DEBT_TO_EQUITY",                  "نسبت بدهی به حقوق صاحبان سهام"),
                ("NET_WORKING_CAPITAL",             "سرمایه در گردش خالص"),
                ("COMPREHENSIVE_LIQUIDITY_INDEX",   "شاخص جامع نقدینگی"),
                ("CURRENT_ASSETS_TO_TOTAL_ASSETS",  "نسبت دارایی جاری به کل دارایی"),
                ("ASSET_TURNOVER",                  "گردش دارایی‌ها"),
                ("TANGIBLE_FIXED_ASSETS_TURNOVER",  "گردش دارایی‌های ثابت مشهود"),
                ("AVERAGE_COLLECTION_PERIOD",       "دوره وصول مطالبات"),
            };

            foreach (var (code, persian) in fundamentalMetrics)
            {
                migrationBuilder.Sql($"""
                    UPDATE "FinancialMetricDefinitions"
                    SET "PersianTitle" = '{persian}',
                        "LookupEligible" = true,
                        "ScannerEligible" = true,
                        "IsFundamentalMetric" = true
                    WHERE "MetricCode" = '{code}';
                    """);
            }

            // ---------------------------------------------------------------
            // 4. Seed additional DynamicMetricAliases (ManualSeed, Active, confidence=1.0)
            //    Only aliases not already present in the 20260613 migration seed
            // ---------------------------------------------------------------
            var seedAt = new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero);
            const string v1 = "v1";
            const string manualSeed = "ManualSeed";
            const string active = "Active";
            const decimal conf = 1.0m;

            // Helper: each row = (guid-suffix, expression, normalizedExpr, language, metricCode)
            // GUIDs use prefix 22222222-2222-2222-2222-{12-digit-suffix}
            var aliases = new (string Id, string Expr, string Norm, string Lang, string Code)[]
            {
                // ---- MONTHLY_SALES additional aliases ----
                ("000000000001", "فروش شرکت",                      "فروش شرکت",                      "fa", "MONTHLY_SALES"),
                ("000000000002", "فروش ماه قبل",                   "فروش ماه قبل",                   "fa", "MONTHLY_SALES"),
                ("000000000003", "فروش ماه مشابه سال قبل",         "فروش ماه مشابه سال قبل",         "fa", "MONTHLY_SALES"),
                ("000000000004", "مبلغ فروش",                       "مبلغ فروش",                      "fa", "MONTHLY_SALES"),
                ("000000000005", "آخرین فروش",                      "آخرین فروش",                     "fa", "MONTHLY_SALES"),
                ("000000000006", "فروش آخرین ماه",                  "فروش آخرین ماه",                 "fa", "MONTHLY_SALES"),

                // ---- AVG_12M_MONTHLY_SALES additional aliases ----
                ("000000000010", "متوسط فروش یک ساله",              "متوسط فروش یک ساله",             "fa", "AVG_12M_MONTHLY_SALES"),
                ("000000000011", "میانگین فروش یک ساله",            "میانگین فروش یک ساله",           "fa", "AVG_12M_MONTHLY_SALES"),
                ("000000000012", "average monthly sales",            "average monthly sales",           "en", "AVG_12M_MONTHLY_SALES"),
                ("000000000013", "12m average sales",                "12m average sales",               "en", "AVG_12M_MONTHLY_SALES"),

                // ---- MONTHLY_SALES_YTD ----
                ("000000000020", "فروش تجمیعی",                     "فروش تجمیعی",                    "fa", "MONTHLY_SALES_YTD"),
                ("000000000021", "فروش از ابتدای سال",               "فروش از ابتدای سال",              "fa", "MONTHLY_SALES_YTD"),
                ("000000000022", "فروش سال جاری",                    "فروش سال جاری",                   "fa", "MONTHLY_SALES_YTD"),
                ("000000000023", "ytd sales",                        "ytd sales",                       "en", "MONTHLY_SALES_YTD"),

                // ---- MONTHLY_SALES_YTD_PREVIOUS_MONTH ----
                ("000000000030", "فروش تجمیعی تا ماه قبل",          "فروش تجمیعی تا ماه قبل",        "fa", "MONTHLY_SALES_YTD_PREVIOUS_MONTH"),
                ("000000000031", "فروش از ابتدای سال تا ماه قبل",   "فروش از ابتدای سال تا ماه قبل", "fa", "MONTHLY_SALES_YTD_PREVIOUS_MONTH"),
                ("000000000032", "ytd previous month",               "ytd previous month",              "en", "MONTHLY_SALES_YTD_PREVIOUS_MONTH"),

                // ---- MONTHLY_SALES_QUANTITY ----
                ("000000000040", "حجم فروش",                         "حجم فروش",                       "fa", "MONTHLY_SALES_QUANTITY"),
                ("000000000041", "تناژ فروش",                        "تناژ فروش",                      "fa", "MONTHLY_SALES_QUANTITY"),
                ("000000000042", "تعداد فروش",                       "تعداد فروش",                     "fa", "MONTHLY_SALES_QUANTITY"),

                // ---- MONTHLY_SALES_RATE ----
                ("000000000050", "متوسط نرخ فروش",                   "متوسط نرخ فروش",                 "fa", "MONTHLY_SALES_RATE"),
                ("000000000051", "قیمت فروش محصول",                  "قیمت فروش محصول",                "fa", "MONTHLY_SALES_RATE"),

                // ---- MONTHLY_PRODUCTION_QUANTITY ----
                ("000000000060", "حجم تولید",                        "حجم تولید",                      "fa", "MONTHLY_PRODUCTION_QUANTITY"),
                ("000000000061", "تناژ تولید",                       "تناژ تولید",                     "fa", "MONTHLY_PRODUCTION_QUANTITY"),

                // ---- MONTHLY_SALES_GROWTH_YOY ----
                ("000000000070", "رشد فروش سالانه",                  "رشد فروش سالانه",                "fa", "MONTHLY_SALES_GROWTH_YOY"),
                ("000000000071", "رشد فروش نسبت به سال قبل",         "رشد فروش نسبت به سال قبل",       "fa", "MONTHLY_SALES_GROWTH_YOY"),
                ("000000000072", "رشد فروش نسبت به پارسال",          "رشد فروش نسبت به پارسال",        "fa", "MONTHLY_SALES_GROWTH_YOY"),
                ("000000000073", "رشد فروش ماهانه نسبت به سال قبل",  "رشد فروش ماهانه نسبت به سال قبل","fa", "MONTHLY_SALES_GROWTH_YOY"),
                ("000000000074", "رشد فروش ماه مشابه",               "رشد فروش ماه مشابه",             "fa", "MONTHLY_SALES_GROWTH_YOY"),
                ("000000000075", "رشد فروش ماه مشابه سال قبل",       "رشد فروش ماه مشابه سال قبل",    "fa", "MONTHLY_SALES_GROWTH_YOY"),
                ("000000000076", "درصد رشد فروش نسبت به مدت مشابه",  "درصد رشد فروش نسبت به مدت مشابه","fa","MONTHLY_SALES_GROWTH_YOY"),
                ("000000000077", "تغییر فروش نسبت به مدت مشابه",     "تغییر فروش نسبت به مدت مشابه",  "fa", "MONTHLY_SALES_GROWTH_YOY"),
                ("000000000078", "YoY sales growth",                  "yoy sales growth",                "en", "MONTHLY_SALES_GROWTH_YOY"),
                ("000000000079", "sales growth yoy",                  "sales growth yoy",                "en", "MONTHLY_SALES_GROWTH_YOY"),

                // ---- MONTHLY_SALES_GROWTH_MOM ----
                ("000000000080", "رشد فروش ماهانه",                  "رشد فروش ماهانه",                "fa", "MONTHLY_SALES_GROWTH_MOM"),
                ("000000000081", "رشد فروش نسبت به ماه قبل",         "رشد فروش نسبت به ماه قبل",       "fa", "MONTHLY_SALES_GROWTH_MOM"),
                ("000000000082", "تغییر فروش نسبت به ماه قبل",       "تغییر فروش نسبت به ماه قبل",    "fa", "MONTHLY_SALES_GROWTH_MOM"),
                ("000000000083", "رشد ماه به ماه فروش",              "رشد ماه به ماه فروش",            "fa", "MONTHLY_SALES_GROWTH_MOM"),
                ("000000000084", "MoM sales growth",                  "mom sales growth",                "en", "MONTHLY_SALES_GROWTH_MOM"),
                ("000000000085", "sales growth mom",                  "sales growth mom",                "en", "MONTHLY_SALES_GROWTH_MOM"),

                // ---- PE_TTM (additional to existing seed) ----
                ("000000000090", "نسبت قیمت به سود",                  "نسبت قیمت به سود",               "fa", "PE_TTM"),
                ("000000000091", "قیمت به سود",                       "قیمت به سود",                    "fa", "PE_TTM"),
                ("000000000092", "نسبت پی به ای",                     "نسبت پی به ای",                  "fa", "PE_TTM"),
                ("000000000093", "price to earnings",                  "price to earnings",               "en", "PE_TTM"),

                // ---- PS_TTM (additional to existing seed) ----
                ("000000000100", "نسبت قیمت به فروش",                  "نسبت قیمت به فروش",             "fa", "PS_TTM"),
                ("000000000101", "قیمت به فروش",                       "قیمت به فروش",                  "fa", "PS_TTM"),
                ("000000000102", "price to sales",                     "price to sales",                  "en", "PS_TTM"),

                // ---- Margin metrics ----
                ("000000000110", "حاشیه سود خالص",                    "حاشیه سود خالص",                 "fa", "NET_PROFIT_MARGIN"),
                ("000000000111", "مارجین خالص",                       "مارجین خالص",                    "fa", "NET_PROFIT_MARGIN"),
                ("000000000112", "نسبت سود خالص به فروش",             "نسبت سود خالص به فروش",          "fa", "NET_PROFIT_MARGIN"),
                ("000000000113", "net margin",                         "net margin",                      "en", "NET_PROFIT_MARGIN"),
                ("000000000120", "حاشیه سود ناخالص",                  "حاشیه سود ناخالص",               "fa", "GROSS_PROFIT_MARGIN"),
                ("000000000121", "مارجین ناخالص",                     "مارجین ناخالص",                  "fa", "GROSS_PROFIT_MARGIN"),
                ("000000000122", "gross margin",                       "gross margin",                    "en", "GROSS_PROFIT_MARGIN"),
                ("000000000130", "حاشیه سود عملیاتی",                 "حاشیه سود عملیاتی",              "fa", "OPERATING_PROFIT_MARGIN"),
                ("000000000131", "مارجین عملیاتی",                    "مارجین عملیاتی",                 "fa", "OPERATING_PROFIT_MARGIN"),
                ("000000000132", "operating margin",                   "operating margin",                "en", "OPERATING_PROFIT_MARGIN"),

                // ---- Revenue ----
                ("000000000140", "درآمد عملیاتی",                     "درآمد عملیاتی",                  "fa", "REVENUE"),
                ("000000000141", "درآمد فروش",                        "درآمد فروش",                     "fa", "REVENUE"),
                ("000000000142", "quarterly revenue",                  "quarterly revenue",               "en", "REVENUE"),

                // ---- Growth YoY (quarterly) ----
                ("000000000150", "رشد درآمد سالانه",                  "رشد درآمد سالانه",               "fa", "REVENUE_GROWTH_YOY"),
                ("000000000151", "رشد درآمد نسبت به سال قبل",         "رشد درآمد نسبت به سال قبل",      "fa", "REVENUE_GROWTH_YOY"),
                ("000000000160", "رشد سود ناخالص سالانه",             "رشد سود ناخالص سالانه",          "fa", "GROSS_PROFIT_GROWTH_YOY"),
                ("000000000161", "رشد سود ناخالص نسبت به سال قبل",    "رشد سود ناخالص نسبت به سال قبل", "fa", "GROSS_PROFIT_GROWTH_YOY"),
                ("000000000170", "رشد سود عملیاتی سالانه",            "رشد سود عملیاتی سالانه",         "fa", "OPERATING_PROFIT_GROWTH_YOY"),
                ("000000000171", "رشد سود خالص سالانه",               "رشد سود خالص سالانه",            "fa", "NET_PROFIT_GROWTH_YOY"),
                ("000000000172", "رشد سود خالص نسبت به سال قبل",      "رشد سود خالص نسبت به سال قبل",  "fa", "NET_PROFIT_GROWTH_YOY"),
                ("000000000180", "رشد سود هر سهم سالانه",             "رشد سود هر سهم سالانه",          "fa", "EPS_GROWTH_YOY"),
                ("000000000190", "رشد حقوق صاحبان سهام سالانه",       "رشد حقوق صاحبان سهام سالانه",   "fa", "EQUITY_GROWTH_YOY"),

                // ---- Growth QoQ (quarterly) ----
                ("000000000200", "رشد درآمد فصلی",                    "رشد درآمد فصلی",                 "fa", "REVENUE_GROWTH_QOQ"),
                ("000000000201", "رشد درآمد نسبت به فصل قبل",         "رشد درآمد نسبت به فصل قبل",      "fa", "REVENUE_GROWTH_QOQ"),
                ("000000000210", "رشد سود ناخالص فصلی",               "رشد سود ناخالص فصلی",            "fa", "GROSS_PROFIT_GROWTH_QOQ"),
                ("000000000220", "رشد سود عملیاتی فصلی",              "رشد سود عملیاتی فصلی",           "fa", "OPERATING_PROFIT_GROWTH_QOQ"),
                ("000000000221", "رشد سود عملیاتی نسبت به فصل قبل",  "رشد سود عملیاتی نسبت به فصل قبل","fa","OPERATING_PROFIT_GROWTH_QOQ"),
                ("000000000230", "رشد سود خالص فصلی",                 "رشد سود خالص فصلی",              "fa", "NET_PROFIT_GROWTH_QOQ"),
                ("000000000231", "رشد سود خالص نسبت به فصل قبل",      "رشد سود خالص نسبت به فصل قبل",  "fa", "NET_PROFIT_GROWTH_QOQ"),
                ("000000000240", "رشد سود هر سهم فصلی",               "رشد سود هر سهم فصلی",            "fa", "EPS_GROWTH_QOQ"),
                ("000000000241", "رشد سود نسبت به فصل قبل",           "رشد سود نسبت به فصل قبل",        "fa", "EPS_GROWTH_QOQ"),
                ("000000000250", "رشد حقوق صاحبان سهام فصلی",         "رشد حقوق صاحبان سهام فصلی",     "fa", "EQUITY_GROWTH_QOQ"),

                // ---- Fundamental metrics ----
                ("000000000260", "نسبت جاری",                          "نسبت جاری",                      "fa", "CURRENT_RATIO"),
                ("000000000261", "current ratio",                      "current ratio",                   "en", "CURRENT_RATIO"),
                ("000000000270", "نسبت بدهی به حقوق صاحبان سهام",    "نسبت بدهی به حقوق صاحبان سهام", "fa", "DEBT_TO_EQUITY"),
                ("000000000271", "debt to equity",                     "debt to equity",                  "en", "DEBT_TO_EQUITY"),
                ("000000000280", "سرمایه در گردش خالص",               "سرمایه در گردش خالص",            "fa", "NET_WORKING_CAPITAL"),
                ("000000000281", "net working capital",                "net working capital",             "en", "NET_WORKING_CAPITAL"),
                ("000000000290", "شاخص جامع نقدینگی",                  "شاخص جامع نقدینگی",               "fa", "COMPREHENSIVE_LIQUIDITY_INDEX"),
                ("000000000300", "نسبت دارایی جاری به کل دارایی",     "نسبت دارایی جاری به کل دارایی",  "fa", "CURRENT_ASSETS_TO_TOTAL_ASSETS"),
                ("000000000310", "گردش دارایی‌ها",                     "گردش دارایی ها",                 "fa", "ASSET_TURNOVER"),
                ("000000000311", "asset turnover",                     "asset turnover",                  "en", "ASSET_TURNOVER"),
                ("000000000320", "گردش دارایی‌های ثابت مشهود",        "گردش دارایی های ثابت مشهود",    "fa", "TANGIBLE_FIXED_ASSETS_TURNOVER"),
                ("000000000330", "دوره وصول مطالبات",                  "دوره وصول مطالبات",               "fa", "AVERAGE_COLLECTION_PERIOD"),
                ("000000000331", "average collection period",          "average collection period",       "en", "AVERAGE_COLLECTION_PERIOD"),

                // ---- Core profitability ----
                ("000000000340", "سود ناخالص",                         "سود ناخالص",                     "fa", "GROSS_PROFIT"),
                ("000000000341", "gross profit",                       "gross profit",                    "en", "GROSS_PROFIT"),
                ("000000000350", "سود عملیاتی",                        "سود عملیاتی",                    "fa", "OPERATING_PROFIT"),
                ("000000000351", "operating profit",                   "operating profit",                "en", "OPERATING_PROFIT"),
                ("000000000360", "سود خالص",                           "سود خالص",                       "fa", "NET_PROFIT"),
                ("000000000361", "net profit",                         "net profit",                      "en", "NET_PROFIT"),
                ("000000000370", "سود قبل از بهره و مالیات",          "سود قبل از بهره و مالیات",       "fa", "EBIT"),
                ("000000000371", "ebit",                               "ebit",                            "en", "EBIT"),
            };

            foreach (var (suffix, expr, norm, lang, code) in aliases)
            {
                var id = Guid.Parse($"22222222-2222-2222-2222-{suffix}");
                migrationBuilder.InsertData(
                    table: "DynamicMetricAliases",
                    columns: new[] { "Id", "Expression", "NormalizedExpression", "Language", "MetricCode",
                                     "MetricVersion", "Source", "Status", "ConfidenceScore", "FrequencyCount",
                                     "CreatedAt", "CreatedBy" },
                    values: new object[] { id, expr, norm, lang, code,
                                           v1, manualSeed, active, conf, 0,
                                           seedAt, "seed-074" });
            }

            // ---------------------------------------------------------------
            // 5. Seed MetricPeriodAliases
            // ---------------------------------------------------------------
            // Priority: longer/more-specific phrases get higher values so they win longest-match
            var periodAliases = new (string Id, string Text, string Norm, string Lang, string PType, string PSel, int Pri)[]
            {
                // Monthly periods (fa)
                ("33333333-3333-3333-3333-000000000001", "آخرین ماه",                    "آخرین ماه",                  "fa", "Monthly",     "M0",     100),
                ("33333333-3333-3333-3333-000000000002", "ماه جاری",                     "ماه جاری",                   "fa", "Monthly",     "M0",     100),
                ("33333333-3333-3333-3333-000000000003", "ماه قبل",                      "ماه قبل",                    "fa", "Monthly",     "M1",     110),
                ("33333333-3333-3333-3333-000000000004", "ماه گذشته",                    "ماه گذشته",                  "fa", "Monthly",     "M1",     110),
                ("33333333-3333-3333-3333-000000000005", "ماه مشابه سال قبل",            "ماه مشابه سال قبل",          "fa", "Monthly",     "M12",    130),
                ("33333333-3333-3333-3333-000000000006", "مدت مشابه سال قبل",            "مدت مشابه سال قبل",          "fa", "Monthly",     "M12",    130),
                ("33333333-3333-3333-3333-000000000007", "پارسال",                        "پارسال",                     "fa", "Monthly",     "M12",    90),
                // Monthly periods (en)
                ("33333333-3333-3333-3333-000000000011", "latest month",                  "latest month",               "en", "Monthly",     "M0",     100),
                ("33333333-3333-3333-3333-000000000012", "previous month",                "previous month",             "en", "Monthly",     "M1",     110),
                ("33333333-3333-3333-3333-000000000013", "same month last year",          "same month last year",       "en", "Monthly",     "M12",    130),
                ("33333333-3333-3333-3333-000000000014", "yoy",                           "yoy",                        "en", "Monthly",     "M12",    80),
                ("33333333-3333-3333-3333-000000000015", "mom",                           "mom",                        "en", "Monthly",     "M1",     80),
                // Quarterly periods (fa)
                ("33333333-3333-3333-3333-000000000020", "آخرین فصل",                    "آخرین فصل",                  "fa", "ThreeMonths", "Q0",     100),
                ("33333333-3333-3333-3333-000000000021", "فصل جاری",                     "فصل جاری",                   "fa", "ThreeMonths", "Q0",     100),
                ("33333333-3333-3333-3333-000000000022", "فصل قبل",                      "فصل قبل",                    "fa", "ThreeMonths", "Q1",     110),
                ("33333333-3333-3333-3333-000000000023", "فصل گذشته",                    "فصل گذشته",                  "fa", "ThreeMonths", "Q1",     110),
                ("33333333-3333-3333-3333-000000000024", "فصل مشابه سال قبل",            "فصل مشابه سال قبل",          "fa", "ThreeMonths", "Q4",     130),
                ("33333333-3333-3333-3333-000000000025", "دوره مشابه سال قبل",           "دوره مشابه سال قبل",         "fa", "ThreeMonths", "Q4",     130),
                // Quarterly periods (en)
                ("33333333-3333-3333-3333-000000000030", "latest quarter",                "latest quarter",             "en", "ThreeMonths", "Q0",     100),
                ("33333333-3333-3333-3333-000000000031", "previous quarter",              "previous quarter",           "en", "ThreeMonths", "Q1",     110),
                ("33333333-3333-3333-3333-000000000032", "same quarter last year",        "same quarter last year",     "en", "ThreeMonths", "Q4",     130),
                ("33333333-3333-3333-3333-000000000033", "qoq",                           "qoq",                        "en", "ThreeMonths", "Q1",     80),
                // Duration periods (fa)
                ("33333333-3333-3333-3333-000000000040", "سه ماهه",                      "سه ماهه",                    "fa", "ThreeMonths", "Latest", 70),
                ("33333333-3333-3333-3333-000000000041", "شش ماهه",                      "شش ماهه",                    "fa", "SixMonths",   "Latest", 70),
                ("33333333-3333-3333-3333-000000000042", "نه ماهه",                      "نه ماهه",                    "fa", "NineMonths",  "Latest", 70),
                ("33333333-3333-3333-3333-000000000043", "دوازده ماهه",                  "دوازده ماهه",                "fa", "TwelveMonths","Latest", 70),
                // Duration periods (en)
                ("33333333-3333-3333-3333-000000000050", "three month",                   "three month",                "en", "ThreeMonths", "Latest", 70),
                ("33333333-3333-3333-3333-000000000051", "six month",                     "six month",                  "en", "SixMonths",   "Latest", 70),
                ("33333333-3333-3333-3333-000000000052", "nine month",                    "nine month",                 "en", "NineMonths",  "Latest", 70),
                ("33333333-3333-3333-3333-000000000053", "twelve month",                  "twelve month",               "en", "TwelveMonths","Latest", 70),
            };

            foreach (var (id, text, norm, lang, ptype, psel, pri) in periodAliases)
            {
                migrationBuilder.InsertData(
                    table: "MetricPeriodAliases",
                    columns: new[] { "Id", "AliasText", "NormalizedAliasText", "Language",
                                     "PeriodType", "PeriodSelector", "Priority", "Status" },
                    values: new object[] { Guid.Parse(id), text, norm, lang, ptype, psel, pri, "Active" });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MetricPeriodAliases");

            migrationBuilder.DropColumn(name: "PersianTitle",            table: "FinancialMetricDefinitions");
            migrationBuilder.DropColumn(name: "LookupEligible",          table: "FinancialMetricDefinitions");
            migrationBuilder.DropColumn(name: "ScannerEligible",         table: "FinancialMetricDefinitions");
            migrationBuilder.DropColumn(name: "IsMonthlyActivityMetric", table: "FinancialMetricDefinitions");
            migrationBuilder.DropColumn(name: "IsValuationMetric",       table: "FinancialMetricDefinitions");
            migrationBuilder.DropColumn(name: "IsGrowthMetric",          table: "FinancialMetricDefinitions");
            migrationBuilder.DropColumn(name: "IsMarginMetric",          table: "FinancialMetricDefinitions");
            migrationBuilder.DropColumn(name: "IsFundamentalMetric",     table: "FinancialMetricDefinitions");
            migrationBuilder.DropColumn(name: "SuppressQuoteContext",    table: "FinancialMetricDefinitions");

            // Remove seeded DynamicMetricAliases from this migration
            migrationBuilder.Sql("""
                DELETE FROM "DynamicMetricAliases"
                WHERE "CreatedBy" = 'seed-074';
                """);
        }
    }
}
