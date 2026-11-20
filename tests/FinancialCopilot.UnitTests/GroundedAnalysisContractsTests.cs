using System.Security.Cryptography;
using System.Text.Json;
using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using FinancialCopilot.Application.Conversations;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «sol-deep-analysis-web-search» T05: old payloads still deserialise, a request without analysis options keeps
/// its Version 1 billing fingerprint, the options change the fingerprint, and offer action ids parse strictly.
/// </summary>
public sealed class GroundedAnalysisContractsTests
{
    private static AiQueryRequest Request(AnalysisOptionsRequest? options = null) => new(
        "سود خالص قمرو چرا کم شد؟",
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "corr-1",
        ConversationId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
        SuggestedActionId: "action-1",
        AnalysisOptions: options);

    [Fact]
    public void Fingerprint_WithoutOptions_IsTheVersion1HashByteForByte()
    {
        var request = Request();
        // The Version 1 payload as it was serialised before this change (same member names and order).
        var version1 = new
        {
            Version = 1,
            Message = request.Message,
            ConversationId = request.ConversationId,
            ScannerPage = 1,
            ScannerPageSize = 20,
            DisclosurePage = 1,
            DisclosurePageSize = 20,
            InsightEventId = (Guid?)null,
            AlertId = (Guid?)null,
            MonthlyActivityTrendReportYear = (int?)null,
            MonthlyActivityTrendReportMonth = (int?)null,
            SuggestedActionId = request.SuggestedActionId
        };
        var expected = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(version1)));

        Assert.Equal(expected, AiQueryRequestFingerprint.Compute(request));
    }

    [Fact]
    public void Fingerprint_ChangesWithTheAnalysisChoice_AndIsStableForTheSameChoice()
    {
        var none = AiQueryRequestFingerprint.Compute(Request());
        var deepWeb = AiQueryRequestFingerprint.Compute(Request(new AnalysisOptionsRequest(AnalysisDepth.Deep, true, "abc")));
        var deepWebAgain = AiQueryRequestFingerprint.Compute(Request(new AnalysisOptionsRequest(AnalysisDepth.Deep, true, "abc")));
        var simpleWeb = AiQueryRequestFingerprint.Compute(Request(new AnalysisOptionsRequest(AnalysisDepth.Simple, true, "abc")));
        var deepNoWeb = AiQueryRequestFingerprint.Compute(Request(new AnalysisOptionsRequest(AnalysisDepth.Deep, false, "abc")));

        Assert.Equal(deepWeb, deepWebAgain);
        Assert.Equal(4, new[] { none, deepWeb, simpleWeb, deepNoWeb }.Distinct().Count());
    }

    [Fact]
    public void AnalysisOptions_ResolveMissingMembersFromTheDefaults()
    {
        var defaults = new AnalysisChoice(AnalysisDepth.Deep, true);

        Assert.Equal(defaults, new AnalysisOptionsRequest().Resolve(defaults));
        Assert.Equal(new AnalysisChoice(AnalysisDepth.Simple, true), new AnalysisOptionsRequest(AnalysisDepth.Simple).Resolve(defaults));
        Assert.Equal(new AnalysisChoice(AnalysisDepth.Deep, false), new AnalysisOptionsRequest(WebSearch: false).Resolve(defaults));
    }

    [Fact]
    public void AnalysisOptionsRequest_HasNoAmount()
    {
        var names = typeof(AnalysisOptionsRequest).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(["Depth", "WebSearch", "PricingVersion"], names);
    }

    [Fact]
    public void OldPayloadJson_DeserializesWithoutTheNewMembers()
    {
        // An answered 163 payload written before this change: FallbackOf has no ConfirmedOfferId and the analysis
        // members do not exist.
        const string json = """
            {"Version":2,"Intent":15,"ClarificationRequired":false,"ClarificationMessage":null,"TextAnswer":"متن",
             "ScannerPlan":null,"ScannerTable":null,"SymbolLookupTable":null,"ExplainableAnswer":null,
             "ConfidenceScore":null,"Usage":null,"MemoryDisclosures":null,
             "FallbackOf":{"Outcome":2,"ReasonCode":"no_data"}}
            """;

        var payload = JsonSerializer.Deserialize<AssistantMessagePayload>(json)!;

        Assert.Equal("متن", payload.TextAnswer);
        Assert.Equal("no_data", payload.FallbackOf!.ReasonCode);
        Assert.Null(payload.FallbackOf.ConfirmedOfferId);
        Assert.Null(payload.GroundedAnalysisOffer);
        Assert.Null(payload.GroundedAnalysisRun);
        Assert.Null(payload.ExternalSources);
    }

    [Fact]
    public void NewPayloadMembers_RoundTrip()
    {
        var offerId = Guid.NewGuid();
        var payload = new AssistantMessagePayload(2, DetectedIntent.Unknown, false, null, "متن", null, null, null, null, null, null, null,
            FallbackOf: new FallbackOrigin(DialogueOutcome.Answered, "no_data", offerId),
            GroundedAnalysisOffer: new GroundedAnalysisOffer(offerId, "IRO1GMRO0001", "قمرو", "1404-Q2", "abcdef123456", DateTimeOffset.Parse("2026-11-20T10:00:00Z")),
            GroundedAnalysisRun: new GroundedAnalysisRun("Deep", true, "DeepWeb", "gpt-6.2-sol"),
            ExternalSources: [new ExternalSource("W1", "کدال", "https://codal.ir/x", "2026-11-01")]);

        var restored = JsonSerializer.Deserialize<AssistantMessagePayload>(JsonSerializer.Serialize(payload))!;

        Assert.Equal(offerId, restored.FallbackOf!.ConfirmedOfferId);
        Assert.Equal(payload.GroundedAnalysisOffer, restored.GroundedAnalysisOffer);
        Assert.Equal(payload.GroundedAnalysisRun, restored.GroundedAnalysisRun);
        Assert.Equal(payload.ExternalSources!.Single(), restored.ExternalSources!.Single());
    }

    [Fact]
    public void OfferActions_RoundTrip_AndFitTelegramCallbackData()
    {
        var offerId = Guid.NewGuid();

        Assert.True(GroundedAnalysisOfferActions.TryParse(GroundedAnalysisOfferActions.Confirm(offerId), out var confirmed, out var isConfirm));
        Assert.Equal((offerId, true), (confirmed, isConfirm));
        Assert.True(GroundedAnalysisOfferActions.TryParse(GroundedAnalysisOfferActions.Cancel(offerId), out var cancelled, out var isConfirmAgain));
        Assert.Equal((offerId, false), (cancelled, isConfirmAgain));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(GroundedAnalysisOfferActions.Confirm(offerId)) <= 64);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ga1:")]
    [InlineData("ga1:not-a-guid:confirm")]
    [InlineData("ga1:00000000000000000000000000000000:confirm")]
    [InlineData("ga1:0f8fad5bd9cb469fa16570867728950e:approve")]
    [InlineData("fs:0f8fad5bd9cb469fa16570867728950e:confirm")]
    public void OfferActions_RejectAnythingElse(string? actionId)
    {
        Assert.False(GroundedAnalysisOfferActions.TryParse(actionId, out _, out _));
    }
}
