using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Feature 154 T02. The routing signature of every preserved phrase was captured on the unchanged
/// develop code (commit 573cfd8) into TestData/Feature154/routing-baseline.json before any Feature 154
/// routing edit. Preserved phrases must keep exactly that signature; phrases Feature 154 intentionally
/// re-routes are recorded with "preserved": false so the diff stays visible.
/// Regenerate only on unchanged code: set FEATURE154_WRITE_ROUTING_BASELINE=1.
/// </summary>
public sealed class Feature154RoutingBaselineTests
{
    private static readonly ConversationalCapabilityRegistry Registry =
        new(InitialConversationalCapabilityCatalog.Create());

    public static readonly (string Phrase, bool Preserved)[] Phrases =
    [
        ("عملکرد وبملت", true),
        ("وضعیت وبملت", true),
        ("صورت سود و زیان وبملت", true),
        ("تحلیل صورت سود و زیان وبملت", true),
        ("صورت عملکرد وبملت", true),
        ("صورت درآمد وبملت", true),
        ("همه اقلام ترازنامه وبملت", true),
        ("جدول کامل ترازنامه شگل", true),
        ("ترازنامه تلفیقی وبملت", true),
        ("ترازنامه حسابرسی‌شده فاذر", true),
        ("ترازنامه تجدید ارائه‌شده شگل", true),
        ("ترازنامه ۱۲ ماهه شگل", true),
        ("تحلیل ترازنامه وبملت", true),
        ("تحلیل شغدیر", true),
        ("سهم شغدیر را بررسی کن", true),
        ("جمع دارایی‌های شگل چقدر است؟", true),
        ("سرمایه فاذر چند است؟", true),
        ("P/E فولاد چقدر است؟", true),
        ("صورت جریان وجوه نقد فولاد", true),
        ("صورت سود و زیان فولاد", true),
        ("گزارش سود و زیان شگل", true),
        ("صورت درآمد و هزینه فاذر", true),
        ("گزارشات فصلی دتولید", true),
        ("عملکرد ۵ فصل اخیر فولاد", true),
        ("گزارش فصلی فولاد", true),
        ("اطلاعیه‌های کدال فولاد", true),
        ("شرکتی که سود خالص ۱۲۳۴۵۶ داشته کدام است؟", true),
        ("فروش ماهانه شغدیر را نشان بده", true),
        // Intentionally re-routed by Feature 154 (recorded for the T02 diff, not asserted).
        ("آخرین ترازنامه کگل", false),
        ("آخرین ترازنامه غالبر", false),
        ("ترازنامه وبملت", false),
        ("صورت وضعیت مالی شگل", false),
        ("وضعیت مالی وبملت", false),
        ("بیلان فاذر", false),
        ("balance sheet وبملت", false),
        ("عملکرد مالی وبملت", false),
        ("صورت عملکرد مالی وبملت", false),
        ("تحلیل عملکرد مالی وبملت", false),
        ("گزارش ترازنامه فصلی فولاد", false),
        ("مقایسه ترازنامه فصلی وبملت", false),
        ("ترازنامه وبملت در ۱۴۰۵/۰۳/۳۱", false),
        ("ترازنامه تلفیقی وبملت ۱۴۰۵/۰۳/۳۱", false),
        ("ترازنامه وبملت را با پایان سال مالی قبل مقایسه کن", false),
        ("P&L وبملت", false),
        ("statement of profit or loss شگل", false),
        ("statement of operations فاذر", false)
    ];

    public sealed record RoutingSignature(
        string Phrase,
        bool Preserved,
        string? DeterministicCapability,
        bool HybridShortCircuit,
        bool LegacyQuarterly,
        bool LegacyStatementTable,
        string? LegacyStatementType,
        bool LegacyStatementAnalysis,
        bool ExecutorQuarterly,
        string? ExecutorStatementType);

