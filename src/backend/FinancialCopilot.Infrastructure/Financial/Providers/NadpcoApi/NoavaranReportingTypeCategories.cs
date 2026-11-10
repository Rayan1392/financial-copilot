namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

/// <summary>
/// Customer-facing labels of the report types that have no verified monthly route. The route decision itself
/// belongs to <see cref="NoavaranMonthlyReportTypeResolver"/>; the labels are the owner-supplied definitions in
/// specs/141-schema-aware-semantic-query-engine/curated-catalog-review.md.
/// </summary>
internal static class NoavaranReportingTypeCategories
{
    private static readonly IReadOnlyDictionary<int, (string Fa, string En)> Labels =
        new Dictionary<int, (string Fa, string En)>
        {
            [1_000_001] = ("ساختمانی", "construction"),
            [1_000_002] = ("سرمایه‌گذاری", "investment"),
            [1_000_003] = ("بانکی", "banking"),
            [1_000_004] = ("لیزینگی", "leasing"),
            [1_000_006] = ("بیمه‌ای", "insurance"),
            [1_000_007] = ("حمل و نقل دریایی", "maritime transportation"),
            [1_000_009] = ("تأمین سرمایه", "investment banking")
        };

    public static (string Fa, string En)? For(int? reportingType) =>
        reportingType is { } code && Labels.TryGetValue(code, out var label) ? label : null;
}
