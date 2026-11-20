using System.Text;
using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «sol-deep-analysis-web-search» T13: the Telegram price line under a discussable report (OQ-111 a) and the 163
/// offer buttons — four tiers labelled with credits and «انصراف» — with callback data under 64 bytes.
/// </summary>
public sealed class TelegramGroundedAnalysisTests
{
    private static readonly Guid OfferId = Guid.Parse("0f8b6c1e-2d3a-4b5c-9d8e-7f6a5b4c3d2e");
    private static readonly AnalysisChoice DeepWeb = new(AnalysisDepth.Deep, true);

    private static readonly IReadOnlyList<GroundedAnalysisPrice> Prices =
    [
        new(AnalysisDepth.Simple, false, 8m), new(AnalysisDepth.Simple, true, 12m),
        new(AnalysisDepth.Deep, false, 16m), new(AnalysisDepth.Deep, true, 25m)
    ];

    private static GroundedAnalysisHint Hint(bool available = true, bool unlimited = false, bool? sufficient = null) =>
        new(available, "v1", Prices, DeepWeb, unlimited,
            sufficient is { } enough
                ? new GroundedAnalysisOfferHint(OfferId, "فولاد", DateTimeOffset.UtcNow.AddHours(1), enough,
                    GroundedAnalysisOfferActions.Confirm(OfferId), GroundedAnalysisOfferActions.Cancel(OfferId))
                : null);

    // ── Callback data ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AnalysisDepth.Simple, false)]
    [InlineData(AnalysisDepth.Simple, true)]
    [InlineData(AnalysisDepth.Deep, false)]
    [InlineData(AnalysisDepth.Deep, true)]
    public void TierCallback_RoundTrips_UnderTheTelegramLimit(AnalysisDepth depth, bool web)
    {
        var data = TelegramGroundedAnalysis.ChoiceCallbackData(OfferId, new AnalysisChoice(depth, web));

        Assert.True(Encoding.UTF8.GetByteCount(data) <= 64);
        Assert.True(TelegramGroundedAnalysis.TryParse(data, out var offerId, out var choice));
        Assert.Equal(OfferId, offerId);
        Assert.Equal(new AnalysisChoice(depth, web), choice);
    }

