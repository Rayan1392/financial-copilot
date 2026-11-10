using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 158 D4: a monthly-sales turn with no data gets the true reason. Symbols are invented.</summary>
public sealed class Feature158MonthlyCoverageReplyTests
{
    private const string Symbol = "نمونه";
    private const string OutsideScopeFa = "این نماد در فهرست شرکت‌های تحت پوشش گزارش ماهانه نیست.";
    private const string OutsideScopeEn = "This symbol is not on the list of companies covered by monthly reports.";

    [Theory]
    [InlineData("ساختمانی", "construction")]
    [InlineData("سرمایه‌گذاری", "investment")]
    [InlineData("بانکی", "banking")]
    [InlineData("لیزینگی", "leasing")]
    [InlineData("بیمه‌ای", "insurance")]
    [InlineData("حمل و نقل دریایی", "maritime transportation")]
    [InlineData("تأمین سرمایه", "investment banking")]
    public async Task NoRouteCode_ReturnsNotCoveredReasonAndExactReply(string categoryFa, string categoryEn)
    {
        var result = await Execute(new MonthlyReportCoverage(MonthlyReportCoverageKind.NoRoute, categoryFa, categoryEn), "fa");

        Assert.Equal(CapabilityExecutionStatus.NoData, result.Status);
        Assert.Equal(DialogueOutcomeReasonCodes.MonthlyReportNotCovered, result.ReasonCode);
        Assert.Equal(
            $"پوشش گزارش ماهانهٔ فروش برای شرکت‌های {categoryFa} در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «{Symbol}» نمودار فروش ماهانه نداریم.",
            result.Payload);
    }

    [Fact]
    public async Task EnglishReply_UsesCategoryEnAndOmitsSymbol()
    {
        var result = await Execute(new MonthlyReportCoverage(MonthlyReportCoverageKind.NoRoute, "بانکی", "banking"), "en");

        Assert.Equal(
            "Monthly sales report coverage for banking companies is not available from the current data source, so there is no monthly sales chart for this symbol.",
            result.Payload);
        Assert.True(AiDialogueOutcomePolicy.IsCompatibleWithLanguage((string)result.Payload!, "en"));
    }

    [Theory]
    [InlineData("fa", OutsideScopeFa)]
    [InlineData("en", OutsideScopeEn)]
    public async Task OutsideScope_ReturnsIneligibleReasonAndOwnSentence(string language, string expected)
    {
        var result = await Execute(new MonthlyReportCoverage(MonthlyReportCoverageKind.OutsideScope), language);

        Assert.Equal(CapabilityExecutionStatus.NoData, result.Status);
        Assert.Equal(DialogueOutcomeReasonCodes.DataStaleOrIneligible, result.ReasonCode);
        Assert.Equal(expected, result.Payload);
    }

    [Fact]
    public async Task CompanyAmbiguous_ReturnsDisambiguation()
    {
        var result = await Execute(new MonthlyReportCoverage(MonthlyReportCoverageKind.CompanyAmbiguous), "fa");

        Assert.Equal(CapabilityExecutionStatus.DisambiguationRequired, result.Status);
        Assert.Equal(DialogueOutcomeReasonCodes.EntityAmbiguous, result.ReasonCode);
    }

