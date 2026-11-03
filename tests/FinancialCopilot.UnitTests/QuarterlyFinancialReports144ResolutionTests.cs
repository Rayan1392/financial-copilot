using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class QuarterlyFinancialReports144ResolutionTests
{
    private static readonly Guid BankingIndustryGroupId = Guid.Parse("c96520f9-1d2e-444a-ab46-5bdffd3797d2");

    private static FinancialIngestionDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task ResolveAsync_ReturnsResolvedCompanyForSupportedProvider()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(Company("12783", "چخزر", "NoavaranCurrentApi"));
        await db.SaveChangesAsync();

        var outcome = await Resolver(db).ResolveAsync("چخزر", "NoavaranCurrentApi");

        Assert.Equal(CompanyResolutionStatus.Resolved, outcome.Status);
        Assert.Equal("12783", outcome.Company?.ExternalCompanyId);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNotFoundWhenCompanyDoesNotExist()
    {
        await using var db = CreateDbContext();

        var outcome = await Resolver(db).ResolveAsync("ناموجود", "NoavaranCurrentApi");

        Assert.Equal(CompanyResolutionStatus.NotFound, outcome.Status);
        Assert.Null(outcome.Company);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsAmbiguousInsteadOfSelectingFirstTickerMatch()
    {
        await using var db = CreateDbContext();
        db.Companies.AddRange(
            Company("1", "نماد", "NoavaranCurrentApi"),
            Company("2", "نماد", "NoavaranCurrentApi"));
        await db.SaveChangesAsync();

        var outcome = await Resolver(db).ResolveAsync("نماد", "NoavaranCurrentApi");

        Assert.Equal(CompanyResolutionStatus.Ambiguous, outcome.Status);
        Assert.Null(outcome.Company);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsInvalidForBlankOrMalformedIdentifier()
    {
        await using var db = CreateDbContext();
        var resolver = Resolver(db);

        Assert.Equal(CompanyResolutionStatus.InvalidIdentifier,
            (await resolver.ResolveAsync(" ", "NoavaranCurrentApi")).Status);
        Assert.Equal(CompanyResolutionStatus.InvalidIdentifier,
            (await resolver.ResolveAsync("\u0001", "NoavaranCurrentApi")).Status);
    }

    [Fact]
    public async Task ResolveAsync_ReportsUnsupportedProviderOrProviderCompanyPair()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(Company("external-1", "نماد", "OtherProvider"));
        await db.SaveChangesAsync();
        var resolver = Resolver(db);

        Assert.Equal(CompanyResolutionStatus.UnsupportedProviderCompany,
            (await resolver.ResolveAsync("نماد", "NoavaranCurrentApi")).Status);
        Assert.Equal(CompanyResolutionStatus.UnsupportedProviderCompany,
            (await resolver.ResolveAsync("نماد", "OtherProvider")).Status);
    }

    [Fact]
    public async Task QuarterlyUseCase_IsDirectlyCallableAndReturnsTypedTableForCanonicalCompany()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(Company("12783", "چخزر", "NoavaranCurrentApi"));
        await db.SaveChangesAsync();
        var repository = new QuarterlyRepositoryFake();
        var useCase = new QuarterlyFinancialReportsQueryUseCase(repository, Resolver(db), TimeProvider.System,
            new BankingFinancialReportsConfiguration(BankingIndustryGroupId));

        var result = await useCase.ExecuteAsync(new QuarterlyFinancialReportTableQuery(
            string.Empty, "چخزر", QuarterCount: 1));

        Assert.Equal(CompanyResolutionStatus.Resolved, result.CompanyResolutionStatus);
        Assert.Equal(QuarterlyFinancialReportOutcome.SuccessfulTable, result.Outcome);
        Assert.Equal("GeneralIncomeStatement", result.TemplateId);
        Assert.Equal("12783", result.ExternalCompanyId);
        Assert.Equal("چخزر", result.CompanySymbol);
        Assert.Equal(15, result.Rows.Count);
        Assert.Equal("12783", repository.LastSelection?.ExternalCompanyId);
        var revenue = result.Rows.Single(row => row.SourceItemId == 15);
        var cell = Assert.Single(revenue.Cells).Value;
        Assert.Equal(0m, cell.Value);
        Assert.Equal(QuarterlyCellState.Available, cell.State);
    }

    [Fact]
    public async Task QuarterlyUseCase_UsesOnlyAuthoritativeBankGroupForTwentyTwoRowTemplate()
    {
        var configuredBankingGroupId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        await using var db = CreateDbContext();
        db.Companies.Add(Company("12783", "وبملت", "NoavaranCurrentApi"));
        await db.SaveChangesAsync();
        var repository = new QuarterlyRepositoryFake
        {
            Groups = [configuredBankingGroupId]
        };
        var useCase = new QuarterlyFinancialReportsQueryUseCase(repository, Resolver(db), TimeProvider.System,
            new BankingFinancialReportsConfiguration(configuredBankingGroupId));

        var result = await useCase.ExecuteAsync(new QuarterlyFinancialReportTableQuery(string.Empty, "وبملت", 1));

        Assert.Equal("BankingIncomeStatement", result.TemplateId);
        Assert.Equal(configuredBankingGroupId, result.ClassificationGroupId);
        Assert.Equal(22, result.Rows.Count);
        Assert.Equal("وبملت", result.CompanySymbol);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task QuarterlyUseCase_DoesNotGuessWhenClassificationIsMissingOrDuplicated(int matchCount)
    {
        await using var db = CreateDbContext();
        db.Companies.Add(Company("12783", "نماد", "NoavaranCurrentApi"));
        await db.SaveChangesAsync();
        var repository = new QuarterlyRepositoryFake
        {
            Groups = Enumerable.Repeat<Guid?>(BankingIndustryGroupId, matchCount).ToArray()
        };
        var useCase = new QuarterlyFinancialReportsQueryUseCase(repository, Resolver(db), TimeProvider.System,
            new BankingFinancialReportsConfiguration(BankingIndustryGroupId));

        var result = await useCase.ExecuteAsync(new QuarterlyFinancialReportTableQuery(string.Empty, "نماد", 1));

        Assert.Equal("ClassificationUnavailable", result.TemplateId);
        Assert.Equal("company_classification_unavailable", result.Reason);
        Assert.Null(repository.LastSelection);
    }

    [Fact]
    public async Task QuarterlyUseCase_ReturnsTypedNotFoundTableWithoutReadingReports()
    {
        await using var db = CreateDbContext();
        var repository = new QuarterlyRepositoryFake();
        var useCase = new QuarterlyFinancialReportsQueryUseCase(repository, Resolver(db), TimeProvider.System,
            new BankingFinancialReportsConfiguration(BankingIndustryGroupId));

        var result = await useCase.ExecuteAsync(new QuarterlyFinancialReportTableQuery(
            string.Empty, "نماد-ناموجود", QuarterCount: 1));

        Assert.Equal(CompanyResolutionStatus.NotFound, result.CompanyResolutionStatus);
        Assert.Equal("Unavailable", result.Status);
        Assert.Equal(QuarterlyFinancialReportOutcome.CompanyNotFound, result.Outcome);
        Assert.Equal(15, result.Rows.Count);
        Assert.Null(repository.LastSelection);
    }

    [Fact]
    public async Task QuarterlyUseCase_ReturnsDistinctOutcomeWhenCompanyExistsWithoutPersistedReports()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(Company("12783", "چخزر", "NoavaranCurrentApi"));
        await db.SaveChangesAsync();
        var useCase = new QuarterlyFinancialReportsQueryUseCase(
            new EmptyQuarterlyRepository(), Resolver(db), TimeProvider.System,
            new BankingFinancialReportsConfiguration(BankingIndustryGroupId));

        var result = await useCase.ExecuteAsync(new QuarterlyFinancialReportTableQuery(
            string.Empty, "چخزر", QuarterCount: 1));

        Assert.Equal(CompanyResolutionStatus.Resolved, result.CompanyResolutionStatus);
        Assert.Equal(QuarterlyFinancialReportOutcome.CompanyFoundNoPersistedReports, result.Outcome);
        Assert.Equal("no_compatible_reports", result.Reason?.Split(':')[0]);
    }

    [Fact]
    public async Task QuarterlyUseCase_ReturnsTypedAmbiguousCompanyOutcome()
    {
        await using var db = CreateDbContext();
        db.Companies.AddRange(
            Company("first", "نماد", "NoavaranCurrentApi"),
            Company("second", "نماد", "NoavaranCurrentApi"));
        await db.SaveChangesAsync();
        var repository = new QuarterlyRepositoryFake();
        var useCase = new QuarterlyFinancialReportsQueryUseCase(repository, Resolver(db), TimeProvider.System,
            new BankingFinancialReportsConfiguration(BankingIndustryGroupId));

        var result = await useCase.ExecuteAsync(new QuarterlyFinancialReportTableQuery(
            string.Empty, "نماد", QuarterCount: 1));

        Assert.Equal(CompanyResolutionStatus.Ambiguous, result.CompanyResolutionStatus);
        Assert.Equal(QuarterlyFinancialReportOutcome.CompanyAmbiguous, result.Outcome);
        Assert.Null(repository.LastSelection);
    }

    private static CompanyResolverService Resolver(FinancialIngestionDbContext db) =>
        new(db, NullLogger<CompanyResolverService>.Instance);

    private static NormalizedCompanyRow Company(string externalId, string ticker, string provider) => new()
    {
        Id = Guid.NewGuid(),
        ProviderName = provider,
        ExternalCompanyId = externalId,
        Ticker = ticker,
        CompanySymbol = ticker,
        TseSymbol = ticker,
        Name = $"شرکت {ticker}",
        LastSynchronizedAt = DateTimeOffset.UtcNow
    };

    private sealed class QuarterlyRepositoryFake : IQuarterlyFinancialReportsRepository
    {
        public QuarterlyFinancialReportSelection? LastSelection { get; private set; }
        public IReadOnlyList<Guid?> Groups { get; init; } = [Guid.Parse("11111111-1111-1111-1111-111111111111")];

        public Task<IReadOnlyList<Guid?>> GetEligibleCompanyGroupsAsync(Guid canonicalCompanyId,
            string providerName, string externalCompanyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Groups);

        public Task<IReadOnlyList<QuarterlyReportObservation>> GetIncomeStatementReportsAsync(
            QuarterlyFinancialReportSelection selection, CancellationToken cancellationToken = default)
        {
            LastSelection = selection;
            var start = new DateOnly(2025, 3, 21);
            var end = DateOnly.FromDateTime(new System.Globalization.PersianCalendar()
                .AddMonths(start.ToDateTime(TimeOnly.MinValue), 3).AddDays(-1));
            var line = new QuarterlySourceObservation(Guid.NewGuid(),
                Guid.Parse(QuarterlyFinancialReportsNormalizer.ApprovedMappings.Single(x => x.SourceItemId == 15).CatalogId),
                15, "فروش", "million_rial", 0m);
            IReadOnlyList<QuarterlyReportObservation> reports =
            [new(Guid.NewGuid(), "report-1", "NoavaranCurrentApi", "12783", "IncomeStatement", 3,
                start, end, false, false, false, "checksum", DateTimeOffset.UtcNow, null, null, [line],
                "MillionRials", "Rial", "IRR", "Accrual", "lineage-1")];
            return Task.FromResult(reports);
        }
    }

    private sealed class EmptyQuarterlyRepository : IQuarterlyFinancialReportsRepository
    {
        public Task<IReadOnlyList<Guid?>> GetEligibleCompanyGroupsAsync(Guid canonicalCompanyId,
            string providerName, string externalCompanyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid?>>([Guid.Parse("11111111-1111-1111-1111-111111111111")]);

        public Task<IReadOnlyList<QuarterlyReportObservation>> GetIncomeStatementReportsAsync(
            QuarterlyFinancialReportSelection selection, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QuarterlyReportObservation>>([]);
    }
}
