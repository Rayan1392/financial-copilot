using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class QuarterlyFinancialReports144PostgreSqlTests(FinancialStatementValueSearch131PostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task Repository_IsolatesProviderCompanyStatementAndScope_AndPreservesZeroAndProvenance()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var sourceIds = QuarterlyFinancialReportsNormalizer.RequiredMappings.ToDictionary(
            mapping => mapping.SourceItemId, mapping => Guid.Parse(mapping.CatalogId));
        var now = DateTimeOffset.UtcNow;

        await using (var db = database.CreateContext())
        {
            foreach (var mapping in QuarterlyFinancialReportsNormalizer.RequiredMappings)
            {
                db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow
                {
                    Id = Guid.Parse(mapping.CatalogId), ProviderName = "NoavaranCurrentApi",
                    StatementType = "IncomeStatement", SourceItemId = mapping.SourceItemId,
                    TitleFa = $"catalog-{mapping.SourceItemId}", Unit = "million_rial", LastSynchronizedAt = now
                });
            }

            var ytd = new[]
            {
                Report("company-144", "q1", "ThreeMonths", new(2025, 3, 20), new(2025, 6, 20), false, "r1", now),
                Report("company-144", "q2", "SixMonths", new(2025, 3, 20), new(2025, 9, 20), false, "r1", now),
                Report("company-144", "q3", "NineMonths", new(2025, 3, 20), new(2025, 12, 20), false, "r1", now),
                Report("company-144", "q4", "TwelveMonths", new(2025, 3, 20), new(2026, 3, 20), false, "r1", now),
                Report("company-144", "q2-consolidated", "SixMonths", new(2025, 3, 20), new(2025, 9, 20), true, "r1", now),
                Report("other-company", "other-company", "SixMonths", new(2025, 3, 20), new(2025, 9, 20), false, "r1", now),
                Report("company-144", "wrong-type", "SixMonths", new(2025, 3, 20), new(2025, 9, 20), false, "r1", now),
                Report("company-144", "wrong-provider", "SixMonths", new(2025, 3, 20), new(2025, 9, 20), false, "r1", now),
                Report("company-144", "q2-revision", "SixMonths", new(2025, 3, 20), new(2025, 9, 20), false, "r2", now)
            };
            ytd[6].StatementType = "BalanceSheet";
            ytd[7].ProviderName = "OtherProvider";
            db.FinancialStatements.AddRange(ytd);
            db.FinancialStatementLineItems.AddRange(
                Line(ytd[0].Id, sourceIds[15], null, 0m),
                Line(ytd[1].Id, sourceIds[15], null, 100m),
                Line(ytd[2].Id, sourceIds[15], "REVENUE", 250m),
                Line(ytd[3].Id, sourceIds[15], "REVENUE", 400m),
                Line(ytd[4].Id, sourceIds[15], "REVENUE", 111m),
                Line(ytd[5].Id, sourceIds[15], "REVENUE", 112m),
                Line(ytd[6].Id, sourceIds[15], "REVENUE", 113m),
                Line(ytd[7].Id, sourceIds[15], "REVENUE", 114m),
                Line(ytd[8].Id, sourceIds[15], "REVENUE", 120m));
            await db.SaveChangesAsync();
        }

        var apiConfigPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/backend/FinancialCopilot.API"));
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiConfigPath)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:FinancialCopilot"] = database.ConnectionString
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddFinancialCopilotInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IQuarterlyFinancialReportsRepository>();
        var reports = await repository.GetIncomeStatementReportsAsync(new("company-144", 5, null, false));

        Assert.Equal(5, reports.Count);
        Assert.All(reports, report =>
        {
            Assert.Equal("NoavaranCurrentApi", report.ProviderName);
            Assert.Equal("company-144", report.ExternalCompanyId);
            Assert.Equal("IncomeStatement", report.StatementType);
            Assert.False(report.IsComposing);
            Assert.Equal("MillionRials", report.AssumedScale);
            Assert.Equal("IRR", report.CurrencyCode);
            Assert.Equal("Accrual", report.AccountingBasis);
            Assert.StartsWith("r", report.ReportRevisionLineage);
        });
        Assert.Equal([12, 9, 6, 6, 3], reports.Select(report => report.PeriodMonths));
        var zero = Assert.Single(reports.Single(report => report.ExternalReportId == "q1").Items);
        Assert.Equal(15, zero.SourceItemId);
        Assert.Equal(sourceIds[15], zero.CatalogId);
        Assert.Equal("million_rial", zero.Unit);
        Assert.Single(await repository.GetIncomeStatementReportsAsync(new("company-144", 5, null, true)));
        Assert.Contains(reports, report => report.ExternalReportId == "q2-revision");
        Assert.Equal(0m, zero.Value);
        Assert.NotEqual(Guid.Empty, zero.LineItemId);
        Assert.Contains(reports, report => report.ExternalReportId == "q1" &&
            report.SourcePayloadChecksum == "checksum-q1" && report.Items.Single().LineItemId == zero.LineItemId);
        var withReferenceWindow = await repository.GetIncomeStatementReportsAsync(new("company-144", 5,
            new DateOnly(2025, 9, 20), false));
        Assert.Contains(withReferenceWindow, report => report.ExternalReportId == "q4" &&
            report.PeriodEnd > new DateOnly(2025, 9, 20));
    }

    [SkippableFact]
    public async Task NonEsfandFiscalCalendar_MapsAndCalculatesFiveConsecutiveQuarters()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var sourceIds = QuarterlyFinancialReportsNormalizer.RequiredMappings
            .Where(mapping => mapping.SourceItemId is 15 or 139 or 160 or 153 or 144)
            .ToDictionary(mapping => mapping.SourceItemId, mapping => Guid.Parse(mapping.CatalogId));
        var now = DateTimeOffset.UtcNow;
        var fy1404Start = JalaliDate(1403, 10, 1);
        var fy1405Start = JalaliDate(1404, 10, 1);

        await using (var db = database.CreateContext())
        {
            db.FinancialStatementSourceItems.AddRange(sourceIds.Select(pair => new FinancialStatementSourceItemCatalogRow
            {
                Id = pair.Value, ProviderName = "NoavaranCurrentApi", StatementType = "IncomeStatement",
                SourceItemId = pair.Key, TitleFa = $"item-{pair.Key}",
                Unit = pair.Key switch
                {
                    153 => "thousand_shares",
                    160 or 317 => "rial_per_share",
                    _ => "million_rial"
                },
                LastSynchronizedAt = now
            }));
            var reports = new[]
            {
                Report("company-144", "fy1404-q1", "ThreeMonths", fy1404Start, JalaliDate(1403, 12, 29), false, "r1", now, "1404/09/30"),
                Report("company-144", "fy1404-q2", "SixMonths", fy1404Start, JalaliDate(1404, 3, 31), false, "r1", now, "1404/09/30"),
                Report("company-144", "fy1404-q3", "NineMonths", fy1404Start, JalaliDate(1404, 6, 31), false, "r1", now, "1404/09/30"),
                Report("company-144", "fy1404-q4", "TwelveMonths", fy1404Start, JalaliDate(1404, 9, 30), false, "r1", now, "1404/09/30"),
                Report("company-144", "fy1405-q1", "ThreeMonths", fy1405Start, JalaliDate(1404, 12, 29), false, "r1", now, "1405/09/30"),
                Report("company-144", "fy1405-q2", "SixMonths", fy1405Start, JalaliDate(1405, 3, 31), false, "r1", now, "1405/09/30"),
                Report("company-144", "fy1405-q2-audited", "SixMonths", fy1405Start, JalaliDate(1405, 3, 31), false, "r2", now, "1405/09/30")
            };
            reports[^1].IsAudited = true;
            db.FinancialStatements.AddRange(reports);
            var cumulative = new[]
            {
                (Revenue: 100m, GrossProfit: 40m, Eps: 10m, Shares: 200_000m),
                (Revenue: 250m, GrossProfit: 100m, Eps: 25m, Shares: 210_000m),
                (Revenue: 400m, GrossProfit: 160m, Eps: 40m, Shares: 220_000m),
                (Revenue: 600m, GrossProfit: 240m, Eps: 60m, Shares: 230_000m),
                (Revenue: 80m, GrossProfit: 30m, Eps: 8m, Shares: 240_000m),
                (Revenue: 190m, GrossProfit: 75m, Eps: 18m, Shares: 245_000m),
                (Revenue: 200m, GrossProfit: 80m, Eps: 20m, Shares: 250_000m)
            };
            db.FinancialStatementLineItems.AddRange(reports.SelectMany((report, index) => new[]
            {
                Line(report.Id, sourceIds[15], "REVENUE", cumulative[index].Revenue),
                Line(report.Id, sourceIds[139], "GROSS_PROFIT", cumulative[index].GrossProfit),
                Line(report.Id, sourceIds[160], "EPS", cumulative[index].Eps),
                Line(report.Id, sourceIds[153], "SHARES", cumulative[index].Shares),
                Line(report.Id, sourceIds[144], "ATTRIBUTABLE_PROFIT", cumulative[index].Eps * cumulative[index].Shares / 1000m)
            }));
            await db.SaveChangesAsync();
        }

        var apiConfigPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/backend/FinancialCopilot.API"));
        var configuration = new ConfigurationBuilder().SetBasePath(apiConfigPath)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:FinancialCopilot"] = database.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddFinancialCopilotInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IQuarterlyFinancialReportsRepository>();
        var reportsFromStore = await repository.GetIncomeStatementReportsAsync(new("company-144", 5, null, false));

        Assert.Equal(7, reportsFromStore.Count);
        Assert.Contains(reportsFromStore, report => report.FiscalYearEvidence == "1405/09/30" &&
            report.PeriodEnd == JalaliDate(1404, 12, 29));
        var result = QuarterlyFinancialReportsNormalizer.Normalize("دتولید", "fixture", "company-144",
            reportsFromStore, 5, null, now);

        Assert.Equal("Available", result.Status);
        Assert.Equal(new[] { (1405, 2), (1405, 1), (1404, 4), (1404, 3), (1404, 2) },
            result.PeriodsNewestFirst.Select(period => (period.FiscalYear, period.Quarter)));
        Assert.Equal(new[]
        {
            JalaliDate(1405, 3, 31), JalaliDate(1404, 12, 29), JalaliDate(1404, 9, 30),
            JalaliDate(1404, 6, 31), JalaliDate(1404, 3, 31)
        }, result.PeriodsNewestFirst.Select(period => period.End));
        var revenue = Assert.Single(result.Rows, row => row.SourceItemId == 15);
        Assert.Equal(120m, revenue.Cells["1405 Q2"].Value);
        Assert.Equal(80m, revenue.Cells["1405 Q1"].Value);
        Assert.Equal(200m, revenue.Cells["1404 Q4"].Value);
        Assert.Equal(150m, revenue.Cells["1404 Q3"].Value);
        Assert.Equal(150m, revenue.Cells["1404 Q2"].Value);
        Assert.Equal(12m, Assert.Single(result.Rows, row => row.SourceItemId == 160).Cells["1405 Q2"].Value);
        Assert.Equal(250_000m, Assert.Single(result.Rows, row => row.SourceItemId == 153).Cells["1405 Q2"].Value);
        Assert.Equal(50m / 120m * 100m,
            Assert.Single(result.Rows, row => row.RowId == "gross_margin").Cells["1405 Q2"].Value);
        Assert.Contains(result.SelectedReports!, report => report.ExternalReportId == "fy1405-q2-audited");
        Assert.DoesNotContain(result.SelectedReports!, report => report.ExternalReportId == "fy1405-q2");
    }

    [SkippableFact]
    public async Task DevelopmentDatabase_ReadOnly_DetolidInventoryAndExactQuarterlyRequest()
    {
        var configured = Environment.GetEnvironmentVariable("FINANCIAL_COPILOT_DEV_READONLY_CONNECTION_STRING");
        Skip.If(string.IsNullOrWhiteSpace(configured),
            "Set FINANCIAL_COPILOT_DEV_READONLY_CONNECTION_STRING to the local Development database connection.");
        var connection = new NpgsqlConnectionStringBuilder(configured)
        {
            Pooling = false,
            Options = "-c default_transaction_read_only=on"
        };
        Skip.If(connection.Host is not ("localhost" or "127.0.0.1" or "::1") ||
                !string.Equals(connection.Database, "financial_copilot", StringComparison.OrdinalIgnoreCase),
            "The Feature 144 smoke test only accepts the local financial_copilot Development database.");

        var apiConfigPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/backend/FinancialCopilot.API"));
        var configuration = new ConfigurationBuilder().SetBasePath(apiConfigPath)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FinancialCopilot"] = connection.ConnectionString
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddFinancialCopilotInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        const string request = "گزارشات فصلی دتولید";
        var parsedQuery = FinancialStatementTableIntentRules.BuildQuarterlyQuery(request);
        var useCase = scope.ServiceProvider.GetRequiredService<IFinancialStatementTableQueryUseCase>();
        var table = await useCase.ExecuteAsync(parsedQuery);
        var result = table?.QuarterlyResult;
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        var inventory = result is null ? [] : await db.FinancialStatements.AsNoTracking()
            .Where(report => report.ExternalCompanyId == result.ExternalCompanyId &&
                             report.ProviderName == "NoavaranCurrentApi" &&
                             report.StatementType == "IncomeStatement")
            .OrderByDescending(report => report.PeriodEnd)
            .Select(report => new
            {
                report.Id, report.ExternalStatementId, report.PeriodType, report.PeriodStart, report.PeriodEnd,
                report.IsAudited, report.IsRepresented, report.IsComposing, report.WarningsJson
            }).ToListAsync();
        var outputDirectory = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")),
            "specs/144-quarterly-financial-reports-table");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "detolid-development-evidence.json"),
            JsonSerializer.Serialize(new { request, parsedQuery, inventory, result, renderedAnswer = table?.RenderedAnswer },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        Assert.NotNull(result);
        Assert.Equal("دتولید", result!.CompanySymbol);
    }

    [SkippableFact]
    public async Task DevelopmentDatabase_ReadOnlyActualUseCaseSmoke_ForChakhzar()
    {
        var configured = Environment.GetEnvironmentVariable("FINANCIAL_COPILOT_DEV_READONLY_CONNECTION_STRING");
        Skip.If(string.IsNullOrWhiteSpace(configured),
            "Set FINANCIAL_COPILOT_DEV_READONLY_CONNECTION_STRING to the authorized local Development database.");
        var connection = new NpgsqlConnectionStringBuilder(configured)
        {
            Pooling = false,
            Options = "-c default_transaction_read_only=on"
        };
        Skip.If(connection.Host is not ("localhost" or "127.0.0.1" or "::1") ||
                !string.Equals(connection.Database, "financial_copilot", StringComparison.OrdinalIgnoreCase),
            "The Feature 144 smoke test only accepts the local financial_copilot Development database.");

        var apiConfigPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/backend/FinancialCopilot.API"));
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiConfigPath)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FinancialCopilot"] = connection.ConnectionString
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddFinancialCopilotInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IQuarterlyFinancialReportsQueryUseCase>();
        var result = await useCase.ExecuteAsync(new QuarterlyFinancialReportTableQuery(
            string.Empty, "چخزر", QuarterCount: 5, IsComposing: false,
            ProviderName: "NoavaranCurrentApi"));

        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        var persistedCompanyReports = await db.FinancialStatements.AsNoTracking()
            .Where(report => report.ExternalCompanyId == result.ExternalCompanyId &&
                             report.ProviderName == "NoavaranCurrentApi" &&
                             report.StatementType == "IncomeStatement" &&
                             (report.PeriodType == "ThreeMonths" || report.PeriodType == "SixMonths" ||
                              report.PeriodType == "NineMonths" || report.PeriodType == "TwelveMonths"))
            .OrderByDescending(report => report.PeriodEnd)
            .Select(report => new
            {
                report.Id, report.ExternalStatementId, report.ProviderName, report.StatementType,
                report.PeriodType, report.PeriodStart, report.PeriodEnd, report.IsComposing,
                report.IsAudited, report.IsRepresented, report.SourcePayloadChecksum, report.WarningsJson
            }).ToListAsync();
        var catalogEvidence = await db.FinancialStatementSourceItems.AsNoTracking()
            .Where(item => item.ProviderName == "NoavaranCurrentApi" && item.StatementType == "IncomeStatement")
            .Where(item => QuarterlyFinancialReportsNormalizer.ApprovedMappings.Select(mapping => mapping.SourceItemId)
                .Contains(item.SourceItemId))
            .OrderBy(item => item.SourceItemId)
            .Select(item => new { item.Id, item.SourceItemId, item.TitleFa, item.Unit })
            .ToListAsync();
        var selectedReportIds = result.SelectedReports?.Select(report => report.ReportId).ToArray() ?? [];
        var sourceObservations = await (
            from line in db.FinancialStatementLineItems.AsNoTracking()
            join catalog in db.FinancialStatementSourceItems.AsNoTracking()
                on line.SourceItemCatalogId equals (Guid?)catalog.Id
            where selectedReportIds.Contains(line.FinancialStatementId)
            orderby line.FinancialStatementId, catalog.SourceItemId
            select new
            {
                ReportId = line.FinancialStatementId, LineItemId = line.Id, catalog.Id,
                catalog.SourceItemId, line.MetricCode, catalog.Unit, line.Value
            }).ToListAsync();
        var renderedTable = await scope.ServiceProvider.GetRequiredService<IFinancialStatementTableQueryUseCase>()
            .ExecuteAsync(new FinancialStatementTableQuery("quarterly smoke", "چخزر",
                FinancialStatementType.IncomeStatement, IsComposing: false,
                UseQuarterlyReportTable: true, QuarterCount: 5));

        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var outputDirectory = Path.Combine(repositoryRoot, "specs/144-quarterly-financial-reports-table");
        var output = Path.Combine(outputDirectory, "real-data-validation-result.json");
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "real-data-database-evidence.json"),
            JsonSerializer.Serialize(new { persistedCompanyReports, catalogEvidence, sourceObservations }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "real-data-rendered-answer.md"),
            renderedTable?.RenderedAnswer ?? "The backend financial statement table use case returned no rendered answer.");
        Assert.Equal("چخزر", result.CompanySymbol);
        Assert.Equal("NoavaranCurrentApi", result.ProviderName);
        Assert.Equal("IncomeStatement", result.StatementType);
        Assert.Equal("Available", result.Status);
        Assert.False(result.IsComposing);
        Assert.Equal(15, result.Rows.Count);
        Assert.Equal(5, result.PeriodsNewestFirst.Count);
        Assert.NotNull(renderedTable);
        Assert.NotNull(renderedTable!.QuarterlyResult);
        Assert.Equal(15, renderedTable.QuarterlyResult!.Rows.Count);
        Assert.StartsWith("### ", renderedTable.RenderedAnswer);
        Assert.Contains("میلیون ریال", renderedTable.RenderedAnswer);
        var markdownRows = renderedTable.RenderedAnswer!.Split('\n').Where(line => line.StartsWith('|')).ToArray();
        Assert.Equal(18, markdownRows.Length);
        var zeroRow = markdownRows.Single(line => line.Contains("هزینه‌های مالی"));
        var zeroColumns = zeroRow.Split('|').Select(value => value.Trim()).ToArray();
        Assert.Equal("-", zeroColumns[3]);
        var negativeRow = markdownRows.Single(line => line.Contains("بهای تمام‌شده"));
        Assert.Contains("(", negativeRow);
        Assert.Contains("−", negativeRow);
    }

    private static NormalizedFinancialStatementRow Report(
        string company, string id, string period, DateOnly start, DateOnly end, bool composing,
        string revision, DateTimeOffset synchronizedAt, string? fiscalYearEnd = null) => new()
    {
        Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = company,
        ExternalStatementId = id, StatementType = "IncomeStatement", PeriodType = period,
        PeriodStart = start, PeriodEnd = end, SourcePayloadChecksum = $"checksum-{id}",
        LastSynchronizedAt = synchronizedAt, IsComposing = composing,
        WarningsJson = $"[{{\"assumedScale\":\"MillionRials\",\"sourceAmountUnit\":\"million_rial\",\"currencyCode\":\"IRR\",\"accountingBasis\":\"Accrual\",\"reportRevisionLineage\":\"{revision}\",\"jalaliFiscalYearEnd\":\"{fiscalYearEnd}\"}}]"
    };

    private static DateOnly JalaliDate(int year, int month, int day) =>
        DateOnly.FromDateTime(new System.Globalization.PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0));

    private static NormalizedFinancialStatementLineItemRow Line(Guid reportId, Guid catalogId, string? metricCode, decimal? value) => new()
    {
        Id = Guid.NewGuid(), FinancialStatementId = reportId, SourceItemCatalogId = catalogId,
        MetricCode = metricCode, Value = value
    };
}
