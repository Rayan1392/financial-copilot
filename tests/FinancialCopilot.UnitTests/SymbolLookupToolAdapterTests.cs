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
