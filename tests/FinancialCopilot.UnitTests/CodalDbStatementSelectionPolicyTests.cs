using FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

namespace FinancialCopilot.UnitTests;

public sealed class CodalDbStatementSelectionPolicyTests
{
    private static readonly DateTimeOffset FyEnd = new(2025, 3, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2024, 6, 22, 0, 0, 0, TimeSpan.Zero);

    private static CodalStatementRow Row(
        long stmtId,
        bool? isAudited = null,
        bool? isRepresented = null,
        bool? isComposing = null,
        byte periodType = 3) =>
        new(stmtId, stmtId, 1001, periodType, FyEnd, null, PeriodEnd, null,
            DateTimeOffset.UtcNow, isAudited, isRepresented, isComposing, null,
            [], []);

    [Fact]
    public void SelectAll_AuditedBeatsUnaudited()
    {
        var rows = new[] { Row(10, isAudited: false), Row(20, isAudited: true) };
        var selected = CodalDbStatementSelectionPolicy.SelectAll(rows);
        Assert.Single(selected);
        Assert.Equal(20L, selected[0].StmtId);
    }

    [Fact]
    public void SelectAll_LatestRepresentmentChosen_WhenBothAudited()
    {
        var rows = new[]
        {
            Row(10, isAudited: true, isRepresented: false),
            Row(20, isAudited: true, isRepresented: true)
        };
        var selected = CodalDbStatementSelectionPolicy.SelectAll(rows);
        Assert.Equal(20L, selected[0].StmtId);
    }

    [Fact]
    public void SelectAll_ConsolidatedPreferredByDefault()
    {
        var rows = new[]
        {
            Row(10, isAudited: true, isRepresented: true, isComposing: false),
            Row(20, isAudited: true, isRepresented: true, isComposing: true)
        };
        var selected = CodalDbStatementSelectionPolicy.SelectAll(rows, preferConsolidated: true);
        Assert.Equal(20L, selected[0].StmtId);
    }

    [Fact]
    public void SelectAll_ParentPreferredWhenConfigured()
    {
        var rows = new[]
        {
            Row(10, isAudited: true, isRepresented: true, isComposing: false),
            Row(20, isAudited: true, isRepresented: true, isComposing: true)
        };
        var selected = CodalDbStatementSelectionPolicy.SelectAll(rows, preferConsolidated: false);
        Assert.Equal(10L, selected[0].StmtId);
    }

    [Fact]
    public void SelectAll_SingleVariant_AlwaysSelected()
    {
        var rows = new[] { Row(42, isAudited: false) };
        var selected = CodalDbStatementSelectionPolicy.SelectAll(rows);
        Assert.Single(selected);
        Assert.Equal(42L, selected[0].StmtId);
    }

    [Fact]
    public void SelectAll_MultipleDistinctPeriods_ProduceMultipleSelections()
    {
        var periodEndQ1 = new DateTimeOffset(2024, 6, 22, 0, 0, 0, TimeSpan.Zero);
        var periodEndQ2 = new DateTimeOffset(2024, 9, 21, 0, 0, 0, TimeSpan.Zero);

        var rows = new[]
        {
            new CodalStatementRow(1, 1, 1001, 3, FyEnd, null, periodEndQ1, null,
                DateTimeOffset.UtcNow, true, true, true, null, [], []),
            new CodalStatementRow(2, 2, 1001, 6, FyEnd, null, periodEndQ2, null,
                DateTimeOffset.UtcNow, true, true, true, null, [], [])
        };

        var selected = CodalDbStatementSelectionPolicy.SelectAll(rows);
        Assert.Equal(2, selected.Count);
    }
}
