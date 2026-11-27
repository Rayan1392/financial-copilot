using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Change «valuation-gauge-forward-ratio» T6: one gauge answer with the forward estimate on, through the same V2 path as
/// <see cref="ValuationGaugeCardsCyclicalWavesE2ETests"/> (payload, text, credits, history). The existing gauge and
/// billing suites run with the estimate off and are unchanged (AC-01, AC-11).
/// </summary>
public sealed class ValuationGaugeForwardEstimateE2ETests : IClassFixture<ValuationGaugeForwardApiFactory>
{
    private readonly ValuationGaugeForwardApiFactory _factory;

    public ValuationGaugeForwardEstimateE2ETests(ValuationGaugeForwardApiFactory factory)
    {
        _factory = factory;
        factory.EnsureForwardSeeded();
        factory.ResetRecorders();
    }

    [Fact]
    public async Task AllGauges_CarrySapiosForwardEstimate_ForTheSameCredits_AndReplayFromHistory()
    {
        var key = Guid.NewGuid();
        var root = await ValuationGaugeChat.AskAsync(_factory, "گیج‌های ارزش‌گذاری شاراک", key);

        var cards = root.GetProperty("valuationGaugeCards");
        var byKind = cards.GetProperty("cards").EnumerateArray().ToDictionary(card => card.GetProperty("kind").GetString()!);
        // Forward P/E = 6.4291 × 200 / (2 100 × 0.1) = 6.12; forward P/S = 0.7405 × 2 000 / 2 100 = 0.71.
        var pe = byKind["Pe"].GetProperty("forward");
        Assert.Equal("6.12", pe.GetProperty("value").GetString());
        Assert.Equal("Inside", pe.GetProperty("side").GetString());
        Assert.Equal("sapio-1", pe.GetProperty("methodVersion").GetString());
        Assert.Equal("10.0", pe.GetProperty("marginPercent").GetString());
        Assert.Equal("0.71", byKind["Ps"].GetProperty("forward").GetProperty("value").GetString());
        Assert.False(byKind["Equilibrium"].TryGetProperty("forward", out _));
        // The provider label and the current values are unchanged.
        Assert.Equal("6.4291", byKind["Pe"].GetProperty("ratio").GetProperty("current").GetString());

        var text = ValuationGaugeChat.Text(root);
        Assert.Contains("P/E آینده‌نگر (برآورد ساپیو): ۶٫۱۲", text);
        Assert.Contains("P/S آینده‌نگر (برآورد ساپیو): ۰٫۷۱", text);
        Assert.Contains("منبع: تحلیل‌اپ", text);

        // AC-11: three usable cards cost three credits, as without the estimate.
        Assert.Equal(3m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.Equal("ValuationGaugesAnswered.3",
            Assert.Single(_factory.ReadUsageEntries(), entry => entry.IdempotencyKey == $"{key:N}:charge").CompletionStatus);
        Feature148Chat.ReadOnlyCyclicalWavesV2(_factory.Reads);
        Feature148Chat.NoOutboundCalls(_factory.Http);
        Assert.Empty(_factory.Fake.ToolCalls);

        var conversationId = root.GetProperty("conversationId").GetGuid();
        var messages = (await Feature148Chat.MessagesAsync(_factory, conversationId)).GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(cards.GetRawText(), messages[^1].GetProperty("assistantContent").GetProperty("valuationGaugeCards").GetRawText());
        Assert.Contains("P/E آینده‌نگر (برآورد ساپیو): ۶٫۱۲", messages[^1].GetProperty("content").GetString());
    }
}

/// <summary>The gauge API factory with the forward estimate on and estimator inputs seeded for شاراک.</summary>
public sealed class ValuationGaugeForwardApiFactory : ValuationGaugeCardsApiFactory
{
    private readonly object _forwardSeedLock = new();
    private bool _forwardSeeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Read while services are composed (the input reader is registered only when on), so it must be a host setting.
        builder.UseSetting("ValuationGaugeCards:ForwardEstimate:Enabled", "true");
    }

    public void EnsureForwardSeeded()
    {
        EnsureGaugeSeeded();
        if (_forwardSeeded) return;
        lock (_forwardSeedLock)
        {
            if (_forwardSeeded) return;
            using var scope = Services.CreateScope();
            Seed(scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>());
            _forwardSeeded = true;
        }

        ResetRecorders();
    }

    /// <summary>
    /// 16 months ending 20 days ago (100 million rials, the last 4 months 150) and 8 quarters of revenue 500 and net profit
    /// 50 million rials: growth 1.5, forecast sales 2 100, margin 0.1.
    /// </summary>
    private static void Seed(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var company = db.Companies.Single(row => row.SymbolIsin == "IRO1PARK0001");
        var lastMonth = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-20);
        for (var index = 0; index < 16; index++)
        {
            var monthEnd = lastMonth.AddMonths(index - 15);
            var id = Guid.NewGuid();
            db.MonthlyReports.Add(new NormalizedMonthlyReportRow
            {
                Id = id, ProviderName = company.ProviderName, ExternalCompanyId = company.ExternalCompanyId,
                ExternalReportId = $"forward-{index}", PeriodStart = monthEnd.AddMonths(-1).AddDays(1), PeriodEnd = monthEnd,
                SourcePayloadChecksum = id.ToString("N"), LastSynchronizedAt = now, LogicalReportKey = $"forward-{index}",
                RevisionFingerprint = id.ToString("N"), IsAccepted = true, RevisionStatus = "Accepted", ReportType = "ProductSales", OutputType = 0
            });
            db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
            {
                Id = Guid.NewGuid(), MonthlyReportId = id, SourceRowFingerprint = id.ToString("N"), SourcePayloadChecksum = id.ToString("N"),
                ProductCode = "P", SalesAmount = index >= 12 ? 150m : 100m
            });
        }

        var ticker = db.CyclicalWavesV2Tickers.Single(row => row.Ticker == "شاراک");
        var lastQuarter = lastMonth.AddMonths(-1);
        for (var index = 0; index < 8; index++)
        {
            var statement = new CyclicalWavesV2IncomeStatementRow
            {
                Id = Guid.NewGuid(), TickerId = ticker.Id, PeriodEndingDate = lastQuarter.AddMonths(3 * (index - 7)),
                FiscalYear = 1405, FiscalMonth = 3 * (index % 4 + 1), CreatedAt = now, UpdatedAt = now
            };
            db.CyclicalWavesV2IncomeStatements.Add(statement);
            db.CyclicalWavesV2IncomeStatementValues.AddRange(
                new CyclicalWavesV2IncomeStatementValueRow { StatementId = statement.Id, LineDefinitionId = 44, SortOrder = 1, Quarter = 500_000_000m },
                new CyclicalWavesV2IncomeStatementValueRow { StatementId = statement.Id, LineDefinitionId = 66, SortOrder = 2, Quarter = 50_000_000m });
        }

        db.SaveChanges();
        Assert.Equal(ProviderSources.NoavaranCurrentApiName, company.ProviderName);
    }
}
