using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

namespace FinancialCopilot.UnitTests;

public sealed class SymbolLookupToolAdapterTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task LookupAsync_EntityOnlyFollowup_RebuildsParserInputWithoutConversationMarkers()
    {
        var parser = new RecordingSymbolLookupParser(new SymbolLookupParseResult(
            [new SymbolLookupParsedPair("چادرملو", new MetricCode("MONTHLY_SALES_YTD"), "فروش YTD")],
            LookupParseStatus.Parsed));
        var lookup = new RecordingSymbolMetricLookupService();
        var adapter = new SymbolLookupToolAdapter(parser, lookup, TimeProvider.System);

        const string enrichedContext = """
            [Recent conversation]
            User: فروش YTD چقدر بوده؟
            Assistant: Found metric data for 1 symbol(s).
            ---
            چادرملو
            """;

        await adapter.LookupAsync(
            "چادرملو",
            "corr-followup-sanitize",
            TenantId,
            ActorId,
            CancellationToken.None,
            queryTextForLookup: enrichedContext);

        Assert.Equal("فروش YTD چقدر بوده؟ چادرملو", parser.LastMessage);
        Assert.DoesNotContain("[Recent conversation]", parser.LastMessage);
        Assert.DoesNotContain("Assistant", parser.LastMessage);

        Assert.NotNull(lookup.LastRequest);
        var request = lookup.LastRequest!;
        var pair = Assert.Single(request.Pairs);
        Assert.Equal("چادرملو", pair.SymbolName);
        Assert.Equal("MONTHLY_SALES_YTD", pair.MetricCode.Value);
    }

    [Fact]
    public async Task LookupAsync_PollutedParserEntity_SanitizesBeforeLookupRequest()
    {
        var parser = new RecordingSymbolLookupParser(new SymbolLookupParseResult(
            [new SymbolLookupParsedPair(
                """
                [Recent conversation]
                User: pe چقدر است؟
                Assistant: some table text
                ---
                کچاد
                """,
                new MetricCode("PE_TTM"),
                "pe")],
            LookupParseStatus.Parsed));
        var lookup = new RecordingSymbolMetricLookupService();
        var adapter = new SymbolLookupToolAdapter(parser, lookup, TimeProvider.System);

        await adapter.LookupAsync(
            "pe کچاد",
            "corr-polluted-entity",
            TenantId,
            ActorId,
            CancellationToken.None);

        Assert.NotNull(lookup.LastRequest);
        var request = lookup.LastRequest!;
        var pair = Assert.Single(request.Pairs);
        Assert.Equal("کچاد", pair.SymbolName);
        Assert.Equal("PE_TTM", pair.MetricCode.Value);
    }

    // ── Rule 1: explicit symbol in current message wins ──────────────────────────────────────────
    // Second query names كچاد explicitly; شگل from prior turn must not appear in parser input.
    [Fact]
    public async Task LookupAsync_ExplicitSymbolInCurrentMessage_DoesNotMergeWithPriorSymbol()
    {
        var parser = new RecordingSymbolLookupParser(new SymbolLookupParseResult(
            [new SymbolLookupParsedPair("کچاد", new MetricCode("LATEST_PRICE"), "آخرین قیمت")],
            LookupParseStatus.Parsed));
        var lookup = new RecordingSymbolMetricLookupService();
        var adapter = new SymbolLookupToolAdapter(parser, lookup, TimeProvider.System);

        const string enrichedContext = """
            [Recent conversation]
            User: آخرین قیمت شگل
            Assistant: Found metric data for 1 symbol(s).
            ---
            آخرین قیمت کچاد
            """;

        await adapter.LookupAsync(
            "آخرین قیمت کچاد",
            "corr-rule1",
            TenantId,
            ActorId,
            CancellationToken.None,
            queryTextForLookup: enrichedContext);

        // Parser must receive only the current message — no شگل bleed-through
        Assert.Equal("آخرین قیمت کچاد", parser.LastMessage);
        Assert.DoesNotContain("شگل", parser.LastMessage);

        Assert.NotNull(lookup.LastRequest);
        var pair = Assert.Single(lookup.LastRequest!.Pairs);
        Assert.Equal("کچاد", pair.SymbolName);
        Assert.Equal("LATEST_PRICE", pair.MetricCode.Value);
    }

    // ── Rule 2: implicit follow-up uses prior symbol as fallback ─────────────────────────────────
    // "pe چقدره؟" has no entity → prior turn شگل must be merged in for the parser.
    [Fact]
    public async Task LookupAsync_ImplicitFollowup_UsesConversationContextSymbol()
    {
        var parser = new RecordingSymbolLookupParser(new SymbolLookupParseResult(
            [new SymbolLookupParsedPair("شگل", new MetricCode("PE_TTM"), "pe")],
            LookupParseStatus.Parsed));
        var lookup = new RecordingSymbolMetricLookupService();
        var adapter = new SymbolLookupToolAdapter(parser, lookup, TimeProvider.System);

        const string enrichedContext = """
            [Recent conversation]
            User: آخرین قیمت شگل
            Assistant: Found metric data for 1 symbol(s).
            ---
            pe چقدره؟
            """;

        await adapter.LookupAsync(
            "pe چقدره؟",
            "corr-rule2",
            TenantId,
            ActorId,
            CancellationToken.None,
            queryTextForLookup: enrichedContext);

        // Parser must receive the merged string containing prior symbol so it can resolve شگل
        Assert.Contains("شگل", parser.LastMessage);
        Assert.Contains("pe", parser.LastMessage, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(lookup.LastRequest);
        var pair = Assert.Single(lookup.LastRequest!.Pairs);
        Assert.Equal("شگل", pair.SymbolName);
        Assert.Equal("PE_TTM", pair.MetricCode.Value);
    }

    // ── Rule 4: new explicit symbol replaces active subject ──────────────────────────────────────
    // Third query "pe چقدره؟" must resolve to کچاد (the latest explicit subject), not شگل.
    [Fact]
    public async Task LookupAsync_ThirdQueryImplicit_ResolvesToLatestExplicitSubject()
    {
        var parser = new RecordingSymbolLookupParser(new SymbolLookupParseResult(
            [new SymbolLookupParsedPair("کچاد", new MetricCode("PE_TTM"), "pe")],
            LookupParseStatus.Parsed));
        var lookup = new RecordingSymbolMetricLookupService();
        var adapter = new SymbolLookupToolAdapter(parser, lookup, TimeProvider.System);

        // enrichedMessage contains both turns; the most-recent User turn is كچاد
        const string enrichedContext = """
            [Recent conversation]
            User: آخرین قیمت شگل
            Assistant: Found metric data for 1 symbol(s).
            User: آخرین قیمت کچاد
            Assistant: Found metric data for 1 symbol(s).
            ---
            pe چقدره؟
            """;

        await adapter.LookupAsync(
            "pe چقدره؟",
            "corr-rule4",
            TenantId,
            ActorId,
            CancellationToken.None,
            queryTextForLookup: enrichedContext);

        // Merged parser input must contain the most-recent prior turn (کچاد), not شگل
        Assert.Contains("کچاد", parser.LastMessage);
        Assert.DoesNotContain("شگل", parser.LastMessage);

        Assert.NotNull(lookup.LastRequest);
        var pair = Assert.Single(lookup.LastRequest!.Pairs);
        Assert.Equal("کچاد", pair.SymbolName);
        Assert.Equal("PE_TTM", pair.MetricCode.Value);
    }

    // ── Rule 5: no rolling window — multiple explicit symbols stay exact ─────────────────────────
    // Query names both کگل and کچاد; exactly those two must reach the parser, nothing from history.
    [Fact]
    public async Task LookupAsync_MultipleExplicitSymbols_NoHistorySymbolsInjected()
    {
        var parser = new RecordingSymbolLookupParser(new SymbolLookupParseResult(
            [
                new SymbolLookupParsedPair("کگل", new MetricCode("LATEST_PRICE"), "آخرین قیمت"),
                new SymbolLookupParsedPair("کچاد", new MetricCode("LATEST_PRICE"), "آخرین قیمت"),
            ],
            LookupParseStatus.Parsed));
        var lookup = new RecordingSymbolMetricLookupService();
        var adapter = new SymbolLookupToolAdapter(parser, lookup, TimeProvider.System);

        const string enrichedContext = """
            [Recent conversation]
            User: آخرین قیمت شگل
            Assistant: Found metric data for 1 symbol(s).
            ---
            آخرین قیمت کگل و کچاد
            """;

        await adapter.LookupAsync(
            "آخرین قیمت کگل و کچاد",
            "corr-rule5",
            TenantId,
            ActorId,
            CancellationToken.None,
            queryTextForLookup: enrichedContext);

        Assert.Equal("آخرین قیمت کگل و کچاد", parser.LastMessage);
        Assert.DoesNotContain("شگل", parser.LastMessage);

        Assert.NotNull(lookup.LastRequest);
        Assert.Equal(2, lookup.LastRequest!.Pairs.Count);
        Assert.Contains(lookup.LastRequest.Pairs, p => p.SymbolName == "کگل");
        Assert.Contains(lookup.LastRequest.Pairs, p => p.SymbolName == "کچاد");
    }

    private sealed class RecordingSymbolLookupParser(SymbolLookupParseResult result) : ISymbolLookupParser
    {
        public string LastMessage { get; private set; } = string.Empty;

        public Task<SymbolLookupParseResult> ParseAsync(
            SymbolLookupParseRequest request,
            CancellationToken cancellationToken)
        {
            LastMessage = request.Message;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingSymbolMetricLookupService : ISymbolMetricLookupService
    {
        public SymbolLookupRequest? LastRequest { get; private set; }

        public Task<SymbolLookupTableResult> LookupAsync(
            SymbolLookupRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new SymbolLookupTableResult(
                Guid.NewGuid(),
                [],
                [],
                new ScannerExecutionFacts(
                    DateTimeOffset.UtcNow,
                    TimeSpan.Zero,
                    TotalSymbolsEvaluated: request.Pairs.Count,
                    MatchingSymbolCount: 0,
                    FromCache: false,
                    Page: 1,
                    PageSize: 1,
                    TotalPages: 1),
                [],
                []));
        }
    }
}
