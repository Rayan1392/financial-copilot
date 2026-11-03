using System.Globalization;
using System.Text.Json;

namespace FinancialCopilot.Infrastructure.Authentication;

/// <summary>Resolves an announcement's fiscal-year end only from applicable authoritative dates.</summary>
internal static class NoavaranFiscalYearEvidenceResolver
{
    public static bool ContainsFiscalYearEnd(string metadataJson, string expected) =>
        ReadFiscalYearEnds(metadataJson).Contains(expected, StringComparer.Ordinal);

    public static string? Resolve(
        DateOnly reportingPeriodEnd,
        string? companyFiscalYearEnd,
        IEnumerable<string> statementMetadata)
    {
        var candidates = statementMetadata.SelectMany(ReadFiscalYearEnds).ToList();
        if (!string.IsNullOrWhiteSpace(companyFiscalYearEnd))
        {
            var companyEnd = CanonicalJalaliDate(companyFiscalYearEnd);
            if (companyEnd is not null) candidates.Add(companyEnd);
            else return null;
        }

        var applicable = candidates
            .Distinct(StringComparer.Ordinal)
            .Where(candidate => AppliesTo(reportingPeriodEnd, candidate))
            .ToArray();
        return applicable.Length == 1 ? applicable[0] : null;
    }

    private static IEnumerable<string> ReadFiscalYearEnds(string json)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { yield break; }

        using (document)
        {
            var nodes = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToArray()
                : new[] { document.RootElement };
            foreach (var node in nodes)
            {
                if (node.ValueKind != JsonValueKind.Object ||
                    !node.TryGetProperty("jalaliFiscalYearEnd", out var value) ||
                    value.ValueKind != JsonValueKind.String) continue;
                var canonical = CanonicalJalaliDate(value.GetString());
                if (canonical is not null) yield return canonical;
            }
        }
    }

    private static string? CanonicalJalaliDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length != 8 ||
            !int.TryParse(digits[..4], CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(digits[4..6], CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(digits[6..], CultureInfo.InvariantCulture, out var day)) return null;
        try { _ = new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0); }
        catch (ArgumentOutOfRangeException) { return null; }
        return $"{year:D4}/{month:D2}/{day:D2}";
    }

    private static bool AppliesTo(DateOnly periodEnd, string jalaliFiscalYearEnd)
    {
        var parts = jalaliFiscalYearEnd.Split('/');
        var calendar = new PersianCalendar();
        var end = calendar.ToDateTime(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture), 0, 0, 0, 0);
        // AddYears uses PersianCalendar, including Esfand 30 leap-year clamping.
        var start = DateOnly.FromDateTime(calendar.AddYears(end, -1).Date).AddDays(1);
        var fiscalEnd = DateOnly.FromDateTime(end);
        return periodEnd >= start && periodEnd <= fiscalEnd;
    }
}