    [Theory]
    [InlineData(MonthlyReportCoverageKind.HasRoute)]
    [InlineData(MonthlyReportCoverageKind.Unknown)]
    public async Task SupportedRouteAndUnknown_KeepTodaysReason(MonthlyReportCoverageKind kind)
    {
        var result = await Execute(new MonthlyReportCoverage(kind), "fa");

        Assert.Equal(CapabilityExecutionStatus.NoData, result.Status);
        Assert.Equal(DialogueOutcomeReasonCodes.SupportedButNoRows, result.ReasonCode);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task ResultPresent_DoesNotCallPort()
    {
        var reader = new FakeReader(new MonthlyReportCoverage(MonthlyReportCoverageKind.NoRoute, "بانکی", "banking"));
        var executor = new MonthlyActivityTrendCapabilityExecutor(new FakeTrend(HasData: true), reader);

        var result = await executor.ExecuteAsync(Frame("fa"), Context(), default);

        Assert.Equal(CapabilityExecutionStatus.Executed, result.Status);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task FrozenV1_DoesNotCallPort()
    {
        var reader = new FakeReader(new MonthlyReportCoverage(MonthlyReportCoverageKind.NoRoute, "بانکی", "banking"));
        var executor = new MonthlyActivityTrendCapabilityExecutor(new FakeTrend(HasData: false), reader);

        using (Feature154RoutingScope.EnterFrozenV1())
        {
            var result = await executor.ExecuteAsync(Frame("fa"), Context(), default);

            Assert.Equal(DialogueOutcomeReasonCodes.SupportedButNoRows, result.ReasonCode);
            Assert.Equal(0, reader.Calls);
        }
    }

    [Theory]
    [InlineData("fa", "پوشش گزارش ماهانهٔ فروش برای شرکت‌های بانکی در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «نمونه» نمودار فروش ماهانه نداریم.")]
    [InlineData("en", "Monthly sales report coverage for banking companies is not available from the current data source, so there is no monthly sales chart for this symbol.")]
    [InlineData("fa", OutsideScopeFa)]
    [InlineData("en", OutsideScopeEn)]
    public void Reply_RendersThroughOutcomePolicy(string language, string text)
    {
        var outcome = new DialogueOutcomeResult(DialogueOutcome.NoData, DialogueOutcomeReasonCodes.MonthlyReportNotCovered, language, null, false);

        Assert.Equal(text, AiDialogueOutcomePolicy.ComposeSystemMessage(outcome, text));
    }

    [Theory]
    [InlineData(1_000_001, "ساختمانی", "construction")]
    [InlineData(1_000_002, "سرمایه‌گذاری", "investment")]
    [InlineData(1_000_003, "بانکی", "banking")]
    [InlineData(1_000_004, "لیزینگی", "leasing")]
    [InlineData(1_000_006, "بیمه‌ای", "insurance")]
    [InlineData(1_000_007, "حمل و نقل دریایی", "maritime transportation")]
    [InlineData(1_000_009, "تأمین سرمایه", "investment banking")]
    public async Task Adapter_MapsSevenCodesToCategories(int reportingType, string categoryFa, string categoryEn)
    {
        await using var db = CreateDb();
        db.Companies.Add(Row(Symbol, reportingType));
        await db.SaveChangesAsync();

        var coverage = await Reader(db).ReadAsync(Symbol, default);

        Assert.Equal(new MonthlyReportCoverage(MonthlyReportCoverageKind.NoRoute, categoryFa, categoryEn), coverage);
    }

    [Theory]
    [InlineData(1_000_000, MonthlyReportCoverageKind.HasRoute)]
    [InlineData(1_000_005, MonthlyReportCoverageKind.HasRoute)]
    [InlineData(1_000_008, MonthlyReportCoverageKind.HasRoute)]
    [InlineData(null, MonthlyReportCoverageKind.Unknown)]
    [InlineData(1_000_999, MonthlyReportCoverageKind.Unknown)]
    public async Task Adapter_MapsSupportedMissingUnknownAndNotEligible(int? reportingType, MonthlyReportCoverageKind expected)
    {
        await using var db = CreateDb();
        db.Companies.Add(Row(Symbol, reportingType));
        await db.SaveChangesAsync();

        Assert.Equal(expected, (await Reader(db).ReadAsync(Symbol, default)).Kind);
    }

    [Fact]
    public async Task Adapter_NonEligibleRow_IsOutsideScopeWhateverTheType()
    {
        await using var db = CreateDb();
        var row = Row(Symbol, 1_000_003);
        row.PrecedencyRight = 1;
        db.Companies.Add(row);
        await db.SaveChangesAsync();

        Assert.Equal(MonthlyReportCoverageKind.OutsideScope, (await Reader(db).ReadAsync(Symbol, default)).Kind);
    }

    [Fact]
    public async Task Adapter_CompanyOnlyWithAnotherProvider_IsOutsideScope()
    {
        await using var db = CreateDb();
        db.Companies.Add(new NormalizedCompanyRow { Id = Guid.NewGuid(), ProviderName = "CyclicalWaves", ExternalCompanyId = "cw", Name = "x", TseSymbol = Symbol });
        await db.SaveChangesAsync();

        Assert.Equal(MonthlyReportCoverageKind.OutsideScope, (await Reader(db).ReadAsync(Symbol, default)).Kind);
    }

    [Fact]
    public async Task Adapter_UnknownSymbol_IsUnknown()
    {
        await using var db = CreateDb();
        await db.SaveChangesAsync();

        Assert.Equal(MonthlyReportCoverageKind.Unknown, (await Reader(db).ReadAsync(Symbol, default)).Kind);
    }

    [Fact]
    public async Task Adapter_TwoEligibleRows_AreCompanyAmbiguous()
    {
        await using var db = CreateDb();
        db.Companies.AddRange(Row(Symbol, 1_000_003), Row(Symbol, 1_000_003));
        await db.SaveChangesAsync();

        Assert.Equal(MonthlyReportCoverageKind.CompanyAmbiguous, (await Reader(db).ReadAsync(Symbol, default)).Kind);
    }

    [Fact]
    public async Task Adapter_ReadFailure_LogsWarningAndReturnsUnknown()
    {
        var db = CreateDb();
        await db.DisposeAsync();
        var logger = new RecordingLogger();

        var coverage = await Reader(db, logger).ReadAsync(Symbol, default);

        Assert.Equal(MonthlyReportCoverageKind.Unknown, coverage.Kind);
        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("coverage_read_failed", warning.Message);
    }

    [Fact]
    public async Task Adapter_CallerCancellation_IsNotSwallowed()
    {
        await using var db = CreateDb();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(db).ReadAsync(Symbol, cancelled.Token));
    }

    [Fact]
    public void Composition_WiresTheMonthlyExecutorAndItsCoverageReader()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FinancialCopilot"] = "Host=invalid.invalid;Port=1;Database=fake",
                ["FinancialData:ChatProvider"] = "NoavaranAmin"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddFinancialCopilotInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<NoavaranMonthlyReportCoverageReader>(scope.ServiceProvider.GetRequiredService<IMonthlyReportCoverageReader>());
        Assert.IsType<MonthlyActivityTrendQueryUseCase>(scope.ServiceProvider.GetRequiredService<IMonthlyActivityTrendQueryUseCase>());
        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(MonthlyActivityTrendCapabilityExecutor));
        Assert.NotNull(ActivatorUtilities.CreateInstance<MonthlyActivityTrendCapabilityExecutor>(scope.ServiceProvider));
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static NormalizedCompanyRow Row(string symbol, int? reportingType) => new()
    {
        Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = Guid.NewGuid().ToString("N"), Name = "شرکت نمونه",
        TseSymbol = symbol, CompanySymbol = symbol, PrecedencyRight = 0, MarketId = NoavaranCompanyScope.BourseMarketId,
        ReportingType = reportingType
    };