    /// <summary>
    /// Routing signature of one phrase. <paramref name="v2"/> false reproduces the frozen V1-reachable methods
    /// (shared rules, interpreter with Feature 154 disabled); true uses the V2 entry points the V2 workflow and
    /// semantic executor call. The baseline was captured on HEAD, where both used the shared rules.
    /// </summary>
    public static RoutingSignature Capture(string phrase, bool preserved, bool v2 = true)
    {
        var deterministic = new DeterministicCapabilityInterpreter(Registry, null,
            v2 ? Feature154RoutingScope.V2 : Feature154RoutingScope.V1Frozen).Interpret(phrase);
        var quarterly = v2
            ? FinancialStatementTableIntentRules.LooksLikeV2QuarterlyReportTableQuery(phrase)
            : FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(phrase);
        var table = quarterly || (v2
            ? FinancialStatementTableIntentRules.LooksLikeV2FinancialStatementTableQuery(phrase)
            : FinancialStatementTableIntentRules.LooksLikeFinancialStatementTableQuery(phrase));
        var legacyQuery = quarterly
            ? v2 ? FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(phrase) : FinancialStatementTableIntentRules.BuildQuarterlyQuery(phrase)
            : v2 ? FinancialStatementTableIntentRules.BuildV2Query(phrase) : FinancialStatementTableIntentRules.BuildQuery(phrase);
        var enriched = v2 ? FinancialStatementTableIntentRules.BuildV2Query(phrase) : FinancialStatementTableIntentRules.BuildQuery(phrase);
        var executorType = v2
            ? FinancialStatementTableIntentRules.ResolveV2StatementType(phrase) ?? (quarterly ? FinancialStatementType.IncomeStatement : enriched.StatementType)
            : enriched.StatementType ?? (quarterly ? FinancialStatementType.IncomeStatement : null);
        return new RoutingSignature(
            phrase,
            preserved,
            deterministic.CapabilityCandidates.FirstOrDefault()?.CapabilityCode,
            quarterly,
            quarterly,
            table,
            table ? legacyQuery.StatementType?.ToString() : null,
            !table && (v2
                ? FinancialStatementAnalysisIntentRules.LooksLikeV2FinancialStatementAnalysisQuery(phrase)
                : FinancialStatementAnalysisIntentRules.LooksLikeFinancialStatementAnalysisQuery(phrase)),
            quarterly,
            executorType?.ToString());
    }

    internal static string BaselinePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "specs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "tests", "FinancialCopilot.UnitTests", "TestData", "Feature154",
            "routing-baseline.json");
    }

    [Fact]
    public void WriteBaseline_OnlyOnUnchangedCode()
    {
        // Written only on demand, from unchanged code.
        if (Environment.GetEnvironmentVariable("FEATURE154_WRITE_ROUTING_BASELINE") != "1") return;
        var signatures = Phrases.Select(item => Capture(item.Phrase, item.Preserved, v2: false)).ToArray();
        var path = BaselinePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(signatures,
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>F154-R-05: every V1-reachable method returns exactly what HEAD returned, for every phrase.</summary>
    [Fact]
    public void V1ReachableMethods_AreUnchangedForEveryPhrase()
    {
        var baseline = JsonSerializer.Deserialize<RoutingSignature[]>(File.ReadAllText(BaselinePath()))!;
        Assert.Equal(Phrases.Length, baseline.Length);
        foreach (var expected in baseline)
            Assert.Equal(expected, Capture(expected.Phrase, expected.Preserved, v2: false));
    }

    [Fact]
    public void PreservedPhrases_KeepTheirT02BaselineSignature()
    {
        var path = BaselinePath();
        Assert.True(File.Exists(path), $"Missing T02 baseline {path}.");
        var baseline = JsonSerializer.Deserialize<RoutingSignature[]>(File.ReadAllText(path))!;
        Assert.Equal(Phrases.Count(item => item.Preserved), baseline.Count(item => item.Preserved));
        foreach (var expected in baseline.Where(item => item.Preserved))
        {
            var actual = Capture(expected.Phrase, true);
            Assert.Equal(expected, actual);
        }
    }
}
