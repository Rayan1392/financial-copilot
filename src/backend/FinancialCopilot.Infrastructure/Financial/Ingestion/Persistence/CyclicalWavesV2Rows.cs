using System.ComponentModel.DataAnnotations.Schema;
using System.Numerics;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

// Feature 148: independent CyclicalWaves v2 persistence in schema "cyclicalwaves".
// These rows reference only each other; nothing here points at a NoavaranAmin or legacy table.

public sealed class CyclicalWavesV2TickerRow
{
    public Guid Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string NormalizedTicker { get; set; } = string.Empty;
    public string? Enticker { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset LastSeenInCatalogAt { get; set; }
}

public sealed class CyclicalWavesV2MetricDefinitionRow
{
    public short Id { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string NameFa { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public short SortOrder { get; set; }
}

public sealed class CyclicalWavesV2FinancialValueRow
{
    public Guid TickerId { get; set; }
    public short MetricDefinitionId { get; set; }
    public decimal? NumericValue { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CyclicalWavesV2ScreenerMetadataRow
{
    public Guid TickerId { get; set; }
    public DateOnly? LastMonthSaleDate { get; set; }
    public DateOnly? LastQuarterDate { get; set; }
    public DateTimeOffset? ProviderUpdatedAt { get; set; }
    public DateTimeOffset LastChangedAt { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}

public sealed class CyclicalWavesV2IncomeStatementLineDefinitionRow
{
    public int Id { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameFa { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CyclicalWavesV2IncomeStatementRow
{
    public Guid Id { get; set; }
    public Guid TickerId { get; set; }
    public DateOnly PeriodEndingDate { get; set; }
    public int FiscalYear { get; set; }
    public int FiscalMonth { get; set; }
    public bool? Audited { get; set; }
    public DateTimeOffset? PublishDate { get; set; }
    public string? CodalUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CyclicalWavesV2IncomeStatementValueRow
{
    public Guid StatementId { get; set; }
    public int LineDefinitionId { get; set; }
    public int SortOrder { get; set; }
    public decimal? Quarter { get; set; }
    public decimal? YearToDate { get; set; }
    public decimal? TrailingYear { get; set; }
}

public sealed class CyclicalWavesV2AnalysisRow
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? PlainTextSummary { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset FirstSyncedAt { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CyclicalWavesV2AnalysisCategoryRow
{
    public long AnalysisId { get; set; }
    public long ProviderCategoryId { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed class CyclicalWavesV2AnalysisTagRow
{
    public long AnalysisId { get; set; }
    public long ProviderTagId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
}

public sealed class CyclicalWavesV2SyncRunRow
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string? TickerFilter { get; set; }
    public string? AnalysisMode { get; set; }
    public string TriggerSource { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int UpdatedItems { get; set; }
    public int UnchangedItems { get; set; }
    public int FailedItems { get; set; }
    public string? ErrorSummary { get; set; }
    public string? LockOwner { get; set; }
    public DateTimeOffset? LockLeaseExpiresAt { get; set; }
    public string? RequestedBy { get; set; }
}

public sealed class CyclicalWavesV2GaugeAvailabilityRow
{
    public Guid TickerId { get; set; }
    public bool HasPe { get; set; }
    public bool HasPs { get; set; }
    public DateTimeOffset LastFetchedAtUtc { get; set; }
}

public sealed class CyclicalWavesV2GaugeDetailRow
{
    public Guid TickerId { get; set; }
    public string GaugeType { get; set; } = string.Empty;
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
    public int D { get; set; }
    public int E { get; set; }
    public int F { get; set; }
    public BigInteger? CloseHigh { get; set; }
    public BigInteger? CloseMiddle { get; set; }
    public BigInteger? CloseLow { get; set; }
    public short? CloseSign { get; set; }
    public short? CloseScale { get; set; }
    public BigInteger StartHigh { get; set; }
    public BigInteger StartMiddle { get; set; }
    public BigInteger StartLow { get; set; }
    public short StartSign { get; set; }
    public short StartScale { get; set; }
    public BigInteger EndHigh { get; set; }
    public BigInteger EndMiddle { get; set; }
    public BigInteger EndLow { get; set; }
    public short EndSign { get; set; }
    public short EndScale { get; set; }
    public BigInteger MinHigh { get; set; }
    public BigInteger MinMiddle { get; set; }
    public BigInteger MinLow { get; set; }
    public short MinSign { get; set; }
    public short MinScale { get; set; }
    public BigInteger AvgHigh { get; set; }
    public BigInteger AvgMiddle { get; set; }
    public BigInteger AvgLow { get; set; }
    public short AvgSign { get; set; }
    public short AvgScale { get; set; }
    public BigInteger MaxHigh { get; set; }
    public BigInteger MaxMiddle { get; set; }
    public BigInteger MaxLow { get; set; }
    public short MaxSign { get; set; }
    public short MaxScale { get; set; }
    [NotMapped] public ExactDecimal? Close { get => ExactDecimalValueConverter.Restore(CloseHigh, CloseMiddle, CloseLow, CloseSign, CloseScale); set => Set(value, (h, m, l, s, sc) => (CloseHigh, CloseMiddle, CloseLow, CloseSign, CloseScale) = (h, m, l, s, sc)); }
    [NotMapped] public ExactDecimal Start { get => ExactDecimalValueConverter.Restore(StartHigh, StartMiddle, StartLow, StartSign, StartScale)!.Value; set => Set(value, (h, m, l, s, sc) => (StartHigh, StartMiddle, StartLow, StartSign, StartScale) = (h!.Value, m!.Value, l!.Value, s!.Value, sc!.Value)); }
    [NotMapped] public ExactDecimal End { get => ExactDecimalValueConverter.Restore(EndHigh, EndMiddle, EndLow, EndSign, EndScale)!.Value; set => Set(value, (h, m, l, s, sc) => (EndHigh, EndMiddle, EndLow, EndSign, EndScale) = (h!.Value, m!.Value, l!.Value, s!.Value, sc!.Value)); }
    [NotMapped] public ExactDecimal Min { get => ExactDecimalValueConverter.Restore(MinHigh, MinMiddle, MinLow, MinSign, MinScale)!.Value; set => Set(value, (h, m, l, s, sc) => (MinHigh, MinMiddle, MinLow, MinSign, MinScale) = (h!.Value, m!.Value, l!.Value, s!.Value, sc!.Value)); }
    [NotMapped] public ExactDecimal Avg { get => ExactDecimalValueConverter.Restore(AvgHigh, AvgMiddle, AvgLow, AvgSign, AvgScale)!.Value; set => Set(value, (h, m, l, s, sc) => (AvgHigh, AvgMiddle, AvgLow, AvgSign, AvgScale) = (h!.Value, m!.Value, l!.Value, s!.Value, sc!.Value)); }
    [NotMapped] public ExactDecimal Max { get => ExactDecimalValueConverter.Restore(MaxHigh, MaxMiddle, MaxLow, MaxSign, MaxScale)!.Value; set => Set(value, (h, m, l, s, sc) => (MaxHigh, MaxMiddle, MaxLow, MaxSign, MaxScale) = (h!.Value, m!.Value, l!.Value, s!.Value, sc!.Value)); }
    public int? Status { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }

    private static void Set(ExactDecimal? value, Action<BigInteger?, BigInteger?, BigInteger?, short?, short?> assign)
    {
        if (value is null) { assign(null, null, null, null, null); return; }
        var parts = ExactDecimalValueConverter.Split(value.Value);
        assign(parts.High, parts.Middle, parts.Low, parts.Sign, parts.Scale);
    }
}

public sealed class CyclicalWavesV2EquilibriumRangeRow
{
    public Guid TickerId { get; set; }
    public bool HasRange { get; set; }
    public BigInteger? MinHigh { get; set; }
    public BigInteger? MinMiddle { get; set; }
    public BigInteger? MinLow { get; set; }
    public short? MinSign { get; set; }
    public short? MinScale { get; set; }
    public BigInteger? AvgHigh { get; set; }
    public BigInteger? AvgMiddle { get; set; }
    public BigInteger? AvgLow { get; set; }
    public short? AvgSign { get; set; }
    public short? AvgScale { get; set; }
    public BigInteger? MaxHigh { get; set; }
    public BigInteger? MaxMiddle { get; set; }
    public BigInteger? MaxLow { get; set; }
    public short? MaxSign { get; set; }
    public short? MaxScale { get; set; }
    [NotMapped] public ExactDecimal? Min { get => ExactDecimalValueConverter.Restore(MinHigh, MinMiddle, MinLow, MinSign, MinScale); set => Set(value, (h, m, l, s, sc) => (MinHigh, MinMiddle, MinLow, MinSign, MinScale) = (h, m, l, s, sc)); }
    [NotMapped] public ExactDecimal? Avg { get => ExactDecimalValueConverter.Restore(AvgHigh, AvgMiddle, AvgLow, AvgSign, AvgScale); set => Set(value, (h, m, l, s, sc) => (AvgHigh, AvgMiddle, AvgLow, AvgSign, AvgScale) = (h, m, l, s, sc)); }
    [NotMapped] public ExactDecimal? Max { get => ExactDecimalValueConverter.Restore(MaxHigh, MaxMiddle, MaxLow, MaxSign, MaxScale); set => Set(value, (h, m, l, s, sc) => (MaxHigh, MaxMiddle, MaxLow, MaxSign, MaxScale) = (h, m, l, s, sc)); }
    public DateTimeOffset FetchedAtUtc { get; set; }

    private static void Set(ExactDecimal? value, Action<BigInteger?, BigInteger?, BigInteger?, short?, short?> assign)
    {
        if (value is null) { assign(null, null, null, null, null); return; }
        var parts = ExactDecimalValueConverter.Split(value.Value);
        assign(parts.High, parts.Middle, parts.Low, parts.Sign, parts.Scale);
    }
}
