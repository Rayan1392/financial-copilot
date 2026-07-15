using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.MarketReports;

internal sealed partial class MarketReportEvidenceAssembler(
    FinancialIngestionDbContext dbContext,
    IOptions<MarketReportOptions> options)
{
    private const string EvidenceSchemaVersion = "market-report-evidence-v1";
    private readonly MarketReportOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MarketReportEvidenceBundle> BuildPublicAsync(
        string segment,
        CancellationToken cancellationToken)
    {
        var normalizedSegment = string.IsNullOrWhiteSpace(segment) ? "all" : segment.Trim();
        var pulse = await dbContext.MarketPulseSnapshots.AsNoTracking()
            .Where(row => row.IsCurrent && row.Segment == normalizedSegment)
            .OrderByDescending(row => row.TradingDate)
            .ThenByDescending(row => row.GeneratedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new MarketReportValidationException("No eligible market-pulse snapshot is available for report generation.");

        var (from, to) = TehranDayWindow(pulse.TradingDate);
        var totalInsights = await dbContext.InsightEvents.AsNoTracking()
            .CountAsync(row => row.DetectedAtUtc >= from && row.DetectedAtUtc < to, cancellationToken);
        var insights = await dbContext.InsightEvents.AsNoTracking()
            .Where(row => row.DetectedAtUtc >= from && row.DetectedAtUtc < to)
            .OrderByDescending(row => row.ImportanceScore)
            .ThenByDescending(row => row.ConfidenceScore)
            .ThenByDescending(row => row.DetectedAtUtc)
            .Take(Math.Max(1, _options.MaximumPublicInsights))
            .ToArrayAsync(cancellationToken);

        return BuildBundle(pulse, insights, [], totalInsights, pulse.GeneratedAtUtc);
    }

    public async Task<MarketReportEvidenceBundle> BuildPersonalAsync(
        Guid tenantId,
        Guid actorId,
        string actorType,
        CancellationToken cancellationToken)
    {
        var pulse = await dbContext.MarketPulseSnapshots.AsNoTracking()
            .Where(row => row.IsCurrent && row.Segment == "all")
            .OrderByDescending(row => row.TradingDate)
            .ThenByDescending(row => row.GeneratedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new MarketReportValidationException("No eligible market-pulse snapshot is available for a personal digest.");
        var symbols = await dbContext.WatchlistSymbols.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.ActorId == actorId && row.ActorType == actorType)
            .OrderBy(row => row.Position)
            .Select(row => row.Symbol)
            .ToArrayAsync(cancellationToken);
        if (symbols.Length == 0)
            throw new MarketReportValidationException("A personal digest is unavailable until at least one symbol is followed.");

        var (from, to) = TehranDayWindow(pulse.TradingDate);
        var totalInsights = await dbContext.InsightEvents.AsNoTracking()
            .CountAsync(row => symbols.Contains(row.Symbol) && row.DetectedAtUtc >= from && row.DetectedAtUtc < to, cancellationToken);
        var insights = await dbContext.InsightEvents.AsNoTracking()
            .Where(row => symbols.Contains(row.Symbol) && row.DetectedAtUtc >= from && row.DetectedAtUtc < to)
            .OrderByDescending(row => row.ImportanceScore)
            .ThenByDescending(row => row.ConfidenceScore)
            .ThenByDescending(row => row.DetectedAtUtc)
            .Take(Math.Max(1, _options.MaximumPersonalInsights))
            .ToArrayAsync(cancellationToken);

        return BuildBundle(pulse, insights, symbols, totalInsights, pulse.GeneratedAtUtc);
    }

    private static MarketReportEvidenceBundle BuildBundle(
        MarketPulseSnapshotRow pulse,
        IReadOnlyCollection<InsightEventRow> insights,
        IReadOnlyList<string> followedSymbols,
        int totalInsights,
        DateTimeOffset assembledAtUtc)
    {
        var items = new List<MarketReportEvidenceItem>();
        var metaText = $"Trading date {pulse.TradingDate:yyyy-MM-dd}; pulse revision {pulse.Revision}; window {pulse.CadenceSlot}.";
        items.Add(Item(
            $"pulse:{pulse.Id:N}:meta", "PulseMetadata", "بازه گزارش", metaText,
            "MarketPulseSnapshots", pulse.SourceWatermarkUtc ?? pulse.GeneratedAtUtc, 1m));

        var facts = Deserialize<MarketPulseFact[]>(pulse.FactsJson) ?? [];
        foreach (var fact in facts.OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            var text = fact.Value.HasValue
                ? $"{fact.LabelFa}: {fact.Value.Value.ToString(CultureInfo.InvariantCulture)} {fact.Unit}; status {fact.Status}."
                : $"{fact.LabelFa}: unavailable; status {fact.Status}; reason {fact.Reason ?? "not supplied"}.";
            items.Add(Item(
                $"pulse:{pulse.Id:N}:fact:{fact.Code}", "PulseFact", fact.LabelFa, text,
                "MarketPulseSnapshots", pulse.SourceWatermarkUtc ?? pulse.GeneratedAtUtc,
                fact.Status == MarketPulseFactStatus.Available ? 1m : 0m,
                fact.Value.HasValue ? [Canonical(fact.Value.Value)] : []));
        }

        var breadth = Deserialize<MarketPulseBreadth>(pulse.BreadthJson);
        if (breadth is not null)
        {
            var text = $"Positive {breadth.Advancing?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}; " +
                       $"negative {breadth.Declining?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}; " +
                       $"unchanged {breadth.Unchanged?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}; " +
                       $"included {breadth.IncludedInstruments}; excluded {breadth.ExcludedInstruments}; status {breadth.Status}.";
            items.Add(Item(
                $"pulse:{pulse.Id:N}:breadth", "Breadth", "عرض بازار", text,
                "MarketPulseSnapshots", pulse.SourceWatermarkUtc ?? pulse.GeneratedAtUtc,
                breadth.Status == MarketPulseFactStatus.Available ? 1m : 0.5m));
        }

        AddJsonItems<MarketPulseIndustryDriver>(items, pulse, pulse.LeadingIndustriesJson, "LeadingIndustry", "صنعت پیشرو");
        AddJsonItems<MarketPulseIndustryDriver>(items, pulse, pulse.LaggingIndustriesJson, "LaggingIndustry", "صنعت ضعیف");
        AddJsonItems<MarketPulseComparison>(items, pulse, pulse.ComparisonsJson, "Comparison", "مقایسه تاریخی");

        foreach (var insight in insights)
        {
            var text = $"{insight.Symbol}: {insight.Title}. {insight.Summary} Reason: {insight.Reason}. Evidence: {insight.EvidenceJson}";
            items.Add(Item(
                $"insight:{insight.Id:N}", "InsightEvent", insight.Title, text,
                $"InsightEvents/{insight.SourceProviderName}", insight.DetectedAtUtc, insight.ConfidenceScore));
        }

        var caveats = new List<string> { "این گزارش صرفاً اطلاع‌رسانی و مبتنی بر شواهد است و توصیه مالی نیست." };
        var excluded = new List<string>();
        if (pulse.IsPartial) caveats.Add("پوشش داده برای این بازه ناقص است.");
        if (!pulse.IsFinal) caveats.Add("این گزارش درون‌روزی است و گزارش نهایی پایان روز نیست.");
        foreach (var fact in facts.Where(item => item.Status != MarketPulseFactStatus.Available))
            excluded.Add($"{fact.Code}: {fact.Reason ?? fact.Status.ToString()}");
        if (totalInsights > insights.Count)
            excluded.Add($"{totalInsights - insights.Count} lower-ranked insight events were excluded by the governed report limit.");
        if (followedSymbols.Count > 0 && insights.Count == 0)
            caveats.Add("برای نمادهای دنبال‌شده در این بازه رویداد بااهمیتی ثبت نشده است.");

        var confidenceParts = new List<decimal>();
        if (breadth is not null)
        {
            var total = breadth.IncludedInstruments + breadth.ExcludedInstruments;
            confidenceParts.Add(total == 0 ? 0m : Math.Clamp((decimal)breadth.IncludedInstruments / total, 0m, 1m));
        }
        confidenceParts.AddRange(insights.Select(row => Math.Clamp(row.ConfidenceScore, 0m, 1m)));
        var confidence = confidenceParts.Count == 0 ? 0m : decimal.Round(confidenceParts.Average(), 4);

        return new MarketReportEvidenceBundle(
            EvidenceSchemaVersion,
            pulse.TradingDate,
            pulse.CadenceSlot,
            pulse.IsPartial,
            pulse.IsFinal,
            [pulse.Id],
            insights.Select(row => row.Id).ToArray(),
            followedSymbols,
            items,
            caveats,
            excluded,
            pulse.SourceWatermarkUtc,
            confidence,
            assembledAtUtc);
    }

    private static void AddJsonItems<T>(
        ICollection<MarketReportEvidenceItem> target,
        MarketPulseSnapshotRow pulse,
        string json,
        string kind,
        string label)
    {
        var values = Deserialize<T[]>(json) ?? [];
        var index = 0;
        foreach (var value in values)
        {
            var text = JsonSerializer.Serialize(value, JsonOptions);
            target.Add(Item(
                $"pulse:{pulse.Id:N}:{kind.ToLowerInvariant()}:{index++}", kind, label, text,
                "MarketPulseSnapshots", pulse.SourceWatermarkUtc ?? pulse.GeneratedAtUtc, 1m));
        }
    }

    private static MarketReportEvidenceItem Item(
        string id,
        string kind,
        string label,
        string text,
        string source,
        DateTimeOffset? freshness,
        decimal? confidence,
        IReadOnlyList<string>? numericValues = null) =>
        new(id, kind, label, text,
            numericValues ?? ExtractNumbers(text), null, source, freshness, confidence);

    private static IReadOnlyList<string> ExtractNumbers(string value) =>
        NumberRegex().Matches(NormalizeDigits(value))
            .Select(match => Canonical(match.Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string Canonical(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    internal static string Canonical(string value) =>
        decimal.TryParse(value.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? Canonical(parsed)
            : value;

    internal static string NormalizeDigits(string value)
    {
        var chars = value.Select(ch => ch switch
        {
            >= '۰' and <= '۹' => (char)('0' + ch - '۰'),
            >= '٠' and <= '٩' => (char)('0' + ch - '٠'),
            _ => ch
        }).ToArray();
        return new string(chars);
    }

    private static (DateTimeOffset From, DateTimeOffset To) TehranDayWindow(DateOnly date)
    {
        var zone = ResolveTehranTimeZone();
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        var next = local.AddDays(1);
        var to = new DateTimeOffset(next, zone.GetUtcOffset(next)).ToUniversalTime();
        return (from, to);
    }

    private static TimeZoneInfo ResolveTehranTimeZone()
    {
        foreach (var id in new[] { "Iran Standard Time", "Asia/Tehran" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    private static T? Deserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return default; }
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])[-+]?\d+(?:[.,]\d+)?")]
    private static partial Regex NumberRegex();
}
