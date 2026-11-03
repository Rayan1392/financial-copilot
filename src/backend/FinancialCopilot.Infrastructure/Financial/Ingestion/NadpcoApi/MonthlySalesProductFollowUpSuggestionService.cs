using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class MonthlySalesProductFollowUpSuggestionService(
    ICompanyResolverService companyResolver,
    IMonthlyProductCatalogReadRepository productCatalog,
    IConversationalCapabilityRegistry registry,
    ILogger<MonthlySalesProductFollowUpSuggestionService> logger)
    : IMonthlySalesProductFollowUpSuggestionService
{
    private static readonly ActivitySource ActivitySource = new("FinancialCopilot.Feature137", "1.0");
    private const int MaximumActions = 3;

    public async Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(
        MonthlyActivityTrendResponse trend,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("monthly_sales_product_follow_up.select");
        var started = Stopwatch.GetTimestamp();
        activity?.SetTag("feature", "137");
        activity?.SetTag("selector.invoked", true);
        activity?.SetTag("selector.anchor_period", $"{trend.LatestReportYear:D4}/{trend.LatestReportMonth:D2}");
        try
        {
            if (trend.LatestReportYear < 1 || trend.LatestReportMonth is < 1 or > 12)
                return Empty("invalid_company_period");

            var company = await companyResolver.ResolveBySymbolAsync(trend.CompanySymbol, ct);
            var canonicalSymbol = company?.TseSymbol ?? company?.Ticker ?? company?.CompanySymbol;
            if (company is null || string.IsNullOrWhiteSpace(company.ExternalCompanyId) || string.IsNullOrWhiteSpace(canonicalSymbol))
                return Empty("unresolved_company_or_symbol");

            var read = await productCatalog.GetProductFollowUpReadAsync(
                company.ExternalCompanyId,
                new JalaliPeriod(trend.LatestReportYear, trend.LatestReportMonth),
                ct);
            activity?.SetTag("selector.candidate_count", read.CandidateUniverse.Count);
            if (read.AnchorPeriod is not { } anchor)
                return Empty("no_anchor_period");
            activity?.SetTag("selector.anchor_period", anchor.ToString());

            var candidates = MonthlyProductTrendProductIdentity.BuildCandidates(company.ExternalCompanyId, read.CandidateUniverse);
            var duplicateTitleKeys = candidates
                .GroupBy(candidate => MonthlyProductTrendCalculator.NormalizeProductText(candidate.DisplayTitle), StringComparer.Ordinal)
                .Where(group => group.Key.Length == 0 || group.Select(candidate => candidate.ProductKey).Distinct(StringComparer.Ordinal).Skip(1).Any())
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            var anchorGroups = read.AnchorObservations
                .Where(row => row.SalesAmount.HasValue)
                .GroupBy(row => MonthlyProductTrendProductIdentity.ProductKey(company.ExternalCompanyId, row), StringComparer.Ordinal);

            var eligible = new List<(MonthlyProductTrendCandidate Candidate, decimal SalesAmount, string NormalizedTitle)>();
            var parserRejections = 0;
            var ambiguityCount = 0;
            var duplicateCount = 0;
            foreach (var group in anchorGroups)
            {
                var candidate = candidates.SingleOrDefault(item => string.Equals(item.ProductKey, group.Key, StringComparison.Ordinal));
                if (candidate is null || !group.Any(row => row.SalesAmount.HasValue)) continue;
                var normalizedTitle = MonthlyProductTrendCalculator.NormalizeProductText(candidate.DisplayTitle);
                if (string.IsNullOrWhiteSpace(normalizedTitle) ||
                    string.Equals(candidate.DisplayTitle.Trim(), "بدون عنوان", StringComparison.Ordinal))
                {
                    parserRejections++;
                    continue;
                }
                if (duplicateTitleKeys.Contains(normalizedTitle))
                {
                    duplicateCount++;
                    continue;
                }

                var queryText = $"روند فروش {candidate.DisplayTitle} {canonicalSymbol}";
                if (candidate.DisplayTitle.Length > 120 || canonicalSymbol.Length > 120 || queryText.Length > 500 ||
                    !MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(queryText))
                {
                    parserRejections++;
                    continue;
                }
                var parsed = MonthlyProductTrendIntentRules.BuildQuery(queryText);
                if (string.IsNullOrWhiteSpace(parsed.CompanyText) || string.IsNullOrWhiteSpace(parsed.ProductText) ||
                    !string.Equals(MonthlyProductTrendCalculator.NormalizeProductText(parsed.CompanyText), MonthlyProductTrendCalculator.NormalizeProductText(canonicalSymbol), StringComparison.Ordinal) ||
                    !string.Equals(MonthlyProductTrendCalculator.NormalizeProductText(parsed.ProductText), normalizedTitle, StringComparison.Ordinal))
                {
                    parserRejections++;
                    continue;
                }
                var matches = MonthlyProductTrendProductIdentity.ResolveMatches(candidates, parsed.ProductText);
                if (matches.Length != 1 || !string.Equals(matches[0].ProductKey, candidate.ProductKey, StringComparison.Ordinal))
                {
                    ambiguityCount++;
                    continue;
                }

                try
                {
                    decimal total = 0m;
                    foreach (var row in group)
                        if (row.SalesAmount is { } amount) total = checked(total + amount);
                    eligible.Add((candidate, total, normalizedTitle));
                }
                catch (OverflowException)
                {
                    activity?.SetTag("selector.zero_action_reason", "sales_aggregation_overflow");
                }
            }

            activity?.SetTag("selector.eligible_count", eligible.Count);
            activity?.SetTag("selector.parser_rejection_count", parserRejections);
            activity?.SetTag("selector.ambiguity_count", ambiguityCount);
            activity?.SetTag("selector.duplicate_count", duplicateCount);
            var actions = eligible
                .OrderByDescending(item => item.SalesAmount)
                .ThenBy(item => item.NormalizedTitle, StringComparer.Ordinal)
                .ThenBy(item => item.Candidate.ProductKey, StringComparer.Ordinal)
                .Take(MaximumActions)
                .Select(item => CreateAction(item.Candidate, canonicalSymbol, company.ExternalCompanyId))
                .ToArray();
            activity?.SetTag("selector.returned_count", actions.Length);
            if (actions.Length > 0) activity?.SetTag("selector.action_attribution", "monthly_sales_product_follow_up");
            if (actions.Length == 0) activity?.SetTag("selector.zero_action_reason", "no_eligible_products");
            return actions;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetTag("selector.read_failure", true);
            activity?.SetTag("selector.zero_action_reason", "read_or_selection_failure");
            logger.LogWarning(exception, "Feature 137 follow-up suggestion selection failed; returning no actions.");
            return [];
        }
        finally
        {
            activity?.SetTag("selector.duration_ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private IReadOnlyCollection<SuggestedAction> Empty(string reason)
    {
        Activity.Current?.SetTag("selector.returned_count", 0);
        Activity.Current?.SetTag("selector.zero_action_reason", reason);
        return [];
    }

    private SuggestedAction CreateAction(MonthlyProductTrendCandidate candidate, string companySymbol, string externalCompanyId)
    {
        var query = $"روند فروش {candidate.DisplayTitle} {companySymbol}";
        var idInput = $"feature137\nmonthly_product_trend\n{registry.Version}\n{externalCompanyId}\n{candidate.ProductKey}";
        var id = "feature137:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idInput))).ToLowerInvariant();
        return new SuggestedAction(
            id,
            SuggestedActionKind.RunRelatedCapability,
            query,
            query,
            "monthly_product_trend",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["company"] = companySymbol,
                ["product"] = candidate.DisplayTitle
            },
            "monthly_sales_product_follow_up",
            registry.Version);
    }
}
