using System.Globalization;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Feature 154 fixtures. §11.1 values are supplied by the Product Owner (million IRR). Subtotals 135/137/139/140 of
/// وبملت are "present" in the spec without amounts; the values used here are the Development values observed in T01.
/// Every value marked SYNTHETIC is a clearly labelled synthetic comparison fixture (spec §11.2), not production data.
/// </summary>
internal static class BalanceSheet154Fixtures
{
    public static readonly Guid BankGroup = Guid.Parse("c96520f9-1d2e-444a-ab46-5bdffd3797d2");
    public static readonly Guid ShegolGroup = Guid.Parse("97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c");
    public static readonly Guid FazarGroup = Guid.Parse("baab0118-18a2-4adf-b668-c4c0bb05527d");

    private static readonly PersianCalendar Jalali = new();

    public static DateOnly J(int year, int month, int day) => DateOnly.FromDateTime(Jalali.ToDateTime(year, month, day, 0, 0, 0, 0));

    public static BalanceSheetStatementHeader Header(string externalId, DateOnly periodEnd, string periodType, string? fiscalYearEnd,
        bool audited = false, bool represented = false, DateOnly? published = null, bool composing = false,
        string? scale = "MillionRials", bool? sourceScope = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), externalId, periodEnd, periodType, audited, represented, composing, published, null,
            fiscalYearEnd, scale, sourceScope ?? composing);

    public static IReadOnlyList<BalanceSheetItemObservation> Items(params (int Id, decimal? Value)[] values) =>
        values.Select(value => new BalanceSheetItemObservation(value.Id, Guid.NewGuid(), value.Value, $"catalog-{value.Id}")).ToArray();

    // وبملت 409865 (supplied, §11.1). 298–305 equal their primaries (candidates only).
    public static IReadOnlyList<BalanceSheetItemObservation> WebMellatItems() => Items(
        (1, 409_287_621m), (57, 3_382_000_000m), (291, 45_209_234_799m), (223, 27_590_264_477m), (170, -181_354_538m),
        (60, 0m), (286, 6m), (146, 1_031_013_395m), (160, 6_481_715_419m),
        (135, 51_498_227_670m), (137, 7_612_077_011m), (139, 52_501_759_193m), (140, 126_830_069m),
        (138, 59_110_304_681m), (143, 52_628_589_262m), (147, 6_481_715_419m), (148, 59_110_304_681m),
        (300, 59_110_304_681m), (303, 52_628_589_262m), (304, 6_481_715_419m), (305, 59_110_304_681m));

    // شگل (supplied, §11.1).
    public static IReadOnlyList<BalanceSheetItemObservation> ShegolItems() => Items(
        (1, 3_263_057m), (57, 6_149_600m), (18, 10_266_826m), (47, 21_347_261m), (51, 0m), (58, 10_850_400m),
        (306, 26_549_068m), (293, 26_549_068m), (307, 16_116_006m), (308, 16_116_006m),
        (138, 63_522_534m), (143, 41_014_336m), (147, 22_508_198m), (148, 63_522_534m));

    // فاذر (supplied, §11.1). No cash observation; 146 equals 147 but is excluded.
    public static IReadOnlyList<BalanceSheetItemObservation> FazarItems() => Items(
        (57, 18_126_475m), (21, 87_160_528m), (45, 17_452_463m), (51, 0m), (64, 64_629_501m),
        (306, 40_302_278m), (307, 29_966_070m), (146, 85_394_709m),
        (138, 145_343_307m), (143, 59_948_598m), (147, 85_394_709m), (148, 145_343_307m));

    // SYNTHETIC S-1 previous report 403733 (1405/03/31).
    public static IReadOnlyList<BalanceSheetItemObservation> WebMellatS1Items() => Items(
        (1, 400_000_000m), (57, 3_382_000_000m), (135, 45_000_000_000m), (137, 5_000_000_000m), (139, 44_000_000_000m),
        (140, 523_000_000m), (138, 50_000_000_000m), (143, 44_523_000_000m), (147, 5_477_000_000m), (148, 50_000_000_000m));

    // SYNTHETIC S-2 شگل fiscal-year-end baseline (1404/09/30).
    public static IReadOnlyList<BalanceSheetItemObservation> ShegolS2Items() => Items(
        (138, 60_000_000m), (143, 40_000_000m), (147, 20_000_000m), (148, 60_000_000m), (57, 6_149_600m));

    public static IReadOnlyList<BalanceSheetItemObservation> Totals(decimal assets, decimal liabilities, decimal equity) => Items(
        (138, assets), (143, liabilities), (147, equity), (148, assets), (57, 1m));
}

