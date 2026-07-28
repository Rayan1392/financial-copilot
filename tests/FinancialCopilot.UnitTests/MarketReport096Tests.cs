using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Domain.Financial.Reports;
using FinancialCopilot.Infrastructure.Financial.MarketReports;

namespace FinancialCopilot.UnitTests;

public sealed class MarketReport096Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-14T12:00:00Z");

    [Fact]
    public void PersonalDigest_RequiresCanonicalActorAndProtectsLifecycle()
    {
        Assert.Throws<ArgumentException>(() => MarketReport.Start(
            MarketReportScope.PersonalDigest, null, null, null, new DateOnly(2026, 7, 14), "final", 1,
            "v1", "evidence", "key", Now));

        var report = MarketReport.Start(
            MarketReportScope.PersonalDigest, Guid.NewGuid(), Guid.NewGuid(), "User",
            new DateOnly(2026, 7, 14), "final", 1, "v1", "evidence", "key", Now);
        report.PublishFallback("روایت مبتنی بر شواهد", "provider unavailable", Now.AddMinutes(1));

        Assert.Equal(MarketReportStatus.Fallback, report.Status);
        Assert.Equal(Now.AddMinutes(1), report.PublishedAtUtc);
        Assert.Throws<InvalidOperationException>(() => report.PublishGenerated("second", Now.AddMinutes(2)));
    }

    [Fact]
    public void NarrativePolicy_RejectsUnsupportedNumberAndUnsafeAdvice()
    {
        var policy = new MarketReportNarrativePolicy();
        var evidence = Evidence();

        Assert.True(policy.TryValidate("ارزش معاملات 120 IRR است. [e:pulse:fact]", evidence, out _));
        Assert.False(policy.TryValidate("ارزش معاملات 121 IRR است. [e:pulse:fact]", evidence, out var unsupported));
        Assert.Contains("numeric claim", unsupported, StringComparison.OrdinalIgnoreCase);
        Assert.False(policy.TryValidate("سیگنال خرید است. [e:pulse:fact]", evidence, out var unsafeReason));
        Assert.Contains("safety policy", unsafeReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fallback_IsPersianEvidenceBoundAndLabelsPartialCoverage()
    {
        var policy = new MarketReportNarrativePolicy();

        var fallback = policy.BuildFallback(MarketReportScope.IntradayMarket, Evidence());

        Assert.Contains("گزارش درون‌روزی بازار", fallback);
        Assert.Contains("[e:pulse:fact]", fallback);
        Assert.Contains("پوشش داده", fallback);
        Assert.True(policy.TryValidate(fallback, Evidence(), out var reason), reason);
    }

    private static MarketReportEvidenceBundle Evidence() => new(
        "market-report-evidence-v1",
        new DateOnly(2026, 7, 14),
        "open-001",
        IsPartial: true,
        IsFinal: false,
        SnapshotIds: [Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")],
        InsightEventIds: [],
        FollowedSymbols: ["فولاد"],
        Items:
        [
            new MarketReportEvidenceItem(
                "pulse:meta", "PulseMetadata", "بازه گزارش",
                "Trading date 2026-07-14; pulse revision 1.", ["2026", "7", "14", "1"],
                null, "MarketPulseSnapshots", Now, 1m),
            new MarketReportEvidenceItem(
                "pulse:fact", "PulseFact", "ارزش معاملات", "ارزش معاملات: 120 IRR.", ["120"],
                "IRR", "MarketPulseSnapshots", Now, 1m)
        ],
        Caveats: ["پوشش داده برای این بازه ناقص است."],
        ExcludedReasons: ["FLOW: unavailable"],
        SourceFreshnessUtc: Now,
        Confidence: 0.8m,
        AssembledAtUtc: Now);
}
