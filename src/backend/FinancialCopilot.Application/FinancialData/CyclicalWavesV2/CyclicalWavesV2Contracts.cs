namespace FinancialCopilot.Application.FinancialData.CyclicalWavesV2;

// Provider-neutral records for the CyclicalWaves v2 bounded context. Infrastructure maps the
// provider DTOs into these; no transport type crosses this boundary.

public sealed record CyclicalWavesV2ScreenerSnapshot(
    string Ticker,
    string? Enticker,
    IReadOnlyDictionary<string, decimal?> Values,
    DateOnly? LastMonthSaleDate,
    DateOnly? LastQuarterDate,
    DateTimeOffset? ProviderUpdatedAt);

public sealed record CyclicalWavesV2StatementLine(
    int Key,
    string NameEn,
    string NameFa,
    decimal? Quarter,
    decimal? YearToDate,
    decimal? TrailingYear);

public sealed record CyclicalWavesV2IncomeStatement(
    DateOnly PeriodEndingDate,
    int FiscalYear,
    int FiscalMonth,
    bool? Audited,
    DateTimeOffset? PublishDate,
    string? CodalUrl,
    IReadOnlyList<CyclicalWavesV2StatementLine> Lines);

public sealed record CyclicalWavesV2IncomeStatementSet(
    string Ticker,
    string? Enticker,
    int Total,
    IReadOnlyList<CyclicalWavesV2IncomeStatement> Statements);

public sealed record CyclicalWavesV2LineDefinition(int Key, string NameEn, string NameFa);

public sealed record CyclicalWavesV2AnalysisCategory(long Id, string Title);

public sealed record CyclicalWavesV2AnalysisTag(long Id, string Name);

public sealed record CyclicalWavesV2AnalysisArticle(
    long Id,
    string Title,
    string? Summary,
    DateTimeOffset PublishedAt,
    IReadOnlyList<CyclicalWavesV2AnalysisCategory> Categories,
    IReadOnlyList<CyclicalWavesV2AnalysisTag> Tags);

public sealed record CyclicalWavesV2AnalysisPage(
    IReadOnlyList<CyclicalWavesV2AnalysisArticle> Items,
    int CurrentPage,
    int LastPage,
    int Total);

/// <summary>Port to the seven documented CyclicalWaves v2 endpoints (authentication is internal to the adapter).</summary>
public interface ICyclicalWavesV2Client
{
    Task<IReadOnlyList<string>> GetTickersAsync(CancellationToken cancellationToken);

    /// <returns><c>null</c> when the provider answers 404 (unknown ticker).</returns>
    Task<CyclicalWavesV2ScreenerSnapshot?> GetScreenerAsync(string ticker, CancellationToken cancellationToken);

    /// <returns><c>null</c> when the provider answers 404 (no symbol with income statements).</returns>
    Task<CyclicalWavesV2IncomeStatementSet?> GetIncomeStatementsAsync(
        string ticker,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CyclicalWavesV2LineDefinition>> GetIncomeStatementLinesAsync(CancellationToken cancellationToken);

    Task<CyclicalWavesV2AnalysisPage> GetAnalysesPageAsync(
        int page,
        int pageSize,
        DateOnly? fromDate,
        CancellationToken cancellationToken);
}

public sealed record CyclicalWavesV2CatalogResult(int Added, int Deactivated, int Reactivated, int Unchanged);

public enum CyclicalWavesV2ApplyOutcome
{
    Updated,
    Unchanged,
    NotInCatalog
}

public sealed record CyclicalWavesV2ScreenerApplyResult(CyclicalWavesV2ApplyOutcome Outcome, bool EntickerConflict);

public sealed record CyclicalWavesV2StatementApplyResult(
    CyclicalWavesV2ApplyOutcome Outcome,
    int InsertedPeriods,
    int UpdatedPeriods,
    int DuplicatePeriodsInResponse);

public sealed record CyclicalWavesV2AnalysisApplyResult(int Inserted, int Updated, int Unchanged);

/// <summary>Latest-state and period persistence for the screener, the ticker catalog and income statements.</summary>
public interface ICyclicalWavesV2Store
{
    Task<CyclicalWavesV2CatalogResult> UpsertCatalogAsync(
        IReadOnlyCollection<string> tickers,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetActiveTickersAsync(CancellationToken cancellationToken);

    /// <returns>The stored ticker spelling when the catalog knows the (normalized) ticker.</returns>
    Task<string?> FindCatalogTickerAsync(string ticker, CancellationToken cancellationToken);

    Task<CyclicalWavesV2ScreenerApplyResult> ApplyScreenerAsync(
        CyclicalWavesV2ScreenerSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> UpsertLineDefinitionsAsync(
        IReadOnlyCollection<CyclicalWavesV2LineDefinition> definitions,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CyclicalWavesV2StatementApplyResult> ApplyIncomeStatementsAsync(
        CyclicalWavesV2IncomeStatementSet statements,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>Independent comprehensive-analysis article persistence. Articles are never deleted.</summary>
public interface ICyclicalWavesV2AnalysisStore
{
    Task<CyclicalWavesV2AnalysisApplyResult> ApplyAnalysesPageAsync(
        IReadOnlyCollection<CyclicalWavesV2AnalysisArticle> articles,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetMaxPublishedAtAsync(CancellationToken cancellationToken);
}
