using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Semantics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class MonthlyStructuredQueryPostgreSqlTests(FinancialStatementValueSearch131PostgreSqlFixture fixture)
{
    private const string Provider = "NoavaranCurrentApi";
    // Fixture product names: they choose a line's source title (see FixtureTitle). The persisted ProductCode is
    // production-like noise (see Line) and never a product identity (owner decision 2026-10-29).
    private const string ProductCode = "PRODUCT:MONTHLY-FIXTURE:PELLET";
    private const string AmbiguousProductCode = "PRODUCT:MONTHLY-FIXTURE:CEMENT";
    private static readonly Guid TenantId = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");

    [SkippableFact]
    public async Task CompiledMonthlyRetrievalAndTimeSeriesExecuteThroughRestrictedReadOnlyExecutor()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var databaseName = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database
            ?? throw new InvalidOperationException("The isolated PostgreSQL test database has no name.");
        var companyId = Guid.NewGuid();
            var companyId2 = Guid.NewGuid();
        var kgelId = Guid.NewGuid();
        var kgehrId = Guid.NewGuid();
        var newlyAddedProducerId = Guid.NewGuid();
        var zeroProductionCompanyId = Guid.NewGuid();
        var productionOnlyCompanyId = Guid.NewGuid();
        var unauthorizedProducerId = Guid.NewGuid();
        var marketId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Markets.Add(new NormalizedMarketRow
            {
                Id = marketId, ProviderName = Provider, ExternalId = "feature141-monthly-fixture",
                Name = "Feature 141 fixture", LastSynchronizedAt = DateTimeOffset.UtcNow
            });
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = companyId, ProviderName = Provider, ExternalCompanyId = "monthly-fixture-company",
                Name = "Monthly fixture company", Ticker = "MTH", CompanySymbol = "MTH", MarketId = marketId, PrecedencyRight = 0,
                LastSynchronizedAt = DateTimeOffset.UtcNow
            });
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = companyId2, ProviderName = Provider, ExternalCompanyId = "monthly-fixture-company-2",
                Name = "Monthly fixture company 2", Ticker = "MTH2", CompanySymbol = "MTH2", MarketId = marketId, PrecedencyRight = 0,
                LastSynchronizedAt = DateTimeOffset.UtcNow
            });
            db.Companies.AddRange(
                new NormalizedCompanyRow { Id = kgelId, ProviderName = Provider, ExternalCompanyId = "feature141-kgel",
                    Name = "Fixture Kegel", Ticker = "کگل", CompanySymbol = "کگل", MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = DateTimeOffset.UtcNow },
                new NormalizedCompanyRow { Id = kgehrId, ProviderName = Provider, ExternalCompanyId = "feature141-kgehr",
                    Name = "Fixture Kogهر", Ticker = "کگهر", CompanySymbol = "کگهر", MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = DateTimeOffset.UtcNow });
            db.Companies.AddRange(
                new NormalizedCompanyRow { Id = newlyAddedProducerId, ProviderName = Provider, ExternalCompanyId = "feature141-new-producer",
                    Name = "Dynamically discovered producer", Ticker = "NEWP", CompanySymbol = "NEWP", MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = DateTimeOffset.UtcNow },
                new NormalizedCompanyRow { Id = zeroProductionCompanyId, ProviderName = Provider, ExternalCompanyId = "feature141-zero-producer",
                    Name = "No positive pellet production", Ticker = "ZERO", CompanySymbol = "ZERO", MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = DateTimeOffset.UtcNow },
                new NormalizedCompanyRow { Id = productionOnlyCompanyId, ProviderName = Provider, ExternalCompanyId = "feature141-production-only",
                    Name = "Positive pellet producer without sales", Ticker = "NOSALES", CompanySymbol = "NOSALES", MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = DateTimeOffset.UtcNow },
                new NormalizedCompanyRow { Id = unauthorizedProducerId, ProviderName = Provider, ExternalCompanyId = "feature141-unauthorized-producer",
                    Name = "Unauthorized pellet producer", Ticker = "UNAUTH", CompanySymbol = "UNAUTH", MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = DateTimeOffset.UtcNow });
            var month1 = Report(companyId, "2026-03", new(2026, 3, 21), new(2026, 4, 20), "accepted-march", true, "Accepted", 0);
            var otherCompanyMonth1 = Report(companyId2, "company-2-2026-03", new(2026, 3, 21), new(2026, 4, 20),
                "accepted-company2-march", true, "Accepted", 0, "monthly-fixture-company-2");
            var month2 = Report(companyId, "2026-04", new(2026, 4, 21), new(2026, 5, 21), "accepted-april", true, "Accepted", 0);
            var company2June = Report(companyId2, "company-2-2026-06", new(2026, 6, 21), new(2026, 7, 20),
                "accepted-company2-june", true, "Accepted", 0, "monthly-fixture-company-2");
            var rejectedRevision = Report(companyId, "2026-04-rejected", new(2026, 4, 21), new(2026, 5, 21),
                "rejected-april", false, "RejectedOlder", 0);
            var cumulative = Report(companyId, "2026-04-ytd", new(2026, 1, 1), new(2026, 5, 21),
                "cumulative-april", true, "Accepted", 1);
            // A second accepted report of the same company identity for the same month (IX_MonthlyReports_LogicalPeriod
            // allows it only with a different PeriodStart): two source rows for one company/period/product.
            var acceptedAlias = Report(companyId, "2026-04-alias", new(2026, 4, 22), new(2026, 5, 21),
                "accepted-april-alias", true, "Accepted", 0);
            var foreignProvider = Report(companyId, "2026-03-other-provider", new(2026, 3, 21), new(2026, 4, 20),
                "foreign-provider-march", true, "Accepted", 0, providerName: "OtherMonthlyProvider");
            var shahrivarKgel = Report(kgelId, "kgel-shahrivar-1405", new(2026, 8, 23), new(2026, 9, 22),
                "kgel-shahrivar-1405", true, "Accepted", 0, "feature141-kgel");
            var kgelYearAgo = Report(kgelId, "kgel-shahrivar-1404", new(2025, 8, 23), new(2025, 9, 22),
                "kgel-shahrivar-1404", true, "Accepted", 0, "feature141-kgel");
            var shahrivarKgehr = Report(kgehrId, "kgehr-shahrivar-1405", new(2026, 8, 23), new(2026, 9, 22),
                "kgehr-shahrivar-1405", true, "Accepted", 0, "feature141-kgehr");
            var newlyAddedProducerReport = Report(newlyAddedProducerId, "new-producer-shahrivar-1405", new(2026, 8, 23), new(2026, 9, 22),
                "new-producer-shahrivar-1405", true, "Accepted", 0, "feature141-new-producer");
            var zeroProductionReport = Report(zeroProductionCompanyId, "zero-producer-shahrivar-1405", new(2026, 8, 23), new(2026, 9, 22),
                "zero-producer-shahrivar-1405", true, "Accepted", 0, "feature141-zero-producer");
            var productionOnlyReport = Report(productionOnlyCompanyId, "production-only-shahrivar-1405", new(2026, 8, 23), new(2026, 9, 22),
                "production-only-shahrivar-1405", true, "Accepted", 0, "feature141-production-only");
            var unauthorizedProducerReport = Report(unauthorizedProducerId, "unauthorized-producer-shahrivar-1405", new(2026, 8, 23), new(2026, 9, 22),
                "unauthorized-producer-shahrivar-1405", true, "Accepted", 0, "feature141-unauthorized-producer");
            var kgehrJuly = Report(kgehrId, "kgehr-july-1405", new(2026, 7, 1), new(2026, 7, 31),
                "kgehr-july-1405", true, "Accepted", 0, "feature141-kgehr");
            var kgehrAugust = Report(kgehrId, "kgehr-august-1405", new(2026, 8, 1), new(2026, 8, 31),
                "kgehr-august-1405", true, "Accepted", 0, "feature141-kgehr");
            var kgelTrendReports = Enumerable.Range(0, 6).Select(index =>
            {
                var start = new DateOnly(2026, 3, 1).AddMonths(index);
                return Report(kgelId, $"kgel-trend-{index + 1}", start, start.AddMonths(1).AddDays(-1),
                    $"kgel-trend-{index + 1}", true, "Accepted", 0, "feature141-kgel");
            }).ToArray();
            var yearOverYearReports = new List<NormalizedMonthlyReportRow>();
            var yearOverYearLines = new List<NormalizedMonthlyReportLineItemRow>();
            for (var offset = 0; offset < 12; offset++)
            {
                var periodMonth = new DateOnly(2025, 3, 1).AddMonths(offset);
                var nextMonth = periodMonth.AddMonths(1);
                var report = Report(companyId, $"yoy-{periodMonth:yyyy-MM}",
                    new DateOnly(periodMonth.Year, periodMonth.Month, 21),
                    new DateOnly(nextMonth.Year, nextMonth.Month, 20),
                    $"accepted-yoy-{periodMonth:yyyy-MM}", true, "Accepted", 0);
                yearOverYearReports.Add(report);
                yearOverYearLines.Add(Line(report, 10m, 8m, offset == 0 ? 50m : 75m, 6.25m, "ton"));
            }
            db.MonthlyReports.AddRange(new[] { month1, otherCompanyMonth1, month2, company2June, rejectedRevision, cumulative, acceptedAlias,
                    foreignProvider, shahrivarKgel, shahrivarKgehr, newlyAddedProducerReport, zeroProductionReport,
                    productionOnlyReport, unauthorizedProducerReport, kgehrJuly, kgehrAugust, kgelYearAgo }
                .Concat(yearOverYearReports).Concat(kgelTrendReports));
            db.MonthlyReportLineItems.AddRange(
                Line(month1, 10m, 8m, 100m, 12.5m, "ton"),
                Line(foreignProvider, 900m, 800m, 9000m, 20m, "ton"),
                Line(month1, 4m, 3m, 40m, 10m, "ton", AmbiguousProductCode),
                Line(month1, 0m, 0m, 0m, 0m, "ton", "PRODUCT:MONTHLY-FIXTURE:ZERO"),
                Line(month1, 1m, 1m, -10m, 10m, "ton", "PRODUCT:MONTHLY-FIXTURE:NEGATIVE"),
                Line(month1, 2m, 2m, 0m, 0m, "pieces", "PRODUCT:MONTHLY-FIXTURE:PIECES"),
                Line(otherCompanyMonth1, 15m, 12m, 175m, 14m, "ton", "PRODUCT:MONTHLY-FIXTURE:MTH2:PELLET", "Pellet"),
                Line(otherCompanyMonth1, 5m, 5m, 25m, 5m, "ton", AmbiguousProductCode),
                Line(company2June, 6m, 5m, 200m, 40m, "ton"),
                Line(month2, 20m, 18m, 250m, 13.8m, "ton"),
                Line(month2, 5m, 5m, 20m, 4m, "ton", "PRODUCT:MONTHLY-FIXTURE:ZERO"),
                Line(month2, 2m, 2m, 20m, 10m, "ton", "PRODUCT:MONTHLY-FIXTURE:NEGATIVE"),
                Line(rejectedRevision, 900m, 800m, 9000m, 20m, "ton"),
                Line(cumulative, 700m, 600m, 7000m, 19m, "ton"),
                Line(month2, 1m, 1m, 2500m, 2m, "ton", AmbiguousProductCode),
                Line(acceptedAlias, 1m, 1m, 2550m, 2m, "ton", AmbiguousProductCode));
            var kgelProductKey = ProductKey("feature141-kgel");
            var kgehrProductKey = ProductKey("feature141-kgehr");
            db.MonthlyReportLineItems.AddRange(
                Line(shahrivarKgel, 25m, 20m, 450m, 22.5m, "ton", kgelProductKey, "گندله"),
                Line(shahrivarKgehr, 47m, 40m, 700m, 17.5m, "ton", kgehrProductKey, "گندله"));
            db.MonthlyReportLineItems.AddRange(
                Line(kgehrJuly, 25m, 20m, 350m, 17.5m, "ton", kgehrProductKey, "Pellet"),
                Line(kgehrAugust, 35m, 30m, 500m, 16.7m, "ton", kgehrProductKey, "Pellet"));
            db.MonthlyReportLineItems.AddRange(
                Line(newlyAddedProducerReport, 62m, 55m, 900m, 16m, "ton", ProductKey("feature141-new-producer"), "\u06af\u0646\u062f\u0644\u0647 \u0633\u0646\u06af\u200c\u0622\u0647\u0646"),
                Line(zeroProductionReport, 0m, 0m, 0m, 0m, "ton", ProductKey("feature141-zero-producer"), "\u06af\u0646\u062f\u0644\u0647"));
            db.MonthlyReportLineItems.Add(Line(productionOnlyReport, 80m, 0m, 0m, 0m, "ton",
                ProductKey("feature141-production-only"), "\u06af\u0646\u062f\u0644\u0647"));
            db.MonthlyReportLineItems.Add(Line(unauthorizedProducerReport, 90m, 75m, 950m, 18m, "ton",
                ProductKey("feature141-unauthorized-producer"), "\u06af\u0646\u062f\u0644\u0647"));
            db.MonthlyReportLineItems.Add(Line(kgelYearAgo, 22m, 18m, 250m, 13.8m, "ton", kgelProductKey,
                "\u06af\u0646\u062f\u0644\u0647"));
            db.MonthlyReportLineItems.Add(Line(shahrivarKgel, 12m, 10m, 400m, 40m, "ton",
                ProductKey("feature141-kgel", "\u06a9\u0646\u0633\u0627\u0646\u062a\u0631\u0647"), "\u06a9\u0646\u0633\u0627\u0646\u062a\u0631\u0647"));
            var trendSales = new[] { 100m, 120m, 150m, 200m, 250m, 300m };
            for (var index = 0; index < kgelTrendReports.Length; index++)
                db.MonthlyReportLineItems.Add(Line(kgelTrendReports[index], 10m + index, 8m + index,
                    trendSales[index], 12.5m, "ton", kgelProductKey, "گندله"));
            db.MonthlyReportLineItems.Add(Line(kgelTrendReports[^1], 6m, 0m, 0m, 0m, "ton",
                ProductKey("feature141-kgel", "\u06a9\u0646\u0633\u0627\u0646\u062a\u0631\u0647"), "\u06a9\u0646\u0633\u0627\u0646\u062a\u0631\u0647"));
            db.MonthlyReportLineItems.AddRange(yearOverYearLines);
            await db.SaveChangesAsync();
        }

        var role = "feature141_reader_" + Guid.NewGuid().ToString("N");
        var password = "F141_" + Guid.NewGuid().ToString("N");
        try
        {
            await CreateRestrictedRoleAsync(database.ConnectionString, databaseName, role, password);
            var restrictedConnection = new NpgsqlConnectionStringBuilder(database.ConnectionString)
            {
                Username = role,
                Password = password,
                Pooling = false
            }.ConnectionString;
            await AssertRoleIsReadOnlyAsync(restrictedConnection, role);

            var snapshot = ApprovedFixtureSnapshot();
            var compiler = new StructuredQueryCompiler();
            var responseGenerator = new PersianStructuredQueryResponseGenerator();
            await using var bindingDb = database.CreateContext();
            var canonicalEntityResolver = new CanonicalQueryEntityResolver(bindingDb,
                Options.Create(new CanonicalEntityResolutionOptions(Enabled: true)));
            var companyBinder = new StructuredQueryCompanyBinder(canonicalEntityResolver);
            var companyScopedProductResolver = new CompanyScopedProductResolver(
                new CompanyResolverService(bindingDb, NullLogger<CompanyResolverService>.Instance),
                new EfCoreMonthlyProductComparisonRepository(bindingDb));
            var productBinder = new StructuredQueryProductBinder(companyScopedProductResolver,
                new EfCoreMonthlyProductComparisonRepository(bindingDb));
            var production = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_production_quantity");
            var shahrivarStart = new DateOnly(2026, 8, 23);
            var shahrivarEnd = new DateOnly(2026, 9, 22);

            var reviewedProductResolver = new ReviewedProductConceptResolver([
                new ReviewedProductConcept("product_iron_ore_pellets", "product-catalog-v1",
                    "\u06af\u0646\u062f\u0644\u0647 \u0633\u0646\u06af\u200c\u0622\u0647\u0646",
                    ["\u06af\u0646\u062f\u0644\u0647", "\u06af\u0646\u062f\u0644\u0647 \u0633\u0646\u06af\u200c\u0622\u0647\u0646"], "approved",
                    "owner-confirmed:feature141-product-equivalence:2026-10-09")
            ]);
            var shortAlias = reviewedProductResolver.Resolve("\u06af\u0646\u062f\u0644\u0647");
            var longAlias = reviewedProductResolver.Resolve("\u06af\u0646\u062f\u0644\u0647 \u0633\u0646\u06af\u200c\u0622\u0647\u0646");
            Assert.Equal(ProductConceptResolutionStatus.Resolved, shortAlias.Status);
            Assert.Equal(shortAlias.CatalogId, longAlias.CatalogId);
            var populationPlan = new StructuredQueryPlan(1, snapshot.CatalogVersion, snapshot.CatalogHash, TenantId,
                [new PlanEntityReference("Product", "\u06af\u0646\u062f\u0644\u0647", ResolvedEntityKey: shortAlias.CatalogId,
                    IdentityProvenance: shortAlias.ApprovalReference, IdentityVerified: true)],
                [new PlanSourceReference(FinancialValueReferenceType.MonthlyMeasure, production.CatalogId, null, null, null, null,
                    production.CatalogId, production.UnitCode, production.UnitKind, "catalog-mapped", "fixture-approved", true)],
                production.RelationshipIds, [new PlanProjection(production.CatalogId, PlanProjectionRole.Value)], [],
                new PlanPeriodSelection(["explicit-period"], [], false, false, shahrivarStart, shahrivarEnd, 1),
                new PlanStatementSelection([], [], [], [], false, true, true, true, true, true), [], [],
                new PlanPagination(0, 20), PlanPresentation.Table,
                new PlanFinancialOperation(FinancialValueOperation.DirectValueRetrieval, PlanFinancialOperationKind.DirectValue,
                    PlanComparisonMode.None, [], false), true, true, true, true, [], ProductConcept: shortAlias,
                CompanyDiscoveryScope: new AuthorizedCompanyDiscoveryScope(TenantId, "fixture-tenant-company-scope",
                    "owner-approved-fixture-tenant-scope", CompanyCoverageComplete: true),
                PopulationCoveragePolicyId: "explicit-reporting-period-v1");
            var populationQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(populationPlan, snapshot).Query);
            Assert.Empty(populationQuery.CompanyIds);
            Assert.Contains("ANY(@company_ids)", populationQuery.Sql, StringComparison.Ordinal);
            var independentFixtureFacts = new List<string>();
            await using (var truthConnection = new NpgsqlConnection(database.ConnectionString))
            {
                await truthConnection.OpenAsync();
                await using var truthCommand = truthConnection.CreateCommand();
                truthCommand.CommandText = "SELECT c.\"Id\", li.\"Title\", li.\"ProductionQuantity\", r.\"PeriodStart\", r.\"PeriodEnd\" " +
                    "FROM public.\"Companies\" c JOIN public.\"MonthlyReports\" r ON r.\"ProviderName\" = c.\"ProviderName\" AND r.\"ExternalCompanyId\" = c.\"ExternalCompanyId\" " +
                    "JOIN public.\"MonthlyReportLineItems\" li ON li.\"MonthlyReportId\" = r.\"Id\" " +
                    "WHERE c.\"Id\" = ANY(@company_ids) AND r.\"PeriodStart\" = @period_start AND r.\"PeriodEnd\" = @period_end " +
                    "AND li.\"ProductionQuantity\" > 0 ORDER BY c.\"Id\"";
                truthCommand.Parameters.AddWithValue("company_ids", new[] { kgelId, kgehrId, newlyAddedProducerId,
                    zeroProductionCompanyId, productionOnlyCompanyId });
                truthCommand.Parameters.AddWithValue("period_start", shahrivarStart);
                truthCommand.Parameters.AddWithValue("period_end", shahrivarEnd);
                await using var truthReader = await truthCommand.ExecuteReaderAsync();
                while (await truthReader.ReadAsync())
                    independentFixtureFacts.Add($"{truthReader.GetGuid(0)}|{truthReader.GetString(1)}|{truthReader.GetDecimal(2)}|{truthReader.GetDateTime(3):yyyy-MM-dd}|{truthReader.GetDateTime(4):yyyy-MM-dd}");
            }
            Assert.Equal(5, independentFixtureFacts.Count); // Four authorized pellet observations plus a positive concentrate observation.
            Assert.Contains(independentFixtureFacts, fact => fact == $"{kgelId}|\u06af\u0646\u062f\u0644\u0647|25|2026-08-23|2026-09-22");
            Assert.Contains(independentFixtureFacts, fact => fact == $"{kgehrId}|\u06af\u0646\u062f\u0644\u0647|47|2026-08-23|2026-09-22");
            Assert.Contains(independentFixtureFacts, fact => fact == $"{newlyAddedProducerId}|\u06af\u0646\u062f\u0644\u0647 \u0633\u0646\u06af\u200c\u0622\u0647\u0646|62|2026-08-23|2026-09-22");
            Assert.Contains(independentFixtureFacts, fact => fact == $"{productionOnlyCompanyId}|\u06af\u0646\u062f\u0644\u0647|80|2026-08-23|2026-09-22");
            var populationResult = await ExecuteAsync(populationQuery, databaseName, role, restrictedConnection,
                [companyId, companyId2, kgelId, kgehrId, newlyAddedProducerId, zeroProductionCompanyId,
                    productionOnlyCompanyId]);
            Assert.Equal(StructuredQueryExecutionStatus.Incomplete, populationResult.Status);
            Assert.True(new[] { kgelId, kgehrId, newlyAddedProducerId, productionOnlyCompanyId }.OrderBy(id => id)
                    .SequenceEqual(populationResult.Rows.Select(row => (Guid)row["CompanyId"]!).OrderBy(id => id)),
                $"Discovery diagnostic={populationResult.DiagnosticCode}; raw fixture facts={string.Join(";", independentFixtureFacts)}");
            Assert.Contains(populationResult.Rows, row => (Guid)row["CompanyId"]! == newlyAddedProducerId
                && Convert.ToDecimal(row["Value"]) == 62m
                && Equals(row["ProductTitle"], "\u06af\u0646\u062f\u0644\u0647 \u0633\u0646\u06af\u200c\u0622\u0647\u0646"));
            Assert.DoesNotContain(populationResult.Rows, row => (Guid)row["CompanyId"]! == zeroProductionCompanyId);
            Assert.DoesNotContain(populationResult.Rows, row => (Guid)row["CompanyId"]! == unauthorizedProducerId);
            Assert.Contains(populationResult.Rows, row => (Guid)row["CompanyId"]! == productionOnlyCompanyId
                && Convert.ToDecimal(row["Value"]) == 80m);
            Assert.All(populationResult.Rows, row => Assert.Equal(shahrivarEnd, row["PeriodEnd"]));
            var restrictedPopulationResult = await ExecuteAsync(populationQuery, databaseName, role, restrictedConnection,
                [companyId, kgelId, newlyAddedProducerId, zeroProductionCompanyId]);
            Assert.DoesNotContain(restrictedPopulationResult.Rows, row => (Guid)row["CompanyId"]! == kgehrId);

            var productionPipeline = await ReplayCapturedQuestionAsync(snapshot, production, companyBinder, productBinder,
                "مقدار تولید گندله کگهر در شهریور ۱۴۰۵ چقدر بوده؟", "کگهر", "گندله",
                "scalar", shahrivarStart, shahrivarEnd, 1, null);
            Assert.True(productionPipeline.Validation.IsStructurallyValid,
                string.Join("|", productionPipeline.Validation.Diagnostics));
            Assert.Equal(StructuredCompanyBindingStatus.Found, productionPipeline.CompanyBinding!.Status);
            Assert.Equal(kgehrId, productionPipeline.CompanyBinding.CompanyId);
            Assert.Equal(StructuredProductBindingStatus.Found, productionPipeline.ProductBinding!.Status);
            Assert.Equal(ProductKey("feature141-kgehr"), productionPipeline.ProductBinding.ProductKey);
            Assert.Contains(productionPipeline.Plan.Diagnostics, item => item == "offline_plan_execution_disabled");
            var productionAuthorizedForFixture = productionPipeline.Plan with
            { RetrievalComplete = true, SemanticReviewApproved = true, QueryAuthorized = true, ExecutionEligible = true };
            var productionQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(productionAuthorizedForFixture, snapshot).Query);
            var productionFixtureResult = await ExecuteAsync(productionQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, productionFixtureResult.Status);
            Assert.Equal(47m, Convert.ToDecimal(Assert.Single(productionFixtureResult.Rows)["Value"]));
            var productionPersianAnswer = responseGenerator.Generate(new(productionAuthorizedForFixture, productionFixtureResult));
            Assert.Equal(StructuredAnswerStatus.Answered, productionPersianAnswer.Status);
            Assert.Contains("\u06f4\u06f7", productionPersianAnswer.TextAnswer);
            Assert.False(string.IsNullOrWhiteSpace(productionPersianAnswer.Unit));

            var rankedProductionGrowthPlan = Plan(snapshot, production, kgelId, PlanPresentation.RankedTable,
                latest: true, productCode: null) with
            {
                Period = new PlanPeriodSelection(["latest"], ["period-over-period"], true, false, MaximumPeriods: 3),
                Aggregations = [new PlanAggregation(production.CatalogId, PlanAggregationOperation.Sum)],
                Sort = [new PlanSort(production.CatalogId, PlanSortDirection.Descending)],
                Pagination = new PlanPagination(0, 5),
                FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                    PlanFinancialOperationKind.GrowthPercentage, PlanComparisonMode.MonthOverMonth, [], FormulaAuthorized: true)
            };
            var rankedProductionGrowthQuery = Assert.IsType<CompiledReadOnlyQuery>(
                compiler.Compile(rankedProductionGrowthPlan, snapshot).Query);
            var rankedProductionGrowth = await ExecuteAsync(rankedProductionGrowthQuery, databaseName, role, restrictedConnection);
            Assert.True(rankedProductionGrowth.Status == StructuredQueryExecutionStatus.Succeeded,
                rankedProductionGrowth.DiagnosticCode);
            Assert.Contains(rankedProductionGrowth.Rows, row =>
                Equals(row["ProductTitle"], "\u06a9\u0646\u0633\u0627\u0646\u062a\u0631\u0647")
                && Convert.ToDecimal(row["GrowthPercentage"]) == 100m);
            Assert.Equal(rankedProductionGrowth.Rows.Select(row => Convert.ToDecimal(row["GrowthPercentage"]))
                    .OrderDescending(),
                rankedProductionGrowth.Rows.Select(row => Convert.ToDecimal(row["GrowthPercentage"])));

            var sales = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales");
            var salesPipeline = await ReplayCapturedQuestionAsync(snapshot, sales, companyBinder, productBinder,
                "مبلغ فروش کگل در شهریور ۱۴۰۵ چقدر بوده؟", "کگل", null,
                "scalar", shahrivarStart, shahrivarEnd, 1, [new PlanAggregation(sales.CatalogId, PlanAggregationOperation.Sum)]);
            Assert.True(salesPipeline.Validation.IsStructurallyValid,
                string.Join("|", salesPipeline.Validation.Diagnostics));
            Assert.Equal(kgelId, salesPipeline.CompanyBinding!.CompanyId);
            var salesAuthorizedForFixture = salesPipeline.Plan with
            { RetrievalComplete = true, SemanticReviewApproved = true, QueryAuthorized = true, ExecutionEligible = true };
            var salesQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(salesAuthorizedForFixture, snapshot).Query);
            var salesFixtureResult = await ExecuteAsync(salesQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, salesFixtureResult.Status);
            Assert.Equal(850m, Convert.ToDecimal(Assert.Single(salesFixtureResult.Rows)["Value"]));
            var salesPersianAnswer = responseGenerator.Generate(new(salesAuthorizedForFixture, salesFixtureResult));
            Assert.Equal(StructuredAnswerStatus.Answered, salesPersianAnswer.Status);
            Assert.Contains("۸۵ میلیون تومان", salesPersianAnswer.TextAnswer);

            var twoCompanyBindings = new[]
            {
                new PlanEntityReference("Company", "MTH", companyId, IdentityProvenance: "fixture-canonical-company", IdentityVerified: true),
                new PlanEntityReference("Company", "MTH2", companyId2, IdentityProvenance: "fixture-canonical-company", IdentityVerified: true)
            };
            var latestCommonMonthPlan = Plan(snapshot, sales, companyId, PlanPresentation.Table, latest: true, productCode: null) with
            {
                Entities = twoCompanyBindings,
                Period = new PlanPeriodSelection(["latest"], ["compare"], true, false),
                Aggregations = [new PlanAggregation(sales.CatalogId, PlanAggregationOperation.Sum)],
                Pagination = new PlanPagination(0, 10)
            };
            var latestCommonMonthQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(latestCommonMonthPlan, snapshot).Query);
            var latestCommonMonth = await ExecuteAsync(latestCommonMonthQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, latestCommonMonth.Status);
            Assert.Equal(2, latestCommonMonth.Rows.Count);
            Assert.Equal(130m, Convert.ToDecimal(Assert.Single(latestCommonMonth.Rows,
                row => Equals(row["CompanyId"], companyId))["Value"]));
            Assert.Equal(200m, Convert.ToDecimal(Assert.Single(latestCommonMonth.Rows,
                row => Equals(row["CompanyId"], companyId2))["Value"]));
            Assert.Contains("HAVING COUNT(DISTINCT", latestCommonMonthQuery.Sql, StringComparison.Ordinal);
            var latestCommonMonthAnswer = responseGenerator.Generate(new(latestCommonMonthPlan, latestCommonMonth));
            Assert.Equal(StructuredAnswerStatus.Answered, latestCommonMonthAnswer.Status);
            Assert.Contains("MTH", latestCommonMonthAnswer.TextAnswer);
            Assert.Contains("MTH2", latestCommonMonthAnswer.TextAnswer);
            Assert.Contains("۱۳ میلیون تومان", latestCommonMonthAnswer.TextAnswer);
            Assert.Contains("۲۰ میلیون تومان", latestCommonMonthAnswer.TextAnswer);

            var topCompaniesPlan = latestCommonMonthPlan with
            {
                Presentation = PlanPresentation.RankedTable,
                Sort = [new PlanSort(sales.CatalogId, PlanSortDirection.Descending)],
                Pagination = new PlanPagination(0, 2)
            };
            var topCompaniesCompilation = compiler.Compile(topCompaniesPlan, snapshot);
            Assert.True(topCompaniesCompilation.Success, string.Join("|", topCompaniesCompilation.Diagnostics));
            var topCompaniesQuery = Assert.IsType<CompiledReadOnlyQuery>(topCompaniesCompilation.Query);
            var topCompanies = await ExecuteAsync(topCompaniesQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, topCompanies.Status);
            Assert.Equal(new[] { 200m, 130m }, topCompanies.Rows.Select(row => Convert.ToDecimal(row["Value"])));
            Assert.Contains("ORDER BY \"Value\" DESC, \"CompanyId\" ASC", topCompaniesQuery.Sql, StringComparison.Ordinal);
            var topCompaniesAnswer = responseGenerator.Generate(new(topCompaniesPlan, topCompanies));
            Assert.Equal(StructuredAnswerStatus.Answered, topCompaniesAnswer.Status);
            Assert.Contains("MTH2", topCompaniesAnswer.TextAnswer);
            Assert.Contains("MTH", topCompaniesAnswer.TextAnswer);

            const string productEquivalenceId = "fixture-reviewed-pellet-equivalence-v1";
            const string productEquivalenceApproval = "fixture-owner-review:pellet-equivalence:v1";
            var productEquivalence = new ProductEquivalenceResolution(ProductEquivalenceResolutionStatus.Resolved,
                productEquivalenceId, "گندله",
                [new(companyId, Provider, "provider-product:MTH:pellet", ProductCode, "Pellet", "ton", "fixture-product:mth", true),
                 new(companyId2, Provider, "provider-product:MTH2:pellet", "PRODUCT:MONTHLY-FIXTURE:MTH2:PELLET", "Pellet", "ton", "fixture-product:mth2", true)],
                "ton", productEquivalenceApproval, null);
            var populationProductRankingPlan = Plan(snapshot, production, companyId, PlanPresentation.RankedTable,
                latest: true, productCode: productEquivalenceId) with
            {
                Entities = [twoCompanyBindings[0], twoCompanyBindings[1],
                    new PlanEntityReference("Product", "گندله", ResolvedEntityKey: productEquivalenceId,
                        IdentityProvenance: productEquivalenceApproval, IdentityVerified: true)],
                ReviewedProductEquivalence = productEquivalence,
                CompanyPopulationId = "fixture-steel-population-v1",
                CompanyPopulationApprovalReference = "fixture-owner-review:steel-population:v1",
                Aggregations = [new PlanAggregation(production.CatalogId, PlanAggregationOperation.Sum)],
                Sort = [new PlanSort(production.CatalogId, PlanSortDirection.Descending)],
                Pagination = new PlanPagination(0, 2)
            };
            var populationProductRankingCompilation = compiler.Compile(populationProductRankingPlan, snapshot);
            Assert.True(populationProductRankingCompilation.Success, string.Join("|", populationProductRankingCompilation.Diagnostics));
            var populationProductRankingQuery = Assert.IsType<CompiledReadOnlyQuery>(populationProductRankingCompilation.Query);
            var populationProductRanking = await ExecuteAsync(populationProductRankingQuery, databaseName, role, restrictedConnection);
            Assert.True(populationProductRanking.Status == StructuredQueryExecutionStatus.Succeeded,
                $"{populationProductRanking.DiagnosticCode}; rows={string.Join(";", populationProductRanking.Rows.Select(row => string.Join(",", row.Select(item => $"{item.Key}={item.Value}"))))}");
            Assert.Equal(new[] { 15m, 10m }, populationProductRanking.Rows.Select(row => Convert.ToDecimal(row["Value"])));
            var populationProductRankingAnswer = responseGenerator.Generate(new(populationProductRankingPlan, populationProductRanking));
            Assert.Equal(StructuredAnswerStatus.Answered, populationProductRankingAnswer.Status);
            Assert.Contains("MTH2", populationProductRankingAnswer.TextAnswer);
            Assert.Contains("MTH", populationProductRankingAnswer.TextAnswer);
            Assert.Contains("۱۵", populationProductRankingAnswer.TextAnswer);
            Assert.Contains("۱۰", populationProductRankingAnswer.TextAnswer);

            var noCommonPeriodPlan = latestCommonMonthPlan with
            {
                Period = new PlanPeriodSelection(["explicit-period"], ["compare"], false, false,
                    new DateOnly(2026, 4, 21), new DateOnly(2026, 5, 21))
            };
            var noCommonPeriodCompilation = compiler.Compile(noCommonPeriodPlan, snapshot);
            Assert.True(noCommonPeriodCompilation.Success, string.Join("|", noCommonPeriodCompilation.Diagnostics));
            var noCommonPeriodQuery = Assert.IsType<CompiledReadOnlyQuery>(noCommonPeriodCompilation.Query);
            var noCommonPeriod = await ExecuteAsync(noCommonPeriodQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.NoData, noCommonPeriod.Status);
            Assert.Empty(noCommonPeriod.Rows);
            var noCommonPeriodAnswer = responseGenerator.Generate(new(noCommonPeriodPlan, noCommonPeriod));
            Assert.Equal(StructuredAnswerStatus.NoData, noCommonPeriodAnswer.Status);
            Assert.Contains("دادهٔ واجد شرایطی پیدا نشد", noCommonPeriodAnswer.TextAnswer);

            var periodAlignedYoyPlan = Plan(snapshot, sales, companyId, PlanPresentation.Scalar,
                latest: true, productCode: ProductCode) with
            {
                Period = new PlanPeriodSelection(["latest"], ["year-over-year"], true, false, MaximumPeriods: 13),
                FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                    PlanFinancialOperationKind.GrowthPercentage, PlanComparisonMode.YearOverYear, [], FormulaAuthorized: true)
            };
            var periodAlignedYoyCompilation = compiler.Compile(periodAlignedYoyPlan, snapshot);
            Assert.True(periodAlignedYoyCompilation.Success, string.Join("|", periodAlignedYoyCompilation.Diagnostics));
            var periodAlignedYoyQuery = Assert.IsType<CompiledReadOnlyQuery>(periodAlignedYoyCompilation.Query);
            var periodAlignedYoyExecution = await ExecuteAsync(periodAlignedYoyQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Ambiguous, periodAlignedYoyExecution.Status);
            Assert.Equal("monthly_comparison_reporting_windows_not_comparable", periodAlignedYoyExecution.DiagnosticCode);
            var periodAlignedYoyResult = Assert.Single(periodAlignedYoyExecution.Rows);
            Assert.Equal(233.33333333333333m, Convert.ToDecimal(periodAlignedYoyResult["GrowthPercentage"]));
            var periodAlignedYoyAnswer = responseGenerator.Generate(new(periodAlignedYoyPlan, periodAlignedYoyExecution));
            Assert.Equal(StructuredAnswerStatus.Ambiguous, periodAlignedYoyAnswer.Status);
            Assert.DoesNotContain("۲۳۳", periodAlignedYoyAnswer.TextAnswer);
            Assert.DoesNotContain("۲۵۰", periodAlignedYoyAnswer.TextAnswer);

            var crossCompanyArtifact = Path.Combine(FindRepositoryRoot(), "specs", "141-schema-aware-semantic-query-engine",
                "artifacts", "cross-company-postgresql-examples-v3-2026-10-09.jsonl");
            var crossCompanyRecords = new List<object>
            {
                new
                {
                    question = "فروش ماهانهٔ شرکت‌های MTH و MTH2 را در آخرین ماه مشترک با هم مقایسه کن.",
                    executionMode = "deterministic replay + isolated PostgreSQL fixture",
                    semanticReferences = new[] { sales.CatalogId, "rel_31e410fb3ae11956" },
                    selectedMeasure = sales.CatalogId, companyBindings = twoCompanyBindings,
                    queryPlan = latestCommonMonthPlan, sqlExecuted = latestCommonMonthQuery.Sql,
                    sqlParameters = latestCommonMonthQuery.Parameters, postgresStatus = latestCommonMonth.Status.ToString(),
                    expected = new[] { new { company = "MTH", companyId, value = 130m, unit = "MillionIRR", periodEnd = "2026-04-20" },
                        new { company = "MTH2", companyId = companyId2, value = 200m, unit = "MillionIRR", periodEnd = "2026-04-20" } },
                    actualRows = latestCommonMonth.Rows,
                    finalAnswerStatus = latestCommonMonthAnswer.Status.ToString(), finalPersianAnswer = latestCommonMonthAnswer.TextAnswer,
                    finalAnswerEvidence = latestCommonMonthAnswer.Evidence,
                    sourceProvider = Provider, reportIdentity = "fixture-2026-03 / fixture-company-2-2026-03",
                    fixtureOnly = true, developmentDatabaseQueried = false
                },
                new
                {
                    question = "در آخرین ماه مشترک، شرکت‌های MTH و MTH2 را بر اساس مبلغ فروش ماهانه رتبه‌بندی کن.",
                    executionMode = "deterministic replay + isolated PostgreSQL fixture",
                    semanticReferences = new[] { sales.CatalogId, "rel_31e410fb3ae11956" },
                    selectedMeasure = sales.CatalogId, companyBindings = twoCompanyBindings,
                    queryPlan = topCompaniesPlan, sqlExecuted = topCompaniesQuery.Sql,
                    sqlParameters = topCompaniesQuery.Parameters, postgresStatus = topCompanies.Status.ToString(),
                    expected = new[] { new { rank = 1, company = "MTH2", value = 200m, unit = "MillionIRR", periodEnd = "2026-04-20" },
                        new { rank = 2, company = "MTH", value = 130m, unit = "MillionIRR", periodEnd = "2026-04-20" } },
                    actualRows = topCompanies.Rows,
                    finalAnswerStatus = topCompaniesAnswer.Status.ToString(), finalPersianAnswer = topCompaniesAnswer.TextAnswer,
                    finalAnswerEvidence = topCompaniesAnswer.Evidence,
                    sourceProvider = Provider, reportIdentity = "fixture-2026-03 / fixture-company-2-2026-03",
                    fixtureOnly = true, developmentDatabaseQueried = false
                },
                new
                {
                    question = "فروش ماهانهٔ شرکت‌های MTH و MTH2 را برای ماه گزارش منتهی به ۲۱ مه ۲۰۲۶ مقایسه کن.",
                    executionMode = "deterministic replay + isolated PostgreSQL fixture",
                    semanticReferences = new[] { sales.CatalogId, "rel_31e410fb3ae11956" },
                    selectedMeasure = sales.CatalogId, companyBindings = twoCompanyBindings,
                    queryPlan = noCommonPeriodPlan, sqlExecuted = noCommonPeriodQuery.Sql,
                    sqlParameters = noCommonPeriodQuery.Parameters, postgresStatus = noCommonPeriod.Status.ToString(),
                    expected = new { outcome = "NoData", reason = "one requested company has no eligible report for the exact period" },
                    actualRows = noCommonPeriod.Rows,
                    finalAnswerStatus = noCommonPeriodAnswer.Status.ToString(), finalPersianAnswer = noCommonPeriodAnswer.TextAnswer,
                    finalAnswerEvidence = noCommonPeriodAnswer.Evidence,
                    sourceProvider = Provider, fixtureOnly = true, developmentDatabaseQueried = false
                },
                new
                {
                    question = "فروش ماهانهٔ محصول آزمایشی MTH در اردیبهشت ۱۴۰۵ نسبت به اردیبهشت ۱۴۰۴ چند درصد رشد کرده است؟",
                    executionMode = "deterministic replay + isolated PostgreSQL fixture",
                    semanticReferences = new[] { sales.CatalogId, "rel_31e410fb3ae11956" },
                    selectedMeasure = sales.CatalogId, companyBinding = new { ticker = "MTH", companyId },
                    productBinding = new { sourceTitles = new[] { "Pellet" }, sourceUnits = new[] { "ton" }, identityProvenance = "fixture-product-labels" },
                    queryPlan = periodAlignedYoyPlan, sqlExecuted = periodAlignedYoyQuery.Sql,
                    sqlParameters = periodAlignedYoyQuery.Parameters, postgresStatus = periodAlignedYoyExecution.Status.ToString(),
                    expected = new { outcome = "Ambiguous", reason = "reporting-window boundaries differ; no approved calendar normalization exists" },
                    rejectedRawCandidate = new { value = 233.33333333333333m, unit = "percent", currentAmount = 250m,
                        previousAmount = 75m, basePeriod = "2025-04-21..2025-05-20", comparisonPeriod = "2026-04-21..2026-05-21" },
                    actualRows = periodAlignedYoyExecution.Rows,
                    finalAnswerStatus = periodAlignedYoyAnswer.Status.ToString(), finalPersianAnswer = periodAlignedYoyAnswer.TextAnswer,
                    finalAnswerEvidence = periodAlignedYoyAnswer.Evidence,
                    sourceProvider = Provider, reportIdentity = "fixture-yoy-2025-04 / fixture-2026-04",
                    fixtureOnly = true, developmentDatabaseQueried = false
                }
            };
            crossCompanyRecords.Add(new
            {
                question = "شرکت‌های صنعت فولاد را بر اساس مقدار تولید گندله رتبه‌بندی کن.",
                executionMode = "deterministic replay + isolated PostgreSQL fixture",
                selectedMeasure = production.CatalogId,
                companyPopulation = new { populationProductRankingPlan.CompanyPopulationId,
                    populationProductRankingPlan.CompanyPopulationApprovalReference, members = twoCompanyBindings },
                productEquivalence = productEquivalence,
                queryPlan = populationProductRankingPlan, sqlExecuted = populationProductRankingQuery.Sql,
                sqlParameters = populationProductRankingQuery.Parameters,
                postgresStatus = populationProductRanking.Status.ToString(),
                expected = new[] { new { company = "MTH2", value = 15m, unit = "ton" }, new { company = "MTH", value = 10m, unit = "ton" } },
                actualRows = populationProductRanking.Rows,
                finalAnswerStatus = populationProductRankingAnswer.Status.ToString(),
                finalPersianAnswer = populationProductRankingAnswer.TextAnswer,
                finalAnswerEvidence = populationProductRankingAnswer.Evidence,
                fixtureOnly = true, developmentDatabaseQueried = false
            });
            await File.WriteAllLinesAsync(crossCompanyArtifact, crossCompanyRecords.Select(record =>
                System.Text.Json.JsonSerializer.Serialize(record)));

            var trendPipeline = await ReplayCapturedQuestionAsync(snapshot, sales, companyBinder, productBinder,
                "روند فروش گندله کگل در شش ماه گذشته چه بوده؟", "کگل", "گندله",
                "time-series", new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 31), 6, null);
            Assert.True(trendPipeline.Validation.IsStructurallyValid,
                string.Join("|", trendPipeline.Validation.Diagnostics));
            var trendAuthorizedForFixture = trendPipeline.Plan with
            { RetrievalComplete = true, SemanticReviewApproved = true, QueryAuthorized = true, ExecutionEligible = true };
            var trendQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(trendAuthorizedForFixture, snapshot).Query);
            var trendFixtureResult = await ExecuteAsync(trendQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, trendFixtureResult.Status);
            Assert.Equal(new[] { 100m, 120m, 150m, 200m, 250m, 300m },
                trendFixtureResult.Rows.Select(row => Convert.ToDecimal(row["Value"])));

            await RunLiveNaturalLanguageFixtureQueryAsync(snapshot, compiler, companyBinder, productBinder,
                databaseName, role, restrictedConnection, kgehrId, ProductKey("feature141-kgehr"),
                kgelId, ProductKey("feature141-kgel"),
                [companyId, companyId2, kgelId, kgehrId, newlyAddedProducerId, zeroProductionCompanyId,
                    productionOnlyCompanyId], unauthorizedProducerId);

            var latestPlan = Plan(snapshot, production, companyId, PlanPresentation.Scalar, latest: true, ProductCode);
            var latest = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(latestPlan, snapshot).Query);
            var latestResult = await ExecuteAsync(latest, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, latestResult.Status);
            var latestRow = Assert.Single(latestResult.Rows);
            Assert.Equal(20m, Convert.ToDecimal(latestRow["Value"]));
            Assert.Equal("Pellet", latestRow["ProductTitle"]);
            Assert.False(latestRow.ContainsKey("ProductCode"));
            Assert.Equal("ton", latestRow["SourceUnit"]);
            Assert.Equal("Accepted", latestRow["RevisionStatus"]);
            Assert.Equal(0, Convert.ToInt32(latestRow["OutputType"]));
            Assert.Equal("fixture-2026-04", latestRow["ExternalReportId"]);

            var seriesPlan = Plan(snapshot, sales, companyId, PlanPresentation.TimeSeries, latest: false, ProductCode) with
            {
                Period = new PlanPeriodSelection([], [], false, false, new DateOnly(2026, 3, 21), new DateOnly(2026, 5, 21), 6),
                Sort = [new PlanSort(sales.PeriodEndColumnCatalogId, PlanSortDirection.Ascending)]
            };
            var series = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(seriesPlan, snapshot).Query);
            var seriesResult = await ExecuteAsync(series, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, seriesResult.Status);
            Assert.Equal(new[] { 100m, 250m }, seriesResult.Rows.Select(row => Convert.ToDecimal(row["Value"])));
            Assert.Equal(new[] { new DateOnly(2026, 4, 20), new DateOnly(2026, 5, 21) },
                seriesResult.Rows.Select(row => row["PeriodEnd"] switch
                {
                    DateOnly date => date,
                    DateTime dateTime => DateOnly.FromDateTime(dateTime),
                    _ => throw new InvalidOperationException("PostgreSQL did not return a date value.")
                }));

            var growthPlan = Plan(snapshot, sales, companyId, PlanPresentation.TimeSeries, latest: false, ProductCode) with
            {
                Period = new PlanPeriodSelection(["explicit-period"], ["period-over-period"], false, false,
                    new DateOnly(2026, 3, 21), new DateOnly(2026, 5, 21), 3),
                Sort = [new PlanSort(sales.PeriodEndColumnCatalogId, PlanSortDirection.Ascending)],
                FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                    PlanFinancialOperationKind.GrowthPercentage, PlanComparisonMode.MonthOverMonth, [], FormulaAuthorized: true)
            };
            var growthQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(growthPlan, snapshot).Query);
            var growthResult = await ExecuteAsync(growthQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, growthResult.Status);
            var aprilGrowth = Assert.Single(growthResult.Rows, row => row["PeriodEnd"] is DateOnly date && date == new DateOnly(2026, 5, 21));
            Assert.Equal(150m, Convert.ToDecimal(aprilGrowth["GrowthPercentage"]));

            var absoluteChangePlan = growthPlan with
            {
                FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                    PlanFinancialOperationKind.AbsoluteChange, PlanComparisonMode.MonthOverMonth, [], FormulaAuthorized: true)
            };
            var absoluteChange = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(absoluteChangePlan, snapshot).Query);
            var absoluteChangeResult = await ExecuteAsync(absoluteChange, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, absoluteChangeResult.Status);
            var aprilAbsoluteChange = Assert.Single(absoluteChangeResult.Rows,
                row => row["PeriodEnd"] is DateOnly date && date == new DateOnly(2026, 5, 21));
            Assert.Equal(150m, Convert.ToDecimal(aprilAbsoluteChange["ComparisonValue"]));
            Assert.Equal(250m, Convert.ToDecimal(aprilAbsoluteChange["Value"]));
            Assert.Equal(100m, Convert.ToDecimal(aprilAbsoluteChange["PreviousValue"]));

            var productionGrowthPlan = Plan(snapshot, production, companyId, PlanPresentation.TimeSeries,
                latest: false, ProductCode) with
            {
                Period = new PlanPeriodSelection(["explicit-period"], ["period-over-period"], false, false,
                    new DateOnly(2026, 3, 21), new DateOnly(2026, 5, 21), 3),
                Sort = [new PlanSort(production.PeriodEndColumnCatalogId, PlanSortDirection.Ascending)],
                FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                    PlanFinancialOperationKind.GrowthPercentage, PlanComparisonMode.MonthOverMonth, [], FormulaAuthorized: true)
            };
            var productionGrowth = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(productionGrowthPlan, snapshot).Query);
            var productionGrowthResult = await ExecuteAsync(productionGrowth, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, productionGrowthResult.Status);
            var aprilProductionGrowth = Assert.Single(productionGrowthResult.Rows,
                row => row["PeriodEnd"] is DateOnly date && date == new DateOnly(2026, 5, 21));
            Assert.Equal(100m, Convert.ToDecimal(aprilProductionGrowth["GrowthPercentage"]));

            var yearOverYearPlan = Plan(snapshot, sales, companyId, PlanPresentation.TimeSeries,
                latest: false, ProductCode) with
            {
                Period = new PlanPeriodSelection(["explicit-period"], ["year-over-year"], false, false,
                    new DateOnly(2025, 3, 21), new DateOnly(2026, 4, 20), 13),
                Sort = [new PlanSort(sales.PeriodEndColumnCatalogId, PlanSortDirection.Ascending)],
                FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                    PlanFinancialOperationKind.GrowthPercentage, PlanComparisonMode.YearOverYear, [], FormulaAuthorized: true)
            };
            var yearOverYear = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(yearOverYearPlan, snapshot).Query);
            var yearOverYearResult = await ExecuteAsync(yearOverYear, databaseName, role, restrictedConnection);
            Assert.True(yearOverYearResult.Status == StructuredQueryExecutionStatus.Succeeded,
                $"{yearOverYearResult.Status}:{yearOverYearResult.DiagnosticCode}; " +
                string.Join("|", yearOverYearResult.Rows.Select(row =>
                    $"{row.GetValueOrDefault("PeriodEnd")} <- {row.GetValueOrDefault("PreviousPeriodEnd")}: {row.GetValueOrDefault("ComparisonStatus")}")));
            var marchYearOverYear = Assert.Single(yearOverYearResult.Rows,
                row => row["PeriodEnd"] is DateOnly date && date == new DateOnly(2026, 4, 20));
            Assert.Equal(100m, Convert.ToDecimal(marchYearOverYear["GrowthPercentage"]));

            var missingMonthPlan = Plan(snapshot, sales, companyId2, PlanPresentation.TimeSeries,
                latest: false, ProductCode) with
            {
                Period = new PlanPeriodSelection(["explicit-period"], ["period-over-period"], false, false,
                    new DateOnly(2026, 3, 21), new DateOnly(2026, 7, 20), 4),
                Sort = [new PlanSort(sales.PeriodEndColumnCatalogId, PlanSortDirection.Ascending)],
                FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                    PlanFinancialOperationKind.GrowthPercentage, PlanComparisonMode.MonthOverMonth, [], FormulaAuthorized: true)
            };
            var missingMonth = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(missingMonthPlan, snapshot).Query);
            var missingMonthResult = await ExecuteAsync(missingMonth, databaseName, role, restrictedConnection);
            Assert.True(missingMonthResult.Status == StructuredQueryExecutionStatus.Incomplete,
                $"{missingMonthResult.DiagnosticCode}; rows={string.Join(";", missingMonthResult.Rows.Select(row => string.Join(",", row.Select(item => $"{item.Key}={item.Value}"))))}");
            Assert.Equal("monthly_comparison_period_missing", missingMonthResult.DiagnosticCode);

            foreach (var (productCode, expectedDiagnostic) in new[]
            {
                ("PRODUCT:MONTHLY-FIXTURE:ZERO", "monthly_growth_zero_denominator"),
                ("PRODUCT:MONTHLY-FIXTURE:NEGATIVE", "monthly_growth_negative_baseline_unsupported")
            })
            {
                var guardedGrowthPlan = Plan(snapshot, sales, companyId, PlanPresentation.TimeSeries,
                    latest: false, productCode) with
                {
                    Period = growthPlan.Period,
                    Sort = growthPlan.Sort,
                    FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                        PlanFinancialOperationKind.GrowthPercentage, PlanComparisonMode.MonthOverMonth, [], FormulaAuthorized: true)
                };
                var guardedGrowth = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(guardedGrowthPlan, snapshot).Query);
                var guardedResult = await ExecuteAsync(guardedGrowth, databaseName, role, restrictedConnection);
                Assert.Equal(StructuredQueryExecutionStatus.Incomplete, guardedResult.Status);
                Assert.Equal(expectedDiagnostic, guardedResult.DiagnosticCode);
            }

            var aggregatePlan = Plan(snapshot, sales, companyId, PlanPresentation.Table, latest: false, productCode: null) with
            {
                Entities =
                [
                    new PlanEntityReference("Company", "monthly fixture company", companyId),
                    new PlanEntityReference("Company", "monthly fixture company 2", companyId2)
                ],
                Period = new PlanPeriodSelection(["explicit-period"], [], false, false,
                    new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 20)),
                Aggregations = [new PlanAggregation(sales.CatalogId, PlanAggregationOperation.Sum)]
            };
            var aggregateQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(aggregatePlan, snapshot).Query);
            Assert.Equal(2, aggregateQuery.CompanyIds.Count);
            var aggregateResult = await ExecuteAsync(aggregateQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, aggregateResult.Status);
            var totalsByCompany = aggregateResult.Rows.ToDictionary(row => (Guid)row["CompanyId"]!,
                row => Convert.ToDecimal(row["Value"]));
            Assert.Equal(130m, totalsByCompany[companyId]);
            Assert.Equal(200m, totalsByCompany[companyId2]);

            var salesQuantity = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales_quantity");
            foreach (var (measure, expectedValue) in new[] { (production, 20m), (salesQuantity, 17m) })
            {
                var quantityAggregatePlan = Plan(snapshot, measure, companyId2, PlanPresentation.Table,
                    latest: false, productCode: null) with
                {
                    Period = new PlanPeriodSelection(["explicit-period"], [], false, false,
                        new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 20)),
                    Aggregations = [new PlanAggregation(measure.CatalogId, PlanAggregationOperation.Sum)]
                };
                var quantityAggregate = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(quantityAggregatePlan, snapshot).Query);
                var quantityAggregateResult = await ExecuteAsync(quantityAggregate, databaseName, role, restrictedConnection);
                Assert.Equal(StructuredQueryExecutionStatus.Succeeded, quantityAggregateResult.Status);
                Assert.Equal(expectedValue, Convert.ToDecimal(Assert.Single(quantityAggregateResult.Rows)["Value"]));
            }

            var rankingPlan = Plan(snapshot, sales, companyId, PlanPresentation.RankedTable, latest: false, productCode: null) with
            {
                Period = new PlanPeriodSelection(["explicit-period"], [], false, false,
                    new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 20)),
                Aggregations = [new PlanAggregation(sales.CatalogId, PlanAggregationOperation.Sum)],
                Sort = [new PlanSort(sales.CatalogId, PlanSortDirection.Descending)],
                Pagination = new PlanPagination(0, 10)
            };
            var rankingQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(rankingPlan, snapshot).Query);
            var rankingResult = await ExecuteAsync(rankingQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, rankingResult.Status);
            Assert.Equal(new[] { 100m, 40m, 0m, 0m, -10m }, rankingResult.Rows.Select(row => Convert.ToDecimal(row["Value"])));

            var quantityRankingPlan = Plan(snapshot, production, companyId, PlanPresentation.RankedTable,
                latest: false, productCode: null) with
            {
                Period = new PlanPeriodSelection(["explicit-period"], [], false, false,
                    new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 20)),
                Aggregations = [new PlanAggregation(production.CatalogId, PlanAggregationOperation.Sum)],
                Sort = [new PlanSort(production.CatalogId, PlanSortDirection.Descending)],
                Pagination = new PlanPagination(0, 10)
            };
            var quantityRanking = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(quantityRankingPlan, snapshot).Query);
            var quantityRankingResult = await ExecuteAsync(quantityRanking, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, quantityRankingResult.Status);
            Assert.Equal(new[] { 10m, 4m, 2m, 1m, 0m }, quantityRankingResult.Rows.Select(row => Convert.ToDecimal(row["Value"])));

            var incompatibleQuantityPlan = Plan(snapshot, production, companyId, PlanPresentation.Table,
                latest: false, productCode: null) with
            {
                Period = new PlanPeriodSelection(["explicit-period"], [], false, false,
                    new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 20)),
                Aggregations = [new PlanAggregation(production.CatalogId, PlanAggregationOperation.Sum)]
            };
            var incompatibleQuantity = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(incompatibleQuantityPlan, snapshot).Query);
            var incompatibleQuantityResult = await ExecuteAsync(incompatibleQuantity, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Ambiguous, incompatibleQuantityResult.Status);
            Assert.Equal("monthly_product_period_has_multiple_source_rows", incompatibleQuantityResult.DiagnosticCode);

            var injectionValue = "PRODUCT'; DROP TABLE \"MonthlyReportLineItems\"; --";
            var parameterizedPlan = Plan(snapshot, sales, companyId, PlanPresentation.Scalar,
                latest: true, injectionValue);
            var parameterized = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(parameterizedPlan, snapshot).Query);
            Assert.DoesNotContain(injectionValue, parameterized.Sql, StringComparison.Ordinal);
            Assert.Contains(parameterized.Parameters, parameter => Equals(parameter.Value, injectionValue));
            var parameterizedResult = await ExecuteAsync(parameterized, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.NoData, parameterizedResult.Status);

            var ambiguousPlan = Plan(snapshot, sales, companyId, PlanPresentation.Scalar,
                latest: true, AmbiguousProductCode);
            var ambiguousQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(ambiguousPlan, snapshot).Query);
            var ambiguousResult = await ExecuteAsync(ambiguousQuery, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Ambiguous, ambiguousResult.Status);
            Assert.Equal("monthly_product_period_has_multiple_source_rows", ambiguousResult.DiagnosticCode);

            // Critical acceptance: rewriting every persisted ProductCode (zero, duplicated across companies, changed
            // between months) changes no Feature 141 monthly answer.
            var probes = new[] { productionQuery, salesQuery, trendQuery, latest, series, growthQuery, absoluteChange,
                yearOverYear, rankingQuery, quantityRanking, incompatibleQuantity, ambiguousQuery, populationProductRankingQuery };
            async Task<string[]> AnswersAsync()
            {
                var answers = new List<string>();
                foreach (var probe in probes)
                    answers.Add(Render(await ExecuteAsync(probe, databaseName, role, restrictedConnection)));
                answers.Add(Render(await ExecuteAsync(populationQuery, databaseName, role, restrictedConnection,
                    [companyId, companyId2, kgelId, kgehrId, newlyAddedProducerId, zeroProductionCompanyId, productionOnlyCompanyId])));
                return answers.ToArray();
            }
            var answersBefore = await AnswersAsync();
            await using (var admin = new NpgsqlConnection(database.ConnectionString))
            {
                await admin.OpenAsync();
                await using var rewrite = admin.CreateCommand();
                rewrite.CommandText = "UPDATE public.\"MonthlyReportLineItems\" SET \"ProductCode\" = CASE WHEN random() < 0.3 THEN '0' " +
                    "ELSE 'PRODUCT:NATURAL:' || substr(md5(random()::text), 1, 16) END";
                Assert.True(await rewrite.ExecuteNonQueryAsync() > 0);
            }
            Assert.Equal(answersBefore, await AnswersAsync());

            var unauthorized = compiler.Compile(Plan(RuntimeSnapshot(), production, companyId,
                PlanPresentation.Scalar, latest: true, ProductCode), RuntimeSnapshot());
            Assert.True(unauthorized.Success);
            Assert.False(unauthorized.Query!.QueryAuthorized);
            Assert.Null(new DevelopmentStructuredQueryAuthorizer(
                Options.Create(ExecutionOptions(databaseName, role, restrictedConnection, unauthorized.Query)),
                new TestEnvironment("Development")).Authorize(unauthorized.Query, TenantId));
        }
        finally
        {
            await DropRoleAsync(database.ConnectionString, role);
        }
    }

    [SkippableFact]
    public async Task FinancialStatementRetrievalExecutesThroughGenericPlanningCompilationAndPersianRendering()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var databaseName = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database
            ?? throw new InvalidOperationException("The isolated PostgreSQL test database has no name.");
        var marketId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var snapshot = ApprovedFinancialStatementFixtureSnapshot();
        var source = Assert.Single(snapshot.DirectSources, item => item.ProviderName == Provider
            && item.StatementType == "IncomeStatement" && item.SourceItemId == 161);
        var statementId = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Markets.Add(new NormalizedMarketRow
            {
                Id = marketId, ProviderName = Provider, ExternalId = "feature141-statement-market",
                Name = "Feature 141 statement fixture", LastSynchronizedAt = DateTimeOffset.UtcNow
            });
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = companyId, ProviderName = Provider, ExternalCompanyId = "feature141-statement-kgehr",
                Name = "Fixture Kogهر", Ticker = "کگهر", CompanySymbol = "کگهر", MarketId = marketId, PrecedencyRight = 0,
                LastSynchronizedAt = DateTimeOffset.UtcNow
            });
            db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow
            {
                Id = source.SourceItemCatalogId, ProviderName = source.ProviderName,
                StatementType = source.StatementType, SourceItemId = source.SourceItemId,
                TitleFa = "سود عملیاتی هر سهم", Unit = source.SourceUnit,
                LastSynchronizedAt = DateTimeOffset.UtcNow
            });
            db.FinancialStatements.Add(new NormalizedFinancialStatementRow
            {
                Id = statementId, CompanyId = null, ProviderName = source.ProviderName,
                ExternalCompanyId = "feature141-statement-kgehr", ExternalStatementId = "fixture-income-2026-q2",
                StatementType = source.StatementType, PeriodType = "ThreeMonths",
                PeriodStart = new DateOnly(2026, 3, 21), PeriodEnd = new DateOnly(2026, 6, 21),
                PublishedAt = new DateOnly(2026, 7, 22), IsComposing = false,
                SourcePayloadChecksum = "feature141-statement-fixture", LastSynchronizedAt = DateTimeOffset.UtcNow
            });
            db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
            {
                Id = Guid.NewGuid(), FinancialStatementId = statementId,
                SourceItemCatalogId = source.SourceItemCatalogId, Value = 3_753m
            });
            await db.SaveChangesAsync();
        }

        var role = "feature141_statement_reader_" + Guid.NewGuid().ToString("N");
        var password = "F141_" + Guid.NewGuid().ToString("N");
        try
        {
            await CreateFinancialStatementRestrictedRoleAsync(database.ConnectionString, role, password);
            var restrictedConnection = new NpgsqlConnectionStringBuilder(database.ConnectionString)
            {
                Username = role, Password = password, Pooling = false
            }.ConnectionString;
            await AssertFinancialStatementRoleIsReadOnlyAsync(restrictedConnection, role);

            await using var bindingDb = database.CreateContext();
            var companyBinder = new StructuredQueryCompanyBinder(new CanonicalQueryEntityResolver(bindingDb,
                Options.Create(new CanonicalEntityResolutionOptions(Enabled: true))));
            const string question = "سود عملیاتی هر سهم کگهر چقدر است؟";
            var financialReference = new FinancialValueCatalogReference(source.CatalogId, source.SourceItemCatalogId,
                source.ProviderName, source.StatementType, source.SourceItemId, "سود عملیاتی هر سهم", null,
                source.SourceUnit, source.NormalizedUnit, source.MetricCode, "verified-persisted-source-key", "approved", true,
                source.SourceUnitStatus);
            var requirements = new QuestionSchemaRequirements(1, snapshot.CatalogVersion, snapshot.CatalogHash,
                [], ["dim_company"], ["company"], source.RelationshipIds, ["default-standalone"], ["latest"], [], ["scalar"],
                StatementScopes: ["standalone"], EntityReferences: ["کگهر"],
                FinancialValueReferences: [new(nameof(FinancialValueReferenceType.DirectSourceItem), source.CatalogId)],
                RequestedFinancialValueOperation: nameof(FinancialValueOperation.DirectValueRetrieval),
                StructuredShape: new StructuredPlanShape([source.CatalogId], [], [], [], [], 0, 1));
            var nodeIds = new[] { "domain-financial-statements" };
            var context = new SchemaContext(1, snapshot.CatalogVersion, snapshot.CatalogHash, nodeIds,
                [new SchemaNodeGovernance(nodeIds[0], true, true, true, true)], [], requirements.DimensionIds,
                requirements.EntityKinds, requirements.RelationshipIds, requirements.SourcePolicies,
                requirements.PeriodPolicies, [], requirements.PresentationRequirements, [], requirements.StatementScopes,
                [], [], requirements.EntityReferences, [financialReference]);
            var navigation = new SchemaNavigationResult(1, snapshot.CatalogVersion, snapshot.CatalogHash,
                [new NavigationCandidate(nodeIds[0], 1, "captured live navigator financial-statement branch")], nodeIds,
                [], requirements.DimensionIds, requirements.EntityKinds, requirements.RelationshipIds,
                requirements.SourcePolicies, requirements.PeriodPolicies, [], [],
                SelectedPresentationRequirements: requirements.PresentationRequirements,
                SelectedStatementScopes: requirements.StatementScopes,
                SelectedEntityReferences: requirements.EntityReferences,
                CompletenessStatus: SchemaContextCompletenessStatus.Complete,
                SelectedFinancialValueReferences: requirements.FinancialValueReferences,
                FinancialValueOperation: requirements.RequestedFinancialValueOperation,
                RetrievalComplete: true, SemanticReviewApproved: true, QueryAuthorized: true);
            var planner = new OfflineStructuredQueryPlanningService(new CapturedRequirementsExtractor(requirements),
                new CapturedNavigator(navigation), new CapturedContextProvider(context),
                new SchemaContextCompletenessValidator(), new StructuredQueryPlanBuilder(),
                new StructuredQueryPlanValidator(), companyBinder);
            var request = new SchemaNavigationRequest(1, question, snapshot.CatalogVersion, snapshot.CatalogHash,
                TenantId: TenantId, CorrelationId: "feature141-statement-postfix-fixture");
            var planning = await planner.PlanAsync(request, CancellationToken.None);
            Assert.True(planning.Validation.IsStructurallyValid, string.Join("|", planning.Validation.Diagnostics));
            Assert.Equal(companyId, planning.CompanyBinding?.CompanyId);

            var approvedPlan = planning.Plan with
                { RetrievalComplete = true, SemanticReviewApproved = true, QueryAuthorized = true, ExecutionEligible = true };
            var compilation = new StructuredQueryCompiler().Compile(approvedPlan, snapshot);
            Assert.True(compilation.Success, string.Join("|", compilation.Diagnostics));
            var query = Assert.IsType<CompiledReadOnlyQuery>(compilation.Query);
            var execution = await ExecuteAsync(query, databaseName, role, restrictedConnection);
            Assert.Equal(StructuredQueryExecutionStatus.Succeeded, execution.Status);
            Assert.Equal(3_753m, Convert.ToDecimal(Assert.Single(execution.Rows)["Value"]));
            var response = new PersianStructuredQueryResponseGenerator().Generate(new(approvedPlan, execution));
            Assert.Equal(StructuredAnswerStatus.Answered, response.Status);
            Assert.Contains("۳٬۷۵۳", response.TextAnswer, StringComparison.Ordinal);

            var root = FindRepositoryRoot();
            var artifact = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts",
                LiveArtifactFileName("financial-statement-generic-pipeline-2026-10-09.json"));
            await File.WriteAllTextAsync(artifact, System.Text.Json.JsonSerializer.Serialize(new
            {
                caseId = "financial-statement-operating-profit-per-share", question,
                executionMode = "captured-navigation-plus-isolated-postgresql-fixture",
                catalogVersion = snapshot.CatalogVersion, catalogHash = snapshot.CatalogHash,
                companyBinding = planning.CompanyBinding, plan = approvedPlan,
                sqlExecuted = query.Sql, sqlParameters = query.Parameters,
                executionStatus = execution.Status.ToString(), observedRows = execution.Rows,
                expectedValue = 3_753m, actualValue = Convert.ToDecimal(execution.Rows[0]["Value"]),
                effectiveUnit = execution.EffectiveUnit, finalPersianAnswer = response.TextAnswer,
                evidence = response.Evidence, secretValuesLogged = false
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            await DropFinancialStatementRoleAsync(database.ConnectionString, role);
        }
    }

    private static StructuredQueryCatalogSnapshot RuntimeSnapshot()
    {
        var root = FindRepositoryRoot();
        var catalog = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var hierarchy = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        return new StructuredQueryCatalogSnapshotBuilder().Build(catalog, hierarchy);
    }

    private static StructuredQueryCatalogSnapshot ApprovedFixtureSnapshot()
    {
        var original = RuntimeSnapshot();
        const string lineTable = "tbl_8ecd0432a4292998";
        const string reportTable = "tbl_8e759d70dada6bd8";
        const string companyTable = "tbl_8646d83cdb350f92";
        return new StructuredQueryCatalogSnapshot(original.CatalogVersion, original.CatalogHash, true,
            original.Tables.Select(x => x.CatalogId is lineTable or reportTable or companyTable
                ? x with { Queryable = true, SemanticApproved = true } : x).ToArray(),
            original.Columns.Select(x => x.TableCatalogId is lineTable or reportTable or companyTable
                ? x with { Queryable = true } : x).ToArray(),
            original.Relationships.Select(x => x.CatalogId is "rel_31e410fb3ae11956" or "rel_71eca79d3b7d6aa2"
                ? x with { Queryable = true, SemanticApproved = true, QueryAuthorized = true } : x).ToArray(),
            original.DirectSources,
            original.MonthlyMeasures.Select(x => x with
                { Queryable = true, SemanticApproved = true, UnitVerified = true }).ToArray());
    }

    private static StructuredQueryCatalogSnapshot ApprovedFinancialStatementFixtureSnapshot()
    {
        var original = RuntimeSnapshot();
        var source = original.DirectSources.Single(item => item.ProviderName == Provider
            && item.StatementType == "IncomeStatement" && item.SourceItemId == 161);
        var tableIds = new[] { source.SourceItemsTableCatalogId, source.LineItemsTableCatalogId,
            source.StatementsTableCatalogId, source.CompaniesTableCatalogId }.ToHashSet(StringComparer.Ordinal);
        var relationshipIds = source.RelationshipIds.ToHashSet(StringComparer.Ordinal);
        return new StructuredQueryCatalogSnapshot(original.CatalogVersion, original.CatalogHash, true,
            original.Tables.Select(item => tableIds.Contains(item.CatalogId)
                ? item with { Queryable = true, SemanticApproved = true } : item).ToArray(),
            original.Columns.Select(item => tableIds.Contains(item.TableCatalogId)
                ? item with { Queryable = true } : item).ToArray(),
            original.Relationships.Select(item => relationshipIds.Contains(item.CatalogId)
                ? item with { Queryable = true, SemanticApproved = true, QueryAuthorized = true } : item).ToArray(),
            original.DirectSources.Select(item => item.CatalogId == source.CatalogId
                ? item with { Queryable = true, SemanticApproved = true } : item).ToArray(),
            original.MonthlyMeasures);
    }

    private static async Task RunLiveNaturalLanguageFixtureQueryAsync(
        StructuredQueryCatalogSnapshot snapshot, StructuredQueryCompiler compiler,
        IStructuredQueryCompanyBinder companyBinder, IStructuredQueryProductBinder productBinder,
        string databaseName, string role, string restrictedConnection, Guid expectedCompanyId, string expectedProductKey,
        Guid kgelCompanyId, string kgelProductKey, IReadOnlyList<Guid> discoveryCompanyIds, Guid unauthorizedProducerId)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FEATURE141_RUN_LIVE_LLM_POSTGRES"), "1", StringComparison.Ordinal))
            return;
        var root = FindRepositoryRoot();
        var apiDirectory = Path.Combine(root, "src", "backend", "FinancialCopilot.API");
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var configuration = EvaluationConfigurationLoader.Build(apiDirectory, "Development", configurationOverrides:
            new Dictionary<string, string?>
            {
                ["Feature141:Enabled"] = "true",
                ["Feature141:SchemaNavigator:HierarchyPath"] = hierarchyPath,
                ["Feature141:SchemaNavigator:CatalogPath"] = catalogPath
            });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddFinancialCopilotInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        var modelRegistry = provider.GetRequiredService<IAiProviderCapabilityRegistry>();
        var available = modelRegistry.GetAvailableProviders(TenantId)
            .Where(item => (item.Capabilities & AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.SchemaNavigation)) ==
                AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.SchemaNavigation)).ToArray();
        if (available.Length == 0)
            throw new InvalidOperationException("live_llm_to_postgresql_fixture_blocked:no_tenant_eligible_structured_output_provider");

        var requirementsExtractor = provider.GetRequiredService<IQuestionSchemaRequirementsExtractor>();
        var navigator = provider.GetRequiredService<IVectorlessSchemaNavigator>();
        var fixtureContext = BuildExplicitFixtureContext(snapshot);
        var planner = new OfflineStructuredQueryPlanningService(requirementsExtractor, navigator,
            new CapturedContextProvider(fixtureContext), new SchemaContextCompletenessValidator(),
            new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(), companyBinder, productBinder,
            productConceptResolver: provider.GetRequiredService<IReviewedProductConceptResolver>(),
            companyDiscoveryScopeResolver: new AuthorizedCompanyDiscoveryScopeResolver([
                new AuthorizedCompanyDiscoveryScope(TenantId, "feature141-isolated-tenant-company-scope",
                    "owner-approved-isolated-fixture-scope", CompanyCoverageComplete: true)
            ]));
        var question = "مقدار تولید گندله کگهر در شهریور ۱۴۰۵ چقدر بوده؟";
        var request = new SchemaNavigationRequest(1, question, snapshot.CatalogVersion, snapshot.CatalogHash,
            TenantId: TenantId, CorrelationId: "feature141-live-llm-postgresql-fixture");
        var planning = await planner.PlanAsync(request, CancellationToken.None);
        var artifactPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts",
            LiveArtifactFileName("monthly-production-sales-live-llm-postgresql-readiness-v2-diagnostic-2026-10-09.json"));
        await File.WriteAllTextAsync(artifactPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            status = "pipeline_diagnostic", provider = available[0].ProviderKey, model = available[0].ModelKey,
            question, measureIds = planning.Requirements.MeasureIds,
            financialValueReferences = planning.Requirements.FinancialValueReferences,
            companyReferences = planning.Requirements.EntityReferences,
            productReferences = planning.Requirements.ProductReferences,
            entityKinds = planning.Requirements.EntityKinds, relationshipIds = planning.Requirements.RelationshipIds,
            periodPolicies = planning.Requirements.PeriodPolicies,
            reportingPeriodStart = planning.Requirements.StructuredShape?.ReportingPeriodStart,
            reportingPeriodEnd = planning.Requirements.StructuredShape?.ReportingPeriodEnd,
            comparisonMode = planning.Requirements.StructuredShape?.ComparisonMode.ToString(),
            filters = planning.Requirements.StructuredShape?.Filters,
            aggregations = planning.Requirements.StructuredShape?.Aggregations,
            sources = planning.Plan.Sources, planEntities = planning.Plan.Entities,
            planPeriod = planning.Plan.Period, planFinancialOperation = planning.Plan.FinancialOperation,
            planProjection = planning.Plan.Projection, planRelationships = planning.Plan.RelationshipIds,
            navigationSelectedMeasureIds = planning.Navigation.SelectedMeasureIds,
            navigationSelectedRelationships = planning.Navigation.SelectedRelationshipIds,
            missing = planning.Navigation.MissingContext, planDiagnostics = planning.Plan.Diagnostics,
            validationDiagnostics = planning.Validation.Diagnostics, companyBinding = planning.CompanyBinding,
            productBinding = planning.ProductBinding, modelCalls = planning.Navigation.ModelCalls,
            inputTokens = planning.Navigation.InputTokens, outputTokens = planning.Navigation.OutputTokens,
            latencyMs = (long)(planning.Navigation.ModelLatency?.TotalMilliseconds ?? 0), secretValuesLogged = false
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var expectedMeasure = "metric_monthly_production_quantity";
        if (!planning.Requirements.MeasureIds.SequenceEqual([expectedMeasure], StringComparer.Ordinal)
            || !planning.Navigation.SelectedMeasureIds.SequenceEqual([expectedMeasure], StringComparer.Ordinal))
            throw new InvalidOperationException($"live_llm_to_postgresql_fixture_measure_mismatch:expected={expectedMeasure};actual={string.Join(',', planning.Navigation.SelectedMeasureIds)}");
        if (planning.CompanyBinding?.Status != StructuredCompanyBindingStatus.Found
            || planning.CompanyBinding.CompanyId != expectedCompanyId)
            throw new InvalidOperationException($"live_llm_to_postgresql_fixture_company_binding_failed:{planning.CompanyBinding?.DiagnosticCode}");
        if (planning.ProductBinding?.Status != StructuredProductBindingStatus.Found
            || planning.ProductBinding.ProductKey != expectedProductKey)
            throw new InvalidOperationException($"live_llm_to_postgresql_fixture_product_binding_failed:{planning.ProductBinding?.DiagnosticCode}");
        if (!planning.Validation.IsStructurallyValid)
            throw new InvalidOperationException($"live_llm_to_postgresql_fixture_plan_validation_failed:{string.Join('|', planning.Validation.Diagnostics)}");

        // This explicit approval applies only to the isolated test catalog and read-only fixture role.
        var fixtureAuthorizedPlan = planning.Plan with
        { RetrievalComplete = true, SemanticReviewApproved = true, QueryAuthorized = true, ExecutionEligible = true };
        var compilation = compiler.Compile(fixtureAuthorizedPlan, snapshot);
        Assert.True(compilation.Success, string.Join("|", compilation.Diagnostics));
        var query = Assert.IsType<CompiledReadOnlyQuery>(compilation.Query);
        var execution = await ExecuteAsync(query, databaseName, role, restrictedConnection);
        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, execution.Status);
        var actual = Convert.ToDecimal(Assert.Single(execution.Rows)["Value"]);
        Assert.Equal(47m, actual);

        await File.WriteAllTextAsync(artifactPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow), caseId = "monthly-02-production-product-month",
            status = "succeeded", provider = available[0].ProviderKey, model = available[0].ModelKey,
            question, expectedMeasure, selectedMeasureIds = planning.Navigation.SelectedMeasureIds,
            candidateMeasureIds = planning.Navigation.CandidateMeasureIds,
            companyBindingStatus = planning.CompanyBinding.Status.ToString(), companyId = planning.CompanyBinding.CompanyId,
            productBindingStatus = planning.ProductBinding.Status.ToString(), productKey = planning.ProductBinding.ProductKey,
            completeness = planning.Navigation.CompletenessStatus?.ToString(), planValidation = planning.Validation.IsStructurallyValid,
            sqlCompilerSupported = true, postgresExecution = execution.Status.ToString(), expectedValue = 47m, actualValue = actual,
            unit = execution.EffectiveUnit, queryFingerprint = execution.QueryFingerprint,
            authorizationProvenance = execution.AuthorizationProvenance, modelCalls = planning.Navigation.ModelCalls,
            inputTokens = planning.Navigation.InputTokens, outputTokens = planning.Navigation.OutputTokens,
            latencyMs = (long)(planning.Navigation.ModelLatency?.TotalMilliseconds ?? 0), secretValuesLogged = false
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var populationQuestion = "کدام شرکت‌ها گندله تولید می‌کنند؟";
        var populationPlanner = new OfflineStructuredQueryPlanningService(requirementsExtractor, navigator,
            new CapturedContextProvider(fixtureContext), new SchemaContextCompletenessValidator(),
            new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(), companyBinder, productBinder,
            productConceptResolver: provider.GetRequiredService<IReviewedProductConceptResolver>(),
            companyDiscoveryScopeResolver: new AuthorizedCompanyDiscoveryScopeResolver([
                new AuthorizedCompanyDiscoveryScope(TenantId, "feature141-isolated-tenant-company-scope",
                    "owner-approved-isolated-fixture-scope", CompanyCoverageComplete: true)
            ]));
        var populationRequest = new SchemaNavigationRequest(1, populationQuestion, snapshot.CatalogVersion,
            snapshot.CatalogHash, TenantId: TenantId, CorrelationId: "feature141-live-company-population-fixture");
        var populationPlanning = await populationPlanner.PlanAsync(populationRequest, CancellationToken.None);
        Assert.True(populationPlanning.Validation.IsStructurallyValid,
            $"validation={string.Join("|", populationPlanning.Validation.Diagnostics)}; requirements={string.Join("|", populationPlanning.Requirements.NormalizationDiagnostics)}; " +
            $"selectedMeasures={string.Join("|", populationPlanning.Navigation.SelectedMeasureIds)}; missing={string.Join("|", populationPlanning.Navigation.MissingContext)}; " +
            $"completeness={populationPlanning.Navigation.CompletenessStatus}");
        Assert.DoesNotContain(populationPlanning.Plan.Entities,
            entity => entity.EntityKind.Equals("Company", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("product_iron_ore_pellets", populationPlanning.Plan.ProductConcept!.CatalogId);
        var approvedPopulationPlan = populationPlanning.Plan with
        { RetrievalComplete = true, SemanticReviewApproved = true, QueryAuthorized = true, ExecutionEligible = true };
        var populationCompilation = compiler.Compile(approvedPopulationPlan, snapshot);
        Assert.True(populationCompilation.Success, string.Join("|", populationCompilation.Diagnostics));
        var populationQuery = Assert.IsType<CompiledReadOnlyQuery>(populationCompilation.Query);
        Assert.Empty(populationQuery.CompanyIds);
        var populationExecution = await ExecuteAsync(populationQuery, databaseName, role, restrictedConnection,
            discoveryCompanyIds);
        Assert.Equal(StructuredQueryExecutionStatus.Incomplete, populationExecution.Status);
        Assert.Equal(new[] { kgelCompanyId, expectedCompanyId,
                discoveryCompanyIds.Single(id => populationExecution.Rows.Any(row =>
                    Equals(row.GetValueOrDefault("CompanyId"), id) && Convert.ToDecimal(row["Value"]) == 62m)),
                discoveryCompanyIds.Single(id => populationExecution.Rows.Any(row =>
                    Equals(row.GetValueOrDefault("CompanyId"), id) && Convert.ToDecimal(row["Value"]) == 80m)) }.OrderBy(id => id),
            populationExecution.Rows.Select(row => (Guid)row["CompanyId"]!).OrderBy(id => id));
        Assert.DoesNotContain(populationExecution.Rows, row => Equals(row.GetValueOrDefault("CompanyId"), unauthorizedProducerId));
        var populationResponseGenerator = new PersianStructuredQueryResponseGenerator();
        var populationAnswer = populationResponseGenerator.Generate(new(approvedPopulationPlan, populationExecution));
        Assert.Equal(StructuredAnswerStatus.Incomplete, populationAnswer.Status);
        Assert.Contains("\u06af\u0646\u062f\u0644\u0647", populationAnswer.TextAnswer);
        Assert.Contains("\u062c\u0632\u0626\u06cc", populationAnswer.TextAnswer);

        var productConcept = provider.GetRequiredService<IReviewedProductConceptResolver>().Resolve("\u06af\u0646\u062f\u0644\u0647");
        Assert.Equal(ProductConceptResolutionStatus.Resolved, productConcept.Status);
        var productSalesQuantity = Assert.Single(snapshot.MonthlyMeasures,
            measure => measure.CatalogId == "metric_monthly_sales_quantity");
        var producerSalesPlan = ProducerSalesPlan(snapshot, productSalesQuantity, productConcept,
            PopulationSalesScope.RequestedProductSales);
        var producerSalesCompilation = compiler.Compile(producerSalesPlan, snapshot);
        Assert.True(producerSalesCompilation.Success, string.Join("|", producerSalesCompilation.Diagnostics));
        var producerSalesQuery = Assert.IsType<CompiledReadOnlyQuery>(producerSalesCompilation.Query);
        Assert.Empty(producerSalesQuery.CompanyIds);
        Assert.Contains("WITH producers AS", producerSalesQuery.Sql, StringComparison.Ordinal);
        Assert.Contains("eligible_sales AS", producerSalesQuery.Sql, StringComparison.Ordinal);
        Assert.Contains(producerSalesQuery.Parameters, parameter => parameter.Name == "product_aliases"
            && parameter.Value is string[] aliases && aliases.SequenceEqual(productConcept.ApprovedAliases));
        var producerSalesExecution = await ExecuteAsync(producerSalesQuery, databaseName, role,
            restrictedConnection, discoveryCompanyIds);
        Assert.Equal(StructuredQueryExecutionStatus.Incomplete, producerSalesExecution.Status);
        Assert.Equal(new[] { 55m, 40m, 20m, 0m }, producerSalesExecution.Rows
            .Select(row => Convert.ToDecimal(row["Value"])));
        Assert.Equal(new[] { discoveryCompanyIds.Single(id => producerSalesExecution.Rows.Any(row =>
                Equals(row["CompanyId"], id) && Convert.ToDecimal(row["Value"]) == 55m)),
                expectedCompanyId, kgelCompanyId, discoveryCompanyIds.Single(id => producerSalesExecution.Rows.Any(row =>
                    Equals(row["CompanyId"], id) && Convert.ToDecimal(row["Value"]) == 0m)) },
            producerSalesExecution.Rows.Select(row => (Guid)row["CompanyId"]!));
        Assert.DoesNotContain(producerSalesExecution.Rows, row => Equals(row.GetValueOrDefault("CompanyId"), unauthorizedProducerId));
        var producerSalesAnswer = populationResponseGenerator.Generate(new(producerSalesPlan, producerSalesExecution));
        // Owner decision 2026-10-30: approved-aliases-only product coverage still ranks the observed companies, labelled partial.
        Assert.Equal(StructuredAnswerStatus.Incomplete, producerSalesAnswer.Status);
        Assert.Contains("رتبهٔ ۱:", producerSalesAnswer.TextAnswer, StringComparison.Ordinal);
        Assert.Contains("۵۵", producerSalesAnswer.TextAnswer, StringComparison.Ordinal);
        Assert.Contains("پوشش کامل بازار را نشان نمی‌دهد", producerSalesAnswer.TextAnswer, StringComparison.Ordinal);

        var totalSales = Assert.Single(snapshot.MonthlyMeasures, measure => measure.CatalogId == "metric_monthly_sales");
        var totalProducerSalesPlan = ProducerSalesPlan(snapshot, totalSales, productConcept,
            PopulationSalesScope.TotalCompanySales);
        var totalProducerSalesCompilation = compiler.Compile(totalProducerSalesPlan, snapshot);
        Assert.True(totalProducerSalesCompilation.Success, string.Join("|", totalProducerSalesCompilation.Diagnostics));
        var totalProducerSalesQuery = Assert.IsType<CompiledReadOnlyQuery>(totalProducerSalesCompilation.Query);
        var totalProducerSalesExecution = await ExecuteAsync(totalProducerSalesQuery, databaseName, role,
            restrictedConnection, discoveryCompanyIds);
        Assert.Equal(StructuredQueryExecutionStatus.Incomplete, totalProducerSalesExecution.Status);
        Assert.Equal(new[] { 900m, 850m, 700m, 0m }, totalProducerSalesExecution.Rows
            .Select(row => Convert.ToDecimal(row["Value"])));

        var populationArtifactPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts",
            LiveArtifactFileName("monthly-company-population-discovery-live-llm-postgresql-readiness-v2.json"));
        await File.WriteAllTextAsync(populationArtifactPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            status = "succeeded_with_partial_coverage", question = populationQuestion, executionMode = "live-llm-plus-isolated-postgresql",
            provider = available[0].ProviderKey, model = available[0].ModelKey,
            measureIds = populationPlanning.Navigation.SelectedMeasureIds,
            productConcept = populationPlanning.Plan.ProductConcept,
            companyReferences = populationPlanning.Requirements.EntityReferences,
            planEntities = populationPlanning.Plan.Entities, periodPolicy = populationPlanning.Plan.PopulationCoveragePolicyId,
            reportingWindowStart = populationPlanning.Plan.Period.ReportingPeriodStart,
            reportingWindowEnd = populationPlanning.Plan.Period.ReportingPeriodEnd,
            companyScope = approvedPopulationPlan.CompanyDiscoveryScope,
            postgresStatus = populationExecution.Status.ToString(), diagnostic = populationExecution.DiagnosticCode,
            sqlExecuted = true, compiledSql = populationQuery.Sql, sqlParameters = populationQuery.Parameters,
            compilerDiagnostics = populationCompilation.Diagnostics,
            queryPlan = approvedPopulationPlan, sourceCatalogIds = populationQuery.SourceCatalogIds,
            relationshipCatalogIds = populationQuery.RelationshipCatalogIds,
            physicalObjectCatalogIds = populationQuery.PhysicalObjectCatalogIds,
            rows = populationExecution.Rows, expectedCompanyIds = new[] { kgelCompanyId, expectedCompanyId,
                discoveryCompanyIds.Single(id => populationExecution.Rows.Any(row => Equals(row.GetValueOrDefault("CompanyId"), id)
                    && Convert.ToDecimal(row["Value"]) == 62m)),
                discoveryCompanyIds.Single(id => populationExecution.Rows.Any(row => Equals(row.GetValueOrDefault("CompanyId"), id)
                    && Convert.ToDecimal(row["Value"]) == 80m)) }.OrderBy(id => id),
            exactCompanySetMatch = populationExecution.Rows.Count == 4,
            finalPersianAnswer = populationAnswer.TextAnswer, evidence = populationAnswer.Evidence,
            queryFingerprint = populationExecution.QueryFingerprint,
            modelCalls = populationPlanning.Navigation.ModelCalls,
            inputTokens = populationPlanning.Navigation.InputTokens, outputTokens = populationPlanning.Navigation.OutputTokens,
            latencyMs = (long)(populationPlanning.Navigation.ModelLatency?.TotalMilliseconds ?? 0), secretValuesLogged = false
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        if (!string.Equals(Environment.GetEnvironmentVariable("FEATURE141_RUN_LIVE_LLM_MATRIX"), "1", StringComparison.Ordinal))
            return;
        var datasetPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts",
            "monthly-production-sales-evaluation.json");
        using var dataset = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(datasetPath));
        var questions = dataset.RootElement.GetProperty("cases").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetString()!,
                item => item.GetProperty("question").GetString()!, StringComparer.Ordinal);
        questions["monthly-14-production-concentrate-explicit-month"] =
            "مقدار تولید کنسانتره کگل در شهریور ۱۴۰۵ چقدر بوده؟";
        questions["producer-16-total-company-sales-ranking"] =
            "کدام شرکت‌های تولیدکننده گندله بیشترین فروش کل ماهانه را داشته‌اند؟";
        var cases = new (string Id, string Measure, string? Company, string? Product, Guid? CompanyId,
            string? ProductKey, decimal[]? ExpectedValues)[]
        {
            ("monthly-02-production-product-month", "metric_monthly_production_quantity", "کگهر", "گندله",
                expectedCompanyId, expectedProductKey, [47m]),
            ("monthly-03-production-mom-growth", "metric_monthly_production_quantity", "کگل", "کنسانتره",
                kgelCompanyId, ProductKey("feature141-kgel", "\u06a9\u0646\u0633\u0627\u0646\u062a\u0631\u0647"), null),
            ("monthly-04-production-top-products-growth", "metric_monthly_production_quantity", "کگهر", null,
                expectedCompanyId, null, null),
            ("monthly-05-sales-amount-month", "metric_monthly_sales", "کگل", null,
                kgelCompanyId, null, [850m]),
            ("monthly-06-sales-quantity-product-latest", "metric_monthly_sales_quantity", "کگهر", "گندله",
                expectedCompanyId, expectedProductKey, [40m]),
            ("monthly-08-sales-six-month-trend", "metric_monthly_sales", "کگل", null,
                kgelCompanyId, null, [120m, 150m, 200m, 250m, 300m, 850m]),
            ("monthly-09-cross-company-product-sales", "metric_monthly_sales_quantity", null, "گندله",
                null, null, null),
            ("monthly-10-company-sales-comparison", "metric_monthly_sales", "کگل و کگهر", null,
                null, null, [850m, 700m]),
            ("monthly-14-production-concentrate-explicit-month", "metric_monthly_production_quantity", "کگل",
                "کنسانتره", kgelCompanyId, ProductKey("feature141-kgel", "کنسانتره"), [12m]),
            ("monthly-01-production-latest", "metric_monthly_production_quantity", "کگل", null,
                kgelCompanyId, null, null),
            ("monthly-07-sales-rate-two-months", "metric_monthly_sales_rate", "کگهر", "گندله",
                expectedCompanyId, expectedProductKey, null),
            ("monthly-11-industry-sales-growth-ranking", "metric_monthly_sales_growth_mom", null, null,
                null, null, null),
            ("monthly-12-sales-vs-operating-profit-growth", "metric_monthly_sales_growth_mom", "کگل", null,
                kgelCompanyId, null, null),
            ("monthly-13-production-vs-sales-growth", "metric_monthly_production_quantity", "کگهر", null,
                expectedCompanyId, null, null),
            ("producer-16-total-company-sales-ranking", "metric_monthly_sales", null, "گندله",
                null, null, null)
        };
        var casesArtifact = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts",
            LiveArtifactFileName("monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl"));
        var responseGenerator = new PersianStructuredQueryResponseGenerator();
        await using var caseOutput = new StreamWriter(casesArtifact, append: false);
        foreach (var liveCase in cases)
        {
            var incorrectNumericAnswer = false;
            var caseQuestion = questions[liveCase.Id];
            var caseRequest = new SchemaNavigationRequest(1, caseQuestion, snapshot.CatalogVersion, snapshot.CatalogHash,
                TenantId: TenantId, CorrelationId: $"feature141-live-fixture-{liveCase.Id}");
            object diagnostic;
            try
            {
                var casePlan = await planner.PlanAsync(caseRequest, CancellationToken.None);
                var stage = "requirements_or_measure_selection";
                var status = "typed_failure";
                string? compilerCode = null;
                string? executionStatus = null;
                string? compiledSql = null;
                IReadOnlyList<StructuredQueryParameter>? compiledParameters = null;
                IReadOnlyList<IReadOnlyDictionary<string, object?>>? observedRows = null;
                PersianStructuredQueryResponse? generatedResponse = null;
                decimal[]? actualValues = null;
                decimal[]? actualComparisonValues = null;
                var expectedGrowthPercentage = liveCase.Id switch
                {
                    "monthly-03-production-mom-growth" => 100m,
                    "monthly-04-production-top-products-growth" => (47m - 35m) / 35m * 100m,
                    _ => (decimal?)null
                };
                if (!casePlan.Requirements.MeasureIds.SequenceEqual([liveCase.Measure], StringComparer.Ordinal)
                    || !casePlan.Navigation.SelectedMeasureIds.SequenceEqual([liveCase.Measure], StringComparer.Ordinal))
                {
                    status = casePlan.Requirements.HasUnknownOrAmbiguousRequirements ? "typed_ambiguity" : "measure_selection_mismatch";
                }
                else if ((liveCase.CompanyId is not null && (casePlan.CompanyBinding?.Status != StructuredCompanyBindingStatus.Found
                         || casePlan.CompanyBinding.CompanyId != liveCase.CompanyId))
                    || (liveCase.ProductKey is not null && (casePlan.ProductBinding?.Status != StructuredProductBindingStatus.Found
                         || casePlan.ProductBinding.ProductKey != liveCase.ProductKey)))
                {
                    stage = "identity_binding";
                    status = "typed_identity_failure";
                }
                else if (!casePlan.Validation.IsStructurallyValid)
                {
                    stage = "planning_validation";
                    status = "typed_plan_failure";
                }
                else
                {
                    stage = "sql_compilation";
                    var approvedFixturePlan = casePlan.Plan with
                    { RetrievalComplete = true, SemanticReviewApproved = true, QueryAuthorized = true, ExecutionEligible = true,
                        FinancialOperation = casePlan.Plan.FinancialOperation with
                        { FormulaAuthorized = casePlan.Plan.FinancialOperation.Operation == FinancialValueOperation.ApprovedPeriodComparison } };
                    var compiled = compiler.Compile(approvedFixturePlan, snapshot);
                    compilerCode = string.Join('|', compiled.Diagnostics);
                    if (!compiled.Success || compiled.Query is not CompiledReadOnlyQuery caseQuery)
                    {
                        status = "typed_compiler_failure";
                    }
                    else
                    {
                        compiledSql = caseQuery.Sql;
                        compiledParameters = caseQuery.Parameters;
                        stage = "restricted_postgresql_execution";
                        var executed = await ExecuteAsync(caseQuery, databaseName, role, restrictedConnection,
                            casePlan.Plan.CompanyDiscoveryScope is null ? null : discoveryCompanyIds);
                        executionStatus = executed.Status.ToString();
                        observedRows = executed.Rows;
                        generatedResponse = responseGenerator.Generate(new(casePlan.Plan, executed));
                        if (executed.Status == StructuredQueryExecutionStatus.Succeeded)
                        {
                            actualValues = executed.Rows.Select(row => Convert.ToDecimal(row["Value"])).ToArray();
                            if (expectedGrowthPercentage.HasValue)
                                actualComparisonValues = executed.Rows.Where(row => row.TryGetValue("GrowthPercentage", out _))
                                    .Select(row => row["GrowthPercentage"] is null ? decimal.MinValue
                                        : Convert.ToDecimal(row["GrowthPercentage"])).ToArray();
                            var expectedValuesMatch = liveCase.ExpectedValues is null
                                || (liveCase.Id == "monthly-10-company-sales-comparison"
                                    ? liveCase.ExpectedValues.Order().SequenceEqual(actualValues.Order())
                                        && Equals(Assert.Single(executed.Rows, row => Equals(row["CompanyId"], kgelCompanyId))["Value"], 850m)
                                        && Equals(Assert.Single(executed.Rows, row => Equals(row["CompanyId"], expectedCompanyId))["Value"], 700m)
                                    : liveCase.ExpectedValues.SequenceEqual(actualValues));
                            if (!expectedValuesMatch)
                            {
                                status = "incorrect_numeric_answer";
                                incorrectNumericAnswer = true;
                            }
                            else if (expectedGrowthPercentage.HasValue
                                && !actualComparisonValues!.Any(actual => liveCase.Id == "monthly-04-production-top-products-growth"
                                    ? actual != decimal.MinValue && Math.Abs(actual - expectedGrowthPercentage.Value) < 0.0001m
                                    : actual == expectedGrowthPercentage.Value))
                            {
                                status = "incorrect_numeric_answer";
                                incorrectNumericAnswer = true;
                            }
                            else
                                status = "succeeded";
                        }
                        else status = "typed_execution_failure";
                    }
                }

                if (generatedResponse is null && status != "succeeded")
                    generatedResponse = responseGenerator.Generate(new(casePlan.Plan, null, stage,
                        casePlan.Validation.Diagnostics));

                diagnostic = new
                {
                    caseId = liveCase.Id, question = caseQuestion, status, failureStage = status == "succeeded" ? null : stage,
                    finalAnswerStatus = generatedResponse?.Status.ToString(), finalPersianAnswer = generatedResponse?.TextAnswer,
                    finalAnswerEvidence = generatedResponse?.Evidence,
                    executionMode = "live LLM + isolated PostgreSQL fixture",
                    provider = available[0].ProviderKey, model = available[0].ModelKey,
                    expectedMeasureIds = new[] { liveCase.Measure }, selectedMeasureIds = casePlan.Navigation.SelectedMeasureIds,
                    requiredMeasureIds = casePlan.Navigation.RequiredMeasureIds,
                    candidateMeasureIds = casePlan.Navigation.CandidateMeasureIds,
                    incidentalMeasureIds = (casePlan.Navigation.CandidateMeasureIds ?? []).Except(
                        casePlan.Navigation.SelectedMeasureIds.Concat(casePlan.Navigation.RequiredMeasureIds ?? []), StringComparer.Ordinal),
                    companyBinding = casePlan.CompanyBinding, productBinding = casePlan.ProductBinding,
                    operation = casePlan.Requirements.RequestedFinancialValueOperation,
                    operationKind = casePlan.Plan.FinancialOperation?.Kind.ToString(),
                    comparisonMode = casePlan.Plan.FinancialOperation?.ComparisonMode.ToString(),
                    requestedOperation = casePlan.Requirements.RequestedFinancialValueOperation,
                    requirementShape = casePlan.Requirements.StructuredShape,
                    comparisonScopes = casePlan.Requirements.ComparisonScopes,
                    planPeriod = casePlan.Plan.Period,
                    normalizationDiagnostics = casePlan.Requirements.NormalizationDiagnostics,
                    presentation = casePlan.Plan.Presentation.ToString(),
                    plan = casePlan.Plan, semanticReferences = casePlan.Plan.Sources,
                    sqlExecuted = compiledSql, sqlParameters = compiledParameters, observedRows,
                    validationDiagnostics = casePlan.Validation.Diagnostics, compilerDiagnostics = compilerCode,
                    executionStatus, expectedValues = liveCase.ExpectedValues, actualValues,
                    expectedGrowthPercentage, actualComparisonValues,
                    modelCalls = casePlan.Navigation.ModelCalls, inputTokens = casePlan.Navigation.InputTokens,
                    outputTokens = casePlan.Navigation.OutputTokens,
                    latencyMs = (long)(casePlan.Navigation.ModelLatency?.TotalMilliseconds ?? 0), secretValuesLogged = false
                };
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostic = new { caseId = liveCase.Id, question = caseQuestion, status = "typed_pipeline_failure",
                    failureStage = "pipeline", failureCode = exception.GetType().Name, diagnostic = exception.Message,
                    expectedMeasureIds = new[] { liveCase.Measure }, secretValuesLogged = false };
            }
            catch (TaskCanceledException exception)
            {
                diagnostic = new { caseId = liveCase.Id, question = caseQuestion, status = "blocked",
                    failureStage = "llm_inference", failureCode = exception.GetType().Name,
                    diagnostic = "The live model request timed out or its transport was canceled.",
                    expectedMeasureIds = new[] { liveCase.Measure }, secretValuesLogged = false };
            }
            await caseOutput.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(diagnostic));
            await caseOutput.FlushAsync();
            if (incorrectNumericAnswer)
                throw new InvalidOperationException($"Feature 141 live fixture returned an incorrect numeric answer for {liveCase.Id}.");
        }
    }

    private static SchemaContext BuildExplicitFixtureContext(StructuredQueryCatalogSnapshot snapshot)
    {
        var nodes = new[] { "domain-monthly-activity" };
        return new SchemaContext(1, snapshot.CatalogVersion, snapshot.CatalogHash, nodes,
            nodes.Select(id => new SchemaNodeGovernance(id, true, true, true, true)).ToArray(),
            snapshot.MonthlyMeasures.Select(item => item.CatalogId).ToArray(),
            ["dim_company", "dim_monthly_period", "dim_product_identity"], ["company", "product"],
            snapshot.Relationships.Select(item => item.CatalogId).ToArray(),
            ["default-standalone"], ["latest", "explicit-period"], ["period-over-period", "year-over-year", "compare"],
            ["table", "chart", "scalar", "time-series", "ranked-table", "summary"], [],
            ["standalone", "consolidated"], ["aggregate"], ["sort", "rank"],
            ["کگل", "کگهر", "گندله", "گندله کگهر", "کگهر گندله"],
            MeasureRelationshipIds: snapshot.MonthlyMeasures.ToDictionary(item => item.CatalogId,
                item => (IReadOnlyList<string>)item.RelationshipIds, StringComparer.Ordinal));
    }

    private static StructuredQueryPlan Plan(StructuredQueryCatalogSnapshot snapshot, StructuredSqlMonthlyMeasure measure,
        Guid companyId, PlanPresentation presentation, bool latest, string? productCode)
    {
        var entities = productCode is null
            ? new[] { new PlanEntityReference("Company", "monthly fixture company", companyId) }
            : new[] { new PlanEntityReference("Company", "monthly fixture company", companyId),
                new PlanEntityReference("Product", "pellet", ResolvedEntityKey: productCode,
                    IdentityProvenance: "feature-129-company-scoped-product-row", IdentityVerified: true) };
        // A named company's product is selected by its source labels, as the product binder supplies them.
        var selection = productCode is null ? null : new MonthlyProductSelection([FixtureTitle(productCode)], ["ton"]);
        return new(1, snapshot.CatalogVersion, snapshot.CatalogHash, TenantId,
            entities,
            [new PlanSourceReference(FinancialValueReferenceType.MonthlyMeasure, measure.CatalogId, null, null, null, null,
                measure.CatalogId, measure.UnitCode, measure.UnitKind, "catalog-mapped", "fixture-approved", true)],
            measure.RelationshipIds, [new PlanProjection(measure.CatalogId, PlanProjectionRole.Value)], [],
            new PlanPeriodSelection(latest ? ["latest"] : [], [], latest, false),
            new PlanStatementSelection([], [], [], [], false, true, true, true, true, true),
            [], [], new PlanPagination(0, 20), presentation,
            new PlanFinancialOperation(FinancialValueOperation.DirectValueRetrieval, PlanFinancialOperationKind.DirectValue,
                PlanComparisonMode.None, [], false), true, true, true, false, [], ProductSelection: selection);
    }

    private static string Render(StructuredQueryExecutionResult result) =>
        $"{result.Status}:{result.DiagnosticCode}:" + System.Text.Json.JsonSerializer.Serialize(result.Rows);

    private static async Task<OfflineStructuredQueryPlanningResult> ReplayCapturedQuestionAsync(
        StructuredQueryCatalogSnapshot snapshot, StructuredSqlMonthlyMeasure measure,
        IStructuredQueryCompanyBinder companyBinder, IStructuredQueryProductBinder productBinder,
        string question, string company, string? product, string presentation,
        DateOnly start, DateOnly end, int maximumPeriods, IReadOnlyList<PlanAggregation>? aggregations)
    {
        var nodeIds = new[] { "domain-monthly-activity" };
        var requirements = new QuestionSchemaRequirements(1, snapshot.CatalogVersion, snapshot.CatalogHash,
            [measure.CatalogId], new[] { "dim_company", "dim_monthly_period" }.Concat(product is null ? Array.Empty<string>() : ["dim_product_identity"]).ToArray(),
            product is null ? ["company"] : ["company", "product"], measure.RelationshipIds, [], ["explicit-period"], [], [presentation],
            StatementScopes: ["standalone"], AggregationRequirements: aggregations?.Count > 0 ? ["aggregate"] : [],
            OrderingRequirements: [], EntityReferences: [company],
            FinancialValueReferences: [new(nameof(FinancialValueReferenceType.MonthlyMeasure), measure.CatalogId)],
            RequestedFinancialValueOperation: nameof(FinancialValueOperation.DirectValueRetrieval),
            StructuredShape: new StructuredPlanShape([], [], [], aggregations ?? [], [],
                Limit: Math.Clamp(maximumPeriods, 1, 100), ReportingPeriodStart: start,
                ReportingPeriodEnd: end, MaximumPeriods: maximumPeriods))
        { ProductReferences = product is null ? [] : [product] };
        var governance = nodeIds.Select(nodeId => new SchemaNodeGovernance(nodeId, true, true, true, true)).ToArray();
        var context = new SchemaContext(1, snapshot.CatalogVersion, snapshot.CatalogHash, nodeIds, governance,
            requirements.MeasureIds, requirements.DimensionIds,
            product is null ? ["company"] : ["company", "product"], requirements.RelationshipIds,
            requirements.SourcePolicies, requirements.PeriodPolicies, requirements.ComparisonScopes,
            requirements.PresentationRequirements, [], requirements.StatementScopes, requirements.AggregationRequirements,
            requirements.OrderingRequirements, requirements.EntityReferences);
        var navigation = new SchemaNavigationResult(1, snapshot.CatalogVersion, snapshot.CatalogHash,
            [new NavigationCandidate("domain-monthly-activity", 1, "captured and catalog-validated navigation anchor")],
            nodeIds, requirements.MeasureIds, requirements.DimensionIds,
            product is null ? ["company"] : ["company", "product"], requirements.RelationshipIds,
            requirements.SourcePolicies, requirements.PeriodPolicies, [], [],
            SelectedPresentationRequirements: requirements.PresentationRequirements,
            SelectedStatementScopes: requirements.StatementScopes,
            SelectedAggregationRequirements: requirements.AggregationRequirements,
            SelectedOrderingRequirements: requirements.OrderingRequirements,
            SelectedEntityReferences: requirements.EntityReferences,
            CandidateMeasureIds: snapshot.MonthlyMeasures.Select(item => item.CatalogId).ToArray(),
            RequiredMeasureIds: requirements.MeasureIds);
        var planner = new OfflineStructuredQueryPlanningService(new CapturedRequirementsExtractor(requirements),
            new CapturedNavigator(navigation), new CapturedContextProvider(context),
            new SchemaContextCompletenessValidator(), new StructuredQueryPlanBuilder(),
            new StructuredQueryPlanValidator(), companyBinder, productBinder);
        return await planner.PlanAsync(new SchemaNavigationRequest(1, question, snapshot.CatalogVersion,
            snapshot.CatalogHash, TenantId: TenantId, CorrelationId: "feature141-deterministic-pg-replay"),
            CancellationToken.None);
    }

    // The binder's semantic title/unit identity (StructuredQueryProductBinder.IdentityKey).
    private static string ProductKey(string externalCompanyId) =>
        $"{externalCompanyId}:TITLE:گندله|UNIT:ton";

    private static string ProductKey(string externalCompanyId, string productTitle) =>
        $"{externalCompanyId}:TITLE:{productTitle}|UNIT:ton";

    private static StructuredQueryPlan ProducerSalesPlan(StructuredQueryCatalogSnapshot snapshot,
        StructuredSqlMonthlyMeasure sales, ProductConceptResolution productConcept, PopulationSalesScope scope)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-12);
        var end = DateOnly.FromDateTime(DateTime.UtcNow);
        return new StructuredQueryPlan(1, snapshot.CatalogVersion, snapshot.CatalogHash, TenantId,
            [new PlanEntityReference("Product", productConcept.CanonicalLabel!,
                ResolvedEntityKey: productConcept.CatalogId, IdentityProvenance: productConcept.ApprovalReference,
                IdentityVerified: true)],
            [new PlanSourceReference(FinancialValueReferenceType.MonthlyMeasure, sales.CatalogId, null, null, null, null,
                sales.CatalogId, sales.UnitCode, sales.UnitKind, "catalog-mapped", "approved", true)],
            sales.RelationshipIds, [new PlanProjection(sales.CatalogId, PlanProjectionRole.Value)], [],
            new PlanPeriodSelection(["approved-observation-window"], [], false, false, start, end, 12),
            new PlanStatementSelection([], [], [], [], false, true, true, true, true, true),
            [new PlanAggregation(sales.CatalogId, PlanAggregationOperation.Sum)],
            [new PlanSort(sales.CatalogId, PlanSortDirection.Descending)], new PlanPagination(0, 10),
            PlanPresentation.RankedTable,
            new PlanFinancialOperation(FinancialValueOperation.DirectValueRetrieval, PlanFinancialOperationKind.DirectValue,
                PlanComparisonMode.None, [], false), true, true, true, true, [], ProductConcept: productConcept,
            CompanyDiscoveryScope: new AuthorizedCompanyDiscoveryScope(TenantId,
                "feature141-isolated-tenant-company-scope", "owner-approved-isolated-fixture-scope", true),
            PopulationCoveragePolicyId: "observed-production-trailing-12-months-v1", PopulationSalesScope: scope);
    }

    private static async Task<StructuredQueryExecutionResult> ExecuteAsync(CompiledReadOnlyQuery query,
        string databaseName, string role, string connectionString, IReadOnlyList<Guid>? discoveryCompanyIds = null)
    {
        var options = Options.Create(ExecutionOptions(databaseName, role, connectionString, query, discoveryCompanyIds));
        var host = new TestEnvironment("Development");
        var authorizer = new DevelopmentStructuredQueryAuthorizer(options, host);
        var grant = Assert.IsType<StructuredQueryExecutionGrant>(authorizer.Authorize(query, TenantId));
        var executor = new NpgsqlReadOnlyStructuredQueryExecutor(options,
            NullLogger<NpgsqlReadOnlyStructuredQueryExecutor>.Instance, host);
        return await executor.ExecuteAsync(query, grant, CancellationToken.None);
    }

    private static StructuredQueryExecutionOptions ExecutionOptions(string databaseName, string role,
        string connectionString, CompiledReadOnlyQuery query, IReadOnlyList<Guid>? discoveryCompanyIds = null) => new()
    {
        Enabled = true, ExplicitDevelopmentOptIn = true, EnvironmentName = "Development",
        ConnectionString = connectionString, DesignatedDatabase = databaseName, ReadOnlyRole = role,
        CommandTimeoutSeconds = 5, MaximumRows = 100, MaximumResultBytes = 1_000_000, MaximumConcurrency = 1,
        DevelopmentScopeId = "feature141-isolated-postgres-fixture",
        AuthorizationProvenance = "test-only-isolated-catalog-approval",
        ReviewedSourceCatalogIds = query.SourceCatalogIds.ToArray(),
        ReviewedRelationshipCatalogIds = query.RelationshipCatalogIds.ToArray(),
        ReviewedPhysicalObjectCatalogIds = query.PhysicalObjectCatalogIds.ToArray(),
        ReviewedTenantCompanyBindings = (discoveryCompanyIds ?? query.CompanyIds)
            .Select(companyId => $"{TenantId:D}:{companyId:D}").ToArray(),
        ReviewedTenantCompanyDiscoveryScopes = query.CompanyDiscoveryScopeId is not null
            ? [$"{TenantId:D}|{query.CompanyDiscoveryScopeId}|{query.CompanyDiscoveryApprovalReference}"] : [],
        ReviewedOperations = [query.Operation.ToString()]
    };

    private static NormalizedMonthlyReportRow Report(Guid companyId, string suffix, DateOnly start, DateOnly end,
        string fingerprint, bool accepted, string revisionStatus, int outputType, string? externalCompanyId = null,
        string? providerName = null) => new()
    {
        Id = Guid.NewGuid(), ProviderName = providerName ?? Provider, ExternalCompanyId = externalCompanyId ?? "monthly-fixture-company",
        ExternalReportId = $"fixture-{suffix}", PeriodStart = start, PeriodEnd = end,
        SourcePayloadChecksum = fingerprint, LastSynchronizedAt = DateTimeOffset.UtcNow,
        LogicalReportKey = $"fixture-{suffix}", RevisionFingerprint = fingerprint,
        IsAccepted = accepted, RevisionStatus = revisionStatus, ReportType = "ProductSales", OutputType = outputType,
        CompanyId = null // production Noavaran reports carry no CompanyId; companyId only names the fixture owner
    };

    private static NormalizedMonthlyReportLineItemRow Line(NormalizedMonthlyReportRow report,
        decimal production, decimal salesQuantity, decimal amount, decimal rate, string unit, string? fixtureProduct = null,
        string? title = null) => new()
    {
        Id = Guid.NewGuid(), MonthlyReportId = report.Id, SourceRowFingerprint = $"row-{report.ExternalReportId}-{fixtureProduct ?? ProductCode}",
        SourceMultiplicity = 1, ProductCode = ProductionLikeProductCode(), Title = title ?? FixtureTitle(fixtureProduct ?? ProductCode), Unit = unit,
        ProductionQuantity = production, SalesQuantity = salesQuantity, SalesAmount = amount, SalesRate = rate,
        SourcePayloadChecksum = report.SourcePayloadChecksum
    };

    // Real Noavaran ProductCode values are per-row hashes that change every month, repeat across companies and
    // products, or are zero; the fixture cycles through such values.
    private static readonly string[] ProductionLikeProductCodes = ["0", "PRODUCT:NATURAL:22d58f29e3fedf1e", "PRODUCT:NATURAL:9a1f0c"];
    private static int productCodeCursor;

    private static string ProductionLikeProductCode() =>
        ProductionLikeProductCodes[Interlocked.Increment(ref productCodeCursor) % ProductionLikeProductCodes.Length];

    // Distinct products of one report have distinct source titles, as in real reports.
    private static string FixtureTitle(string fixtureProduct) => fixtureProduct switch
    {
        ProductCode => "Pellet",
        AmbiguousProductCode => "Cement",
        "PRODUCT:MONTHLY-FIXTURE:ZERO" => "Zero product",
        "PRODUCT:MONTHLY-FIXTURE:NEGATIVE" => "Negative product",
        "PRODUCT:MONTHLY-FIXTURE:PIECES" => "Pieces product",
        _ => fixtureProduct
    };

    private static async Task CreateRestrictedRoleAsync(string adminConnectionString, string databaseName,
        string role, string password)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; " +
            $"GRANT USAGE ON SCHEMA public TO \"{role}\"; " +
            $"GRANT SELECT ON TABLE public.\"MonthlyReports\", public.\"MonthlyReportLineItems\", public.\"Companies\" TO \"{role}\";";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateFinancialStatementRestrictedRoleAsync(string adminConnectionString,
        string role, string password)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; " +
            $"GRANT USAGE ON SCHEMA public TO \"{role}\"; " +
            $"GRANT SELECT ON TABLE public.\"FinancialStatements\", public.\"FinancialStatementLineItems\", " +
            $"public.\"FinancialStatementSourceItems\", public.\"Companies\" TO \"{role}\";";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertFinancialStatementRoleIsReadOnlyAsync(string connectionString, string role)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_user, rolsuper OR rolcreatedb OR rolcreaterole, " +
            "has_table_privilege(current_user, 'public.\"FinancialStatements\"', 'SELECT'), " +
            "has_table_privilege(current_user, 'public.\"FinancialStatements\"', 'INSERT') " +
            "FROM pg_roles WHERE rolname = current_user";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(role, reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
    }

    private static async Task AssertRoleIsReadOnlyAsync(string connectionString, string role)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_user, rolsuper OR rolcreatedb OR rolcreaterole, has_table_privilege(current_user, 'public.\"MonthlyReports\"', 'SELECT'), has_table_privilege(current_user, 'public.\"MonthlyReports\"', 'INSERT') FROM pg_roles WHERE rolname = current_user";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(role, reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
    }

    private static async Task DropRoleAsync(string adminConnectionString, string role)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"REVOKE ALL PRIVILEGES ON TABLE public.\"MonthlyReports\", public.\"MonthlyReportLineItems\", public.\"Companies\" FROM \"{role}\"; " +
            $"REVOKE USAGE ON SCHEMA public FROM \"{role}\"; DROP ROLE IF EXISTS \"{role}\"";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropFinancialStatementRoleAsync(string adminConnectionString, string role)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"REVOKE ALL PRIVILEGES ON TABLE public.\"FinancialStatements\", " +
            $"public.\"FinancialStatementLineItems\", public.\"FinancialStatementSourceItems\", " +
            $"public.\"Companies\" FROM \"{role}\"; REVOKE USAGE ON SCHEMA public FROM \"{role}\"; " +
            $"DROP ROLE IF EXISTS \"{role}\"";
        await command.ExecuteNonQueryAsync();
    }

    private static string LiveArtifactFileName(string fileName)
    {
        var requestedTag = Environment.GetEnvironmentVariable("FEATURE141_LIVE_ARTIFACT_TAG");
        if (string.IsNullOrWhiteSpace(requestedTag)) return fileName;
        var tag = new string(requestedTag.Where(character => char.IsLetterOrDigit(character)
            || character is '-' or '_').ToArray());
        if (tag.Length == 0) throw new InvalidOperationException("FEATURE141_LIVE_ARTIFACT_TAG contains no safe filename characters.");
        return $"{Path.GetFileNameWithoutExtension(fileName)}-{tag}{Path.GetExtension(fileName)}";
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "README.md"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "feature141-monthly-postgres-test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CapturedRequirementsExtractor(QuestionSchemaRequirements captured) : IQuestionSchemaRequirementsExtractor
    {
        public Task<QuestionSchemaRequirements> ExtractAsync(SchemaNavigationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(captured);
    }

    private sealed class CapturedNavigator(SchemaNavigationResult captured) : IVectorlessSchemaNavigator
    {
        public Task<SchemaNavigationResult> NavigateQuestionAsync(SchemaNavigationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(captured);

        public Task<SchemaNavigationResult> NavigateAsync(SchemaNavigationRequest request,
            QuestionSchemaRequirements independentlyExtractedRequirements, CancellationToken cancellationToken) =>
            Task.FromResult(captured);
    }

    private sealed class CapturedContextProvider(SchemaContext captured) : ICatalogNavigationHierarchyProvider
    {
        public Task<SchemaContext?> GetContextAsync(SchemaContextExpansionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<SchemaContext?>(captured);
    }
}

