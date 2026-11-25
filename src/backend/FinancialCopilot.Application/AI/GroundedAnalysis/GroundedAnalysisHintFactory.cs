using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;

namespace FinancialCopilot.Application.AI.GroundedAnalysis;

/// <summary>
/// Builds the <see cref="GroundedAnalysisHint"/> of a response (design-proposal §7): the backend price list and its
/// version, the defaults, the caller's unlimited status and, for a 163 offer, whether the balance covers the default
/// tier. Read only: it never reserves. A failed balance read never breaks the turn (the hint is advisory; confirmation
/// and Report Discussion enforce the balance themselves).
/// </summary>
public sealed class GroundedAnalysisHintFactory(
    GroundedAnalysisOptions options,
    GroundedAnalysisPricing pricing,
    IGroundedAnalysisBalanceReader? balances = null)
{
    public bool Enabled => options.Enabled;

    /// <summary>
    /// Whether the caller's spendable balance covers this tier (always for an unlimited account); null when it cannot be
    /// read. A 163 confirmation checks this before it claims the offer, so a short balance never uses up the offer: it
    /// stays confirmable until it expires (design-proposal §6). The reservation still enforces the balance.
    /// </summary>
    public async Task<bool?> CoversAsync(AiQueryRequest request, AnalysisChoice choice, CancellationToken cancellationToken)
    {
        if (balances is null) return null;
        try
        {
            var balance = await balances.ReadAsync(request.TenantId, request.ActorId, request.UserId, request.ApiClientId,
                request.ExternalUserId, cancellationToken);
            return balance is null ? null : balance.Unlimited || balance.AvailableCredits >= pricing.CreditsFor(choice);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// OQ-113 (a): the hint of a saved answer, for a reloaded conversation. Only the conversation's latest assistant
    /// message may carry it (a later message has already answered or replaced it); availability is the decision stored
    /// when the turn ran, an expired offer is left out, and prices and balance are read now. The confirmation still
    /// re-checks the offer, the prices and the balance.
    /// </summary>
    public Task<GroundedAnalysisHint?> ForSavedAnswerAsync(
        AiQueryRequest caller,
        FinancialCopilot.Application.Conversations.AssistantMessagePayload payload,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var offer = payload.GroundedAnalysisOffer is { } saved && saved.ExpiresAt > now ? saved : null;
        return CreateAsync(caller, payload.GroundedAnalysisAvailable == true, offer, cancellationToken,
            UsesMonthlySalesDefaults(payload.MonthlyActivityTrendResult is not null, payload.ReportDiscussion));
    }

    public async Task<GroundedAnalysisHint?> CreateAsync(
        AiQueryRequest request,
        bool available,
        GroundedAnalysisOffer? offer,
        CancellationToken cancellationToken,
        bool monthlySales = false)
    {
        if (!options.Enabled || !available && offer is null) return null;

        GroundedAnalysisBalance? balance = null;
        if (balances is not null)
        {
            try
            {
                balance = await balances.ReadAsync(request.TenantId, request.ActorId, request.UserId, request.ApiClientId,
                    request.ExternalUserId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                balance = null;
            }
        }

        var unlimited = balance?.Unlimited == true;
        var offerHint = offer is null
            ? null
            : new GroundedAnalysisOfferHint(
                offer.OfferId,
                offer.Symbol,
                offer.ExpiresAt,
                // Unknown balance: the confirmation checks it; the offer is not blocked on a failed read.
                unlimited || balance is null || balance.AvailableCredits >= pricing.CreditsFor(options.DefaultChoice),
                GroundedAnalysisOfferActions.Confirm(offer.OfferId),
                GroundedAnalysisOfferActions.Cancel(offer.OfferId));
        var useReportDefaults = available && monthlySales && offer is null;
        var defaults = useReportDefaults ? MonthlySalesDefaults : options.DefaultChoice;
        return new GroundedAnalysisHint(available, pricing.PricingVersion, pricing.Prices, defaults, unlimited, offerHint, useReportDefaults);
    }

    internal static AnalysisChoice MonthlySalesDefaults => new(AnalysisDepth.Simple, false);

    public static bool UsesMonthlySalesDefaults(bool hasMonthlySalesReport, ReportDiscussionMarker? marker) =>
        hasMonthlySalesReport || marker is { DeactivatesReport: false, ReportFamily: nameof(ReportFamily.MonthlySales) };
}
