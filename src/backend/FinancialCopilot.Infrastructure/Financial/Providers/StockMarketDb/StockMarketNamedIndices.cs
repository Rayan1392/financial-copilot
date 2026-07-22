namespace FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;

/// <summary>
/// Authoritative catalog of the named market indices that the daily-index sync reads from
/// <c>Tse.IndexNew</c>. Each entry pairs the source <c>InstrumentRef</c> with its Persian name.
/// This is the single owner of these identifiers; queries and tests derive from it rather than
/// repeating the GUID literals.
/// </summary>
public static class StockMarketNamedIndices
{
    public sealed record NamedIndex(Guid InstrumentRef, string PersianName, long? TsetmcInstrumentCode = null);

    public static readonly IReadOnlyList<NamedIndex> All =
    [
        new(Guid.Parse("36423CB8-D33B-47AD-89D4-06FA49592CBA"), "شاخص کل", 32097828799138957),
        new(Guid.Parse("1B32B991-F48A-4F7E-9C0C-328D0B093EA5"), "شاخص کل فرابورس"),
        new(Guid.Parse("B27FA320-194F-4710-8D12-277E245D33C5"), "شاخص بازده نقدی و قیمت"),
        new(Guid.Parse("47CE7543-C052-4C44-BF0D-29281818FCA5"), "شاخص ۵۰ شرکت فعال‌تر"),
        new(Guid.Parse("42FCE63E-6CEB-405B-9179-78606C210D86"), "شاخص قیمت (هم‌وزن)"),
        new(Guid.Parse("D01F9D84-A1C8-46F3-A959-800DEF9E112F"), "شاخص کل (هم‌وزن)", 67130298613737946),
    ];

    public static IReadOnlyList<Guid> InstrumentRefs { get; } =
        All.Select(index => index.InstrumentRef).ToArray();

    public static IReadOnlyList<long> TsetmcInstrumentCodes { get; } =
        All.Select(index => index.TsetmcInstrumentCode)
            .OfType<long>()
            .ToArray();
}