internal sealed class FakeBalanceSheetReader : IBalanceSheetStatementReader
{
    public List<BalanceSheetStatementHeader> Headers { get; } = [];
    public List<BalanceSheetFiscalEvidence> Evidence { get; } = [];
    public Dictionary<Guid, IReadOnlyList<BalanceSheetItemObservation>> Lines { get; } = [];
    public bool HasConsolidated { get; set; }
    public int Calls { get; private set; }
    public List<IReadOnlyCollection<Guid>> LineItemRequests { get; } = [];

    public BalanceSheetStatementHeader Add(BalanceSheetStatementHeader header, IReadOnlyList<BalanceSheetItemObservation> items)
    {
        Headers.Add(header);
        Lines[header.StatementId] = items;
        return header;
    }

    public Task<BalanceSheetHeaderSet> ReadHeadersAsync(string providerName, string externalCompanyId, CancellationToken cancellationToken = default)
    {
        Calls += 2;
        return Task.FromResult(new BalanceSheetHeaderSet(
            Headers.Where(header => !header.IsComposing).OrderByDescending(header => header.PeriodEnd).Take(BalanceSheetReportQueryUseCase.HeaderWindow).ToArray(),
            Evidence.ToArray()));
    }

    public Task<IReadOnlyList<BalanceSheetStatementHeader>> ReadHeadersAtDatesAsync(string providerName, string externalCompanyId,
        IReadOnlyCollection<DateOnly> periodEnds, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<BalanceSheetStatementHeader>>(
            Headers.Where(header => !header.IsComposing && periodEnds.Contains(header.PeriodEnd)).ToArray());
    }

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<BalanceSheetItemObservation>>> ReadLineItemsAsync(string providerName,
        IReadOnlyCollection<Guid> statementIds, IReadOnlyCollection<int> sourceItemIds, CancellationToken cancellationToken = default)
    {
        Calls++;
        LineItemRequests.Add(statementIds);
        var ids = sourceItemIds.ToHashSet();
        return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BalanceSheetItemObservation>>>(
            statementIds.Where(Lines.ContainsKey).ToDictionary(id => id,
                id => (IReadOnlyList<BalanceSheetItemObservation>)Lines[id].Where(item => ids.Contains(item.SourceItemId)).ToArray()));
    }

    public Task<bool> ConsolidatedExistsAsync(string providerName, string externalCompanyId, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(HasConsolidated);
    }
}

internal sealed class FakeCompanyResolution : ICompanyResolutionOutcomeService
{
    public Task<CompanyResolutionOutcome> ResolveAsync(string? identifier, string providerName, CancellationToken cancellationToken = default) =>
        Task.FromResult(identifier switch
        {
            "وبملت" or "بانک ملت" => new CompanyResolutionOutcome(CompanyResolutionStatus.Resolved,
                new ResolvedCompany(Guid.Parse("308d5136-079d-489b-a8d3-8db222706f41"), "452", "وبملت", null, null, null, null, "وبملت", "وبملت", "بانک ملت")),
            "شگل" => new CompanyResolutionOutcome(CompanyResolutionStatus.Resolved,
                new ResolvedCompany(Guid.Parse("225ff342-3a18-4796-9e86-6af20691bbb2"), "167", "شگل", null, null, null, null, "شگل", "شگل", "گلتاش")),
            "فاذر" => new CompanyResolutionOutcome(CompanyResolutionStatus.Resolved,
                new ResolvedCompany(Guid.Parse("7ac9fcd3-fba5-42eb-b484-fa313faf07b1"), "290", "فاذر", null, null, null, null, "فاذر", "فاذر", "صنایع آذر آب")),
            "مبهم" => new CompanyResolutionOutcome(CompanyResolutionStatus.Ambiguous, Reason: "ambiguous_company"),
            _ => new CompanyResolutionOutcome(CompanyResolutionStatus.NotFound, Reason: "not_found")
        });
}

internal sealed class FakeClassification(Dictionary<string, Guid?[]> groups) : IQuarterlyFinancialReportsRepository
{
    public Task<IReadOnlyList<QuarterlyReportObservation>> GetIncomeStatementReportsAsync(QuarterlyFinancialReportSelection selection,
        CancellationToken cancellationToken = default) => throw new InvalidOperationException("Feature 154 never reads income statements.");

    public Task<IReadOnlyList<Guid?>> GetEligibleCompanyGroupsAsync(Guid canonicalCompanyId, string providerName, string externalCompanyId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid?>>(groups.TryGetValue(externalCompanyId, out var value) ? value : []);
}
