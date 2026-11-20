namespace FinancialCopilot.Application.AI.GroundedAnalysis;

/// <summary>
/// The client's analysis choice on <c>POST /api/ai/v1/query</c> (FR-4). Every member is optional; missing members use
/// the configured defaults (Deep, web on). There is no amount: <see cref="PricingVersion"/> only tells the backend which
/// price list the client showed, so a stale page is detected before anything runs (PO-9).
/// </summary>
public sealed record AnalysisOptionsRequest(
    AnalysisDepth? Depth = null,
    bool? WebSearch = null,
    string? PricingVersion = null)
{
    public AnalysisChoice Resolve(AnalysisChoice defaults) =>
        new(Depth ?? defaults.Depth, WebSearch ?? defaults.WebSearch);
}

/// <summary>A pending 163 analysis offer as clients see it.</summary>
public sealed record GroundedAnalysisOfferHint(
    Guid OfferId,
    string Symbol,
    DateTimeOffset ExpiresAt,
    bool SufficientBalance,
    string ConfirmActionId,
    string CancelActionId);

/// <summary>
/// What a client needs to show the analysis controls and the cost before sending (PO-9): the backend price list,
/// its version, the defaults and whether the account is unlimited. <see cref="Available"/> is true when the next
/// question may be answered by Report Discussion; <see cref="Offer"/> is set on a 163 offer.
/// </summary>
public sealed record GroundedAnalysisHint(
    bool Available,
    string PricingVersion,
    IReadOnlyList<GroundedAnalysisPrice> Prices,
    AnalysisChoice Defaults,
    bool Unlimited,
    GroundedAnalysisOfferHint? Offer = null);

/// <summary>
/// One external source an answer cites with a <c>[Wn]</c> marker (FR-10). Only sources the hosted web search actually
/// consulted are kept; the server writes the «منابع» block from these.
/// </summary>
public sealed record ExternalSource(
    string Id,
    string Title,
    string Url,
    string PublishedAt);

/// <summary>
/// Persisted on the assistant message of a 163 offer (FR-7). The question is the user message of the same exchange
/// and is read from the conversation, never copied here.
/// </summary>
public sealed record GroundedAnalysisOffer(
    Guid OfferId,
    string CompanyCanonicalId,
    string Symbol,
    string NewestPeriod,
    string PricingVersion,
    DateTimeOffset ExpiresAt);

/// <summary>Persisted on an answered analysis: what ran and at which tier (no amount, no token count).</summary>
public sealed record GroundedAnalysisRun(
    string Depth,
    bool WebSearch,
    string Tier,
    string Model);

/// <summary>
/// The facts of one analysis call for cost telemetry (FR-13): monitoring only, never an input to billing (PO-7).
/// </summary>
public sealed record GroundedAnalysisCallFacts(
    AnalysisChoice Choice,
    FinancialCopilot.Application.AI.ModelProviders.AiExecutionUsageFacts? Usage,
    int ConsultedSources);

/// <summary>The Persian names of the depths, tiers and amounts, shared by every server-written analysis text.</summary>
public static class AnalysisLabels
{
    public static string Depth(AnalysisDepth depth) => depth == AnalysisDepth.Simple ? "ساده" : "عمیق";

    public static string Tier(AnalysisDepth depth, bool webSearch) => $"{Depth(depth)} {(webSearch ? "با وب" : "بدون وب")}";

    public static string Credits(decimal credits) =>
        $"{PersianDigits(credits.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))} اعتبار";

    public static string PersianDigits(string text) =>
        string.Concat(text.Select(character => character is >= '0' and <= '9' ? "۰۱۲۳۴۵۶۷۸۹"[character - '0'] : character));
}

/// <summary>The account facts an offer shows (FR-7): read only, never a reservation.</summary>
public sealed record GroundedAnalysisBalance(decimal AvailableCredits, bool Unlimited);

/// <summary>Reads the caller's spendable balance and unlimited status without reserving anything (PO-11).</summary>
public interface IGroundedAnalysisBalanceReader
{
    Task<GroundedAnalysisBalance?> ReadAsync(
        Guid tenantId,
        Guid actorId,
        Guid? userId,
        Guid? apiClientId,
        string? externalUserId,
        CancellationToken cancellationToken);
}

/// <summary>The suggested-action ids of a 163 offer (design-proposal §6): <c>ga1:{offerId}:confirm|cancel</c>.</summary>
public static class GroundedAnalysisOfferActions
{
    public const string Prefix = "ga1:";
    private const string ConfirmSuffix = ":confirm";
    private const string CancelSuffix = ":cancel";

    public static string Confirm(Guid offerId) => $"{Prefix}{offerId:N}{ConfirmSuffix}";

    public static string Cancel(Guid offerId) => $"{Prefix}{offerId:N}{CancelSuffix}";

    public static bool IsOfferAction(string? suggestedActionId) =>
        suggestedActionId?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>Parses an offer action; false for anything that is not exactly confirm or cancel of one offer id.</summary>
    public static bool TryParse(string? suggestedActionId, out Guid offerId, out bool confirm)
    {
        offerId = Guid.Empty;
        confirm = false;
        if (!IsOfferAction(suggestedActionId)) return false;
        var body = suggestedActionId![Prefix.Length..];
        string idText;
        if (body.EndsWith(ConfirmSuffix, StringComparison.Ordinal))
        {
            confirm = true;
            idText = body[..^ConfirmSuffix.Length];
        }
        else if (body.EndsWith(CancelSuffix, StringComparison.Ordinal))
        {
            idText = body[..^CancelSuffix.Length];
        }
        else
        {
            return false;
        }

        return Guid.TryParseExact(idText, "N", out offerId) && offerId != Guid.Empty;
    }
}
