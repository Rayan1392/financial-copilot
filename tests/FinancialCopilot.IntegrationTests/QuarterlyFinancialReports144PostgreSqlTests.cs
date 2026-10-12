using FinancialCopilot.Application.FinancialData.Ingestion;
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
        var sourceIds = QuarterlyFinancialReportsNormalizer.ApprovedMappings.ToDictionary(
            mapping => mapping.SourceItemId, mapping => Guid.Parse(mapping.CatalogId));
        var now = DateTimeOffset.UtcNow;

        await using (var db = database.CreateContext())
        {
            foreach (var mapping in QuarterlyFinancialReportsNormalizer.ApprovedMappings)
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
        Assert.Equal(16, result.Rows.Count);
        Assert.Equal(5, result.PeriodsNewestFirst.Count);
        Assert.NotNull(renderedTable);
        Assert.NotNull(renderedTable!.QuarterlyResult);
        Assert.Equal(16, renderedTable.QuarterlyResult!.Rows.Count);
        Assert.Contains("### جدول گزارش فصلی چخزر", renderedTable.RenderedAnswer);
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
        string revision, DateTimeOffset synchronizedAt) => new()
    {
        Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = company,
        ExternalStatementId = id, StatementType = "IncomeStatement", PeriodType = period,
        PeriodStart = start, PeriodEnd = end, SourcePayloadChecksum = $"checksum-{id}",
        LastSynchronizedAt = synchronizedAt, IsComposing = composing,
        WarningsJson = $"[{{\"assumedScale\":\"MillionRials\",\"sourceAmountUnit\":\"million_rial\",\"currencyCode\":\"IRR\",\"accountingBasis\":\"Accrual\",\"reportRevisionLineage\":\"{revision}\"}}]"
    };

    private static NormalizedFinancialStatementLineItemRow Line(Guid reportId, Guid catalogId, string? metricCode, decimal? value) => new()
    {
        Id = Guid.NewGuid(), FinancialStatementId = reportId, SourceItemCatalogId = catalogId,
        MetricCode = metricCode, Value = value
    };
}