    [Fact]
    public void CancelCallback_RoundTrips_WithoutAChoice()
    {
        var data = TelegramGroundedAnalysis.CancelCallbackData(OfferId);

        Assert.True(TelegramGroundedAnalysis.TryParse(data, out var offerId, out var choice));
        Assert.Equal(OfferId, offerId);
        Assert.Null(choice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ga1:")]
    [InlineData("ga1:not-a-guid:d1")]
    [InlineData("ga1:0f8b6c1e2d3a4b5c9d8e7f6a5b4c3d2e")]
    [InlineData("ga1:0f8b6c1e2d3a4b5c9d8e7f6a5b4c3d2e:d2")]
    [InlineData("ga1:0f8b6c1e2d3a4b5c9d8e7f6a5b4c3d2e:confirm")]
    [InlineData("ga1:0f8b6c1e2d3a4b5c9d8e7f6a5b4c3d2e:d1:x")]
    [InlineData("GA1:0f8b6c1e2d3a4b5c9d8e7f6a5b4c3d2e:d1")]
    public void MalformedCallback_IsRejected(string? data) =>
        Assert.False(TelegramGroundedAnalysis.TryParse(data, out _, out _));

    // ── Price line (162) ──────────────────────────────────────────────────────────────

    [Fact]
    public void PriceLine_ShowsTheDefaultTierAndItsCredits() =>
        Assert.Equal("پرسش تحلیلی درباره‌ی این گزارش: عمیق با وب، ۲۵ اعتبار", TelegramGroundedAnalysis.PriceLine(Hint()));

    [Fact]
    public void PriceLine_ForMax_SaysNothingIsCharged() =>
        Assert.Equal("پرسش تحلیلی درباره‌ی این گزارش: عمیق با وب؛ با اشتراک Max اعتباری کسر نمی‌شود.",
            TelegramGroundedAnalysis.PriceLine(Hint(unlimited: true)));

    [Fact]
    public void PriceLine_IsAbsent_WithoutAvailability_OrOnAnOffer()
    {
        Assert.Null(TelegramGroundedAnalysis.PriceLine(null));
        Assert.Null(TelegramGroundedAnalysis.PriceLine(Hint(available: false)));
        Assert.Null(TelegramGroundedAnalysis.PriceLine(Hint(sufficient: true)));
    }

    // ── Offer buttons (163) ───────────────────────────────────────────────────────────

    [Fact]
    public void Offer_HasFourTierButtonsWithCredits_AndCancel()
    {
        var (note, actions) = TelegramGroundedAnalysis.Offer(Hint(sufficient: true));

        Assert.Null(note);
        Assert.Equal(
            ["تحلیل کن — ساده بدون وب (۸ اعتبار)", "تحلیل کن — ساده با وب (۱۲ اعتبار)",
             "تحلیل کن — عمیق بدون وب (۱۶ اعتبار)", "تحلیل کن — عمیق با وب (۲۵ اعتبار)", "انصراف"],
            actions.Select(action => action.Text));
        Assert.Equal($"ga1:{OfferId:N}:s0", actions[0].CallbackData);
        Assert.Equal($"ga1:{OfferId:N}:d1", actions[3].CallbackData);
        Assert.Equal($"ga1:{OfferId:N}:x", actions[4].CallbackData);
        Assert.All(actions, action => Assert.True(Encoding.UTF8.GetByteCount(action.CallbackData) <= 64));
    }

    [Fact]
    public void Offer_OnAShortBalance_KeepsOnlyCheaperTiers_AndSaysHowToBuy()
    {
        var (note, actions) = TelegramGroundedAnalysis.Offer(Hint(sufficient: false));

        Assert.Equal(["s0", "s1", "d0", "x"], actions.Select(action => action.CallbackData.Split(':')[^1]));
        Assert.Equal("موجودی برای تحلیل عمیق با وب کافی نیست. برای خرید اعتبار /plans را بفرستید.", note);
    }

    [Fact]
    public void Offer_ForMax_ShowsEveryTierWithoutCredits()
    {
        var (note, actions) = TelegramGroundedAnalysis.Offer(Hint(unlimited: true, sufficient: true));

        Assert.Equal("تحلیل کن — عمیق با وب", actions[3].Text);
        Assert.Equal(5, actions.Count);
        Assert.Equal("با اشتراک Max اعتباری کسر نمی‌شود.", note);
    }

    [Fact]
    public void NoOffer_NoButtons() => Assert.Empty(TelegramGroundedAnalysis.Offer(Hint()).Actions);

    // ── Renderer ──────────────────────────────────────────────────────────────────────

    private static readonly TelegramAssistantResponseRenderer Renderer = new(
        new TelegramMonthlyTrendChartRenderer(), NullLogger<TelegramAssistantResponseRenderer>.Instance);

    private static AiQueryResponse Response(GroundedAnalysisHint? hint, string text = "جدول گزارش") =>
        new AiQueryResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.SymbolLookup, null, null, null,
            null, null, text, false, null, new UsageAccountingResult("AiQuery.StockAnalysis", "Completed", 1, 9, "v1", false))
        {
            GroundedAnalysis = hint
        };

    [Fact]
    public void Renderer_PutsThePriceLineUnderADiscussableReport_WithoutButtons()
    {
        var message = Assert.Single(Renderer.Render(Response(Hint()), "fa-IR"));

        Assert.EndsWith("\n\nپرسش تحلیلی درباره‌ی این گزارش: عمیق با وب، ۲۵ اعتبار", message.Text, StringComparison.Ordinal);
        Assert.Null(message.Actions);
    }

    [Fact]
    public void Renderer_PutsTheOfferButtonsOnTheLastPart()
    {
        var message = Assert.Single(Renderer.Render(Response(Hint(sufficient: false), "پیشنهاد تحلیل"), "fa-IR"));

        Assert.EndsWith("برای خرید اعتبار /plans را بفرستید\\.", message.Text, StringComparison.Ordinal);
        Assert.Equal(4, message.Actions!.Count);
    }

    [Fact]
    public void Renderer_WithoutAHint_IsUnchanged()
    {
        var plain = Renderer.Render(Response(null), "fa-IR");

        Assert.DoesNotContain("پرسش تحلیلی", Assert.Single(plain).Text, StringComparison.Ordinal);
        Assert.Null(plain[0].Actions);
    }

    [Fact]
    public void Renderer_KeepsEveryPartUnderTheTelegramLimit_WithTheLineOnTheLastPart()
    {
        var messages = Renderer.Render(Response(Hint(), string.Join('\n', Enumerable.Repeat(new string('ا', 120), 70))), "fa-IR");

        Assert.True(messages.Count > 1);
        Assert.All(messages, message => Assert.True(message.Text.Length <= 4096));
        Assert.EndsWith("۲۵ اعتبار", messages[^1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("پرسش تحلیلی", messages[0].Text, StringComparison.Ordinal);
    }
}
