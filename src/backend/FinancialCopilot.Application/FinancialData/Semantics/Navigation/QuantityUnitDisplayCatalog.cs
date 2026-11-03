namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

public sealed record ReviewedQuantityUnit(string CanonicalCode, string PersianLabel,
    IReadOnlyList<string> SourceAliases, string ApprovalReference);

/// <summary>Versioned labels for exact provider quantity-unit evidence. No value conversion is performed.</summary>
public sealed class QuantityUnitDisplayCatalog
{
    public const string Version = "monthly-quantity-unit-display-v1";

    private static readonly IReadOnlyList<ReviewedQuantityUnit> Defaults =
    [
        new("ton", "تن", ["ton", "tons", "t", "تن"], "reviewed-source-unit-label:metric-ton:v1"),
        new("kilogram", "کیلوگرم", ["kg", "kgs", "kilogram", "kilograms", "کیلوگرم"], "reviewed-source-unit-label:kilogram:v1"),
        new("piece", "عدد", ["piece", "pieces", "عدد"], "reviewed-source-unit-label:piece:v1")
    ];

    private readonly IReadOnlyDictionary<string, ReviewedQuantityUnit> byAlias;

    public QuantityUnitDisplayCatalog(IReadOnlyList<ReviewedQuantityUnit>? entries = null)
    {
        var selected = entries ?? Defaults;
        byAlias = selected.SelectMany(entry => entry.SourceAliases.Select(alias => (Alias: Normalize(alias), Entry: entry)))
            .Where(item => item.Alias.Length > 0)
            .ToDictionary(item => item.Alias, item => item.Entry, StringComparer.OrdinalIgnoreCase);
    }

    public ReviewedQuantityUnit? Resolve(string? sourceUnit) =>
        string.IsNullOrWhiteSpace(sourceUnit) ? null : byAlias.GetValueOrDefault(Normalize(sourceUnit));

    private static string Normalize(string value) => value.Trim().Normalize().Replace('\u064a', '\u06cc')
        .Replace('\u0643', '\u06a9');
}
