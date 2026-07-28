using System.Diagnostics.Metrics;
using System.Diagnostics;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

internal static class DisclosureListingTelemetry
{
    private static readonly Meter Meter = new("FinancialCopilot.Disclosures", "1.0");
    private static readonly Counter<long> Queries = Meter.CreateCounter<long>("disclosure_listing_queries");
    private static readonly Counter<long> EmptyQueries = Meter.CreateCounter<long>("disclosure_listing_empty_queries");
    private static readonly Counter<long> Results = Meter.CreateCounter<long>("disclosure_listing_results");
    private static readonly Histogram<double> DurationMs = Meter.CreateHistogram<double>("disclosure_listing_duration_ms");

    public static void Record(DisclosureListingQuery query, DisclosureListingResult? result, double durationMs, string outcome)
    {
        var types = query.Types is { Count: > 0 } ? string.Join(',', query.Types.Order()) : "all";
        var tags = new TagList { { "channel", query.Channel }, { "types", types }, { "scope", query.ConsolidationScope.ToString() }, { "outcome", outcome } };
        Queries.Add(1, tags);
        DurationMs.Record(durationMs, tags);
        if (result is not null)
        {
            var providers = result.Items.Select(item => item.ProviderName).Distinct(StringComparer.Ordinal).Order().Take(4);
            var resultTags = new TagList { { "channel", query.Channel }, { "providers", string.Join(',', providers) }, { "coverage", result.CoverageStatus.ToString() }, { "freshness", result.FreshnessReasonCode } };
            Results.Add(result.TotalCount, resultTags);
            if (result.TotalCount == 0) EmptyQueries.Add(1, resultTags);
        }
    }
}
