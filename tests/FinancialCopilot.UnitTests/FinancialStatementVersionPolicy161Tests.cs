using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class FinancialStatementVersionPolicy161Tests
{
    private sealed record Version(string ExternalId, Guid Id, FinancialStatementVersionRank Rank, string Signature);

    [Fact]
    public void Rank_PrefersAuditThenRestatementThenPublication_AndReturnsOnlyTopPeers()
    {
        var earlier = new DateOnly(2026, 1, 1);
        var later = new DateOnly(2026, 2, 1);
        Version[] versions =
        [
            new("1", Guid.NewGuid(), new(false, true, later), "a"),
            new("2", Guid.NewGuid(), new(true, false, later), "a"),
            new("3", Guid.NewGuid(), new(true, true, null), "a"),
            new("4", Guid.NewGuid(), new(true, true, earlier), "a"),
            new("5", Guid.NewGuid(), new(true, true, later), "a"),
            new("6", Guid.NewGuid(), new(true, true, later), "a")
        ];
        var ranked = FinancialStatementVersionPolicy.Rank(versions, x => x.Rank);
        Assert.Equal(["5", "6", "4", "3", "2", "1"], ranked.Select(x => x.ExternalId));
        Assert.Equal(["5", "6"], FinancialStatementVersionPolicy.TopTieGroup(ranked, x => x.Rank).Select(x => x.ExternalId));
        Assert.Empty(FinancialStatementVersionPolicy.TopTieGroup(Array.Empty<Version>(), x => x.Rank));
    }

    [Fact]
    public void Pick_RequiresEqualSignaturesBeforeStableNumericOrGuidTieBreak()
    {
        var rank = new FinancialStatementVersionRank(true, false, null);
        var low = new Version("9", Guid.Parse("00000000-0000-0000-0000-000000000001"), rank, "1=42");
        var high = new Version("10", Guid.Parse("00000000-0000-0000-0000-000000000002"), rank, "1=42");
        var sameExternalHigherId = high with { Id = Guid.Parse("00000000-0000-0000-0000-000000000003") };
        FinancialStatementVersionPick<Version> Pick(params Version[] versions) =>
            FinancialStatementVersionPolicy.Pick(versions, x => x.Signature, x => x.ExternalId, x => x.Id);

        Assert.Null(Pick().Winner);
        Assert.False(Pick().IsConflict);
        Assert.Equal(sameExternalHigherId, Pick(low, high, sameExternalHigherId).Winner);
        var conflict = Pick(low, high with { Signature = "1=43" });
        Assert.True(conflict.IsConflict);
        Assert.Null(conflict.Winner);
        Assert.Equal("b", Pick(low with { ExternalId = "a" }, high with { ExternalId = "b" }).Winner!.ExternalId);
    }
}