    private static NoavaranMonthlyReportCoverageReader Reader(FinancialIngestionDbContext db, ILogger<NoavaranMonthlyReportCoverageReader>? logger = null)
    {
        var outcomes = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        return new(
            new CanonicalMonthlyCompanyResolver(db, outcomes),
            db,
            new NoavaranMonthlyReportTypeResolver(),
            logger ?? NullLogger<NoavaranMonthlyReportCoverageReader>.Instance);
    }

    private sealed class RecordingLogger : ILogger<NoavaranMonthlyReportCoverageReader>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static Task<CapabilityExecutionResult> Execute(MonthlyReportCoverage coverage, string language) =>
        new MonthlyActivityTrendCapabilityExecutor(new FakeTrend(HasData: false), new FakeReader(coverage))
            .ExecuteAsync(Frame(language), Context(), default);

    private static ValidatedQueryFrame Frame(string language) => new(
        "monthly_activity_trend", 1,
        [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, Symbol, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, "monthly_activity_trend")],
        new("q", "q", language, [], [], [], null, null, null, [], [], 1m, [], 1));

    private static QueryExecutionContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "fa");

    private sealed class FakeReader(MonthlyReportCoverage coverage) : IMonthlyReportCoverageReader
    {
        public int Calls { get; private set; }

        public Task<MonthlyReportCoverage> ReadAsync(string symbol, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(coverage);
        }
    }

    private sealed record FakeTrend(bool HasData) : IMonthlyActivityTrendQueryUseCase
    {
        public Task<MonthlyActivityTrendResponse?> ExecuteAsync(MonthlyActivityTrendQuery query, CancellationToken ct = default) =>
            Task.FromResult(HasData
                ? new MonthlyActivityTrendResponse(Symbol, "شرکت", 1405, 4, "میلیارد تومان", 1m, null, null, null, null, null, null, [], [], [], "NoavaranCurrentApi", DateTimeOffset.UtcNow)
                : null);
    }
}
