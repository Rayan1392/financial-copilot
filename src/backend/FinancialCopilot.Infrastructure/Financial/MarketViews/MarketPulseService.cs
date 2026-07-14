using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FinancialCopilot.Infrastructure.Financial.MarketViews;

public sealed class MarketPulseService(
    FinancialIngestionDbContext dbContext,
    IBillableAccountResolver accountResolver,
    IPlanCapabilityService planCapabilities,
    IMarketQuoteSourcePriority sourcePriority,
    IOptions<MarketViewOptions> options,
    TimeProvider timeProvider,
    ILogger<MarketPulseService> logger) : IMarketPulseService, IMarketPulseSnapshotGenerator
{
    public const string CapabilityCode = "MarketPulse.Read";
    public const string DefinitionVersion = "v1";
    private const string Disclaimer = "Informational, evidence-based market statistics; not financial advice.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MarketViewOptions _options = options.Value;

    public async Task<MarketPulseSnapshot> GetLatestAsync(
        CurrentActor actor,
        string? segment,
        CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        return await CaptureAsync(segment, cancellationToken);
    }

    public async Task<MarketPulseHistoryPage> GetHistoryAsync(
        CurrentActor actor,
        MarketPulseHistoryQuery query,
        CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        var segment = NormalizeSegment(query.Segment);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? _options.PulseHistoryPageSize : query.PageSize, 1, 100);
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            throw new MarketPulseValidationException("The history from date must not be after the to date.");

        var rows = dbContext.MarketPulseSnapshots.AsNoTracking()
            .Where(row => row.IsCurrent && row.Segment == segment);
        if (query.From.HasValue) rows = rows.Where(row => row.TradingDate >= query.From.Value);
        if (query.To.HasValue) rows = rows.Where(row => row.TradingDate <= query.To.Value);
        if (query.SessionState.HasValue)
        {
            var state = query.SessionState.Value.ToString();
            rows = rows.Where(row => row.SessionState == state);
        }
        if (query.IsFinal.HasValue) rows = rows.Where(row => row.IsFinal == query.IsFinal.Value);

        var total = await rows.CountAsync(cancellationToken);
        var pageRows = await rows
            .OrderByDescending(row => row.TradingDate)
            .ThenByDescending(row => row.GeneratedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return new MarketPulseHistoryPage(pageRows.Select(Map).ToArray(), page, pageSize, total);
    }

    public async Task<MarketPulseSnapshot> CaptureAsync(string? segment, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedSegment = NormalizeSegment(segment);
        var now = timeProvider.GetUtcNow();
        var session = MarketPulseCalculator.ResolveSession(now, _options.PulseCadenceMinutes);
        var staleBefore = now.AddMinutes(-Math.Max(1, _options.StaleAfterMinutes));

        var instruments = dbContext.TradingInstruments.AsNoTracking()
            .Where(row => row.IsActive && row.ProviderName == sourcePriority.PrimarySourceName);
        if (!normalizedSegment.Equals("all", StringComparison.OrdinalIgnoreCase))
            instruments = instruments.Where(row => row.MarketCode == normalizedSegment);

        var scopedInstruments = await instruments
            .Select(row => new { row.Id, row.NormalizedCompanyId })
            .ToArrayAsync(cancellationToken);
        var instrumentIds = scopedInstruments.Select(row => row.Id).ToArray();
        var rawQuotes = instrumentIds.Length == 0
            ? []
            : await dbContext.LatestMarketQuotes.AsNoTracking()
                .Where(row => instrumentIds.Contains(row.TradingInstrumentId) && row.TradingDate == session.TradingDate)
                .Where(row => row.ProviderName == sourcePriority.PrimarySourceName)
                .Select(row => new
                {
                    row.TradingInstrumentId,
                    row.PriceChangePercentage,
                    row.AsOf,
                    row.ProviderName
                })
                .ToArrayAsync(cancellationToken);
        var quotes = rawQuotes
            .GroupBy(row => row.TradingInstrumentId)
            .Select(group => group.OrderByDescending(row => row.AsOf).First())
            .ToArray();

        var companyIds = scopedInstruments.Select(row => row.NormalizedCompanyId).OfType<Guid>().Distinct().ToArray();
        var companyIndustries = companyIds.Length == 0
            ? []
            : await (
                from company in dbContext.Companies.AsNoTracking()
                join industry in dbContext.Industries.AsNoTracking()
                    on company.IndustryId equals industry.Id into industryRows
                from industry in industryRows.DefaultIfEmpty()
                where companyIds.Contains(company.Id)
                select new { company.Id, IndustryCode = industry == null ? null : industry.ExternalId, IndustryName = industry == null ? null : industry.Name })
                .ToArrayAsync(cancellationToken);
        var industryByCompany = companyIndustries.ToDictionary(row => row.Id);
        var companyByInstrument = scopedInstruments.ToDictionary(row => row.Id, row => row.NormalizedCompanyId);
        var quoteInputs = quotes.Select(row =>
        {
            string? code = null;
            string? name = null;
            if (companyByInstrument.TryGetValue(row.TradingInstrumentId, out var companyId) &&
                companyId.HasValue && industryByCompany.TryGetValue(companyId.Value, out var industry))
            {
                code = industry.IndustryCode;
                name = industry.IndustryName;
            }
            return new MarketPulseCalculator.Quote(row.PriceChangePercentage, row.AsOf, code, name);
        }).ToArray();

        var breadth = MarketPulseCalculator.CalculateBreadth(
            quoteInputs, staleBefore, Math.Max(0, scopedInstruments.Length - quotes.Length));
        var industries = MarketPulseCalculator.CalculateIndustryDrivers(
            quoteInputs, staleBefore, _options.PulseIndustryDriverCount);
        var transaction = await CalculateTransactionValueAsync(
            instrumentIds, session, now, staleBefore, cancellationToken);
        var comparisons = await CalculateComparisonsAsync(
            instrumentIds, session.TradingDate, transaction.Value, cancellationToken);

        var facts = BuildFacts(transaction);
        var quoteWatermark = quotes.Select(row => (DateTimeOffset?)row.AsOf).Max();
        var sourceWatermark = new[] { quoteWatermark, transaction.Watermark }.OfType<DateTimeOffset>().DefaultIfEmpty().Max();
        DateTimeOffset? nullableWatermark = sourceWatermark == default ? null : sourceWatermark;
        var evidenceCutoff = nullableWatermark?.ToString("O") ??
                             $"{session.TradingDate:yyyy-MM-dd}/{session.CadenceSlot}";
        var evidence = new[]
        {
            new MarketPulseEvidence(
                "LatestMarketQuotes",
                JoinProviders(rawQuotes.Select(row => row.ProviderName)),
                quoteWatermark,
                breadth.IncludedInstruments,
                breadth.ExcludedInstruments,
                "percent",
                evidenceCutoff),
            new MarketPulseEvidence(
                transaction.Dataset,
                transaction.Provider,
                transaction.Watermark,
                transaction.Included,
                transaction.Excluded,
                "IRR",
                evidenceCutoff)
        };
        var isFinal = session.State == MarketPulseSessionState.Closed &&
                      transaction.Status == MarketPulseFactStatus.Available;
        var isPartial = !isFinal ||
                        breadth.Status is MarketPulseFactStatus.Unavailable or MarketPulseFactStatus.Stale;
        var inputHash = HashInput(normalizedSegment, session, facts, breadth, industries, comparisons, evidence, isFinal);

        MarketPulseSnapshotRow row;
        try
        {
            row = await PersistRevisionAsync(
                normalizedSegment, session, now, nullableWatermark, facts, breadth, industries,
                comparisons, evidence, transaction.Value, isPartial, isFinal, inputHash, cancellationToken);
        }
        catch (Exception exception) when (IsSlotConcurrencyConflict(exception))
        {
            logger.LogWarning(
                exception,
                "Concurrent market-pulse writer detected for {TradingDate}/{Segment}/{Slot}; retrying once.",
                session.TradingDate, normalizedSegment, session.CadenceSlot);
            dbContext.ChangeTracker.Clear();
            row = await PersistRevisionAsync(
                normalizedSegment, session, now, nullableWatermark, facts, breadth, industries,
                comparisons, evidence, transaction.Value, isPartial, isFinal, inputHash, cancellationToken);
        }
        logger.LogInformation(
            "Market pulse {TradingDate}/{Segment}/{Slot} revision={Revision} corrected={Corrected} partial={Partial} partialFacts={PartialFacts} final={Final} included={Included} excluded={Excluded} watermark={Watermark} durationMs={DurationMs}.",
            row.TradingDate, row.Segment, row.CadenceSlot, row.Revision, row.Revision > 1, row.IsPartial,
            facts.Count(fact => fact.Status != MarketPulseFactStatus.Available), row.IsFinal,
            breadth.IncludedInstruments, breadth.ExcludedInstruments, row.SourceWatermarkUtc,
            stopwatch.Elapsed.TotalMilliseconds);
        return Map(row);
    }

    private async Task AuthorizeAsync(CurrentActor actor, CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(actor.ActorId, actor.TenantId, actor.UserId, actor.ApiClientId, null),
            cancellationToken);
        try
        {
            await planCapabilities.ValidateCanExecuteAsync(account, CapabilityCode, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new MarketPulseAccessDeniedException(exception.Message);
        }
    }

    private string NormalizeSegment(string? segment)
    {
        var normalized = string.IsNullOrWhiteSpace(segment) ? "all" : segment.Trim();
        var allowed = _options.PulseSegments
            .Append("all")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var match = allowed.FirstOrDefault(value => value.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new MarketPulseValidationException(
                $"Unsupported market segment '{normalized}'. Allowed values: {string.Join(", ", allowed)}.");
        return match.ToLowerInvariant() == "all" ? "all" : match;
    }

    private async Task<TransactionValueResult> CalculateTransactionValueAsync(
        Guid[] instrumentIds,
        MarketPulseCalculator.Session session,
        DateTimeOffset cutoff,
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken)
    {
        if (instrumentIds.Length == 0)
            return TransactionValueResult.Unavailable("No active instruments belong to the selected segment.");
        if (session.State is MarketPulseSessionState.PreOpen or MarketPulseSessionState.Holiday or MarketPulseSessionState.Unknown)
            return TransactionValueResult.Unavailable("Transaction value is not valid in the current session state.");

        if (session.State == MarketPulseSessionState.Closed)
        {
            var daily = await dbContext.DailyInstrumentTrades.AsNoTracking()
                .Where(row => row.ProviderName == sourcePriority.PrimarySourceName &&
                              instrumentIds.Contains(row.TradingInstrumentId) && row.TradingDate == session.TradingDate)
                .ToArrayAsync(cancellationToken);
            if (daily.Length > 0)
            {
                var canonicalDaily = daily
                    .GroupBy(row => row.TradingInstrumentId)
                    .Select(group => group.OrderByDescending(row => row.SourceInsertedAt).First())
                    .ToArray();
                var watermark = canonicalDaily.Max(row => row.SourceInsertedAt);
                return new TransactionValueResult(
                    MarketPulseCalculator.CalculateTransactionValue(canonicalDaily.Select(row => row.TotalCapital)),
                    MarketPulseFactStatus.Available,
                    null,
                    "DailyInstrumentTrades", JoinProviders(canonicalDaily.Select(row => row.ProviderName)),
                    watermark, canonicalDaily.Length, instrumentIds.Length - canonicalDaily.Length);
            }
        }

        var snapshots = await dbContext.IntradayTradeSnapshots.AsNoTracking()
            .Where(row => row.ProviderName == sourcePriority.PrimarySourceName &&
                          instrumentIds.Contains(row.TradingInstrumentId) &&
                          row.TradingDate == session.TradingDate && row.ReceivedAt <= cutoff)
            .ToArrayAsync(cancellationToken);
        var latest = snapshots
            .GroupBy(row => row.TradingInstrumentId)
            .Select(group => group.OrderByDescending(row => row.TradingTime).ThenByDescending(row => row.ReceivedAt).First())
            .ToArray();
        if (latest.Length == 0)
            return TransactionValueResult.Unavailable("No canonical transaction-value observations were available at the cutoff.");
        var fresh = latest.Where(row => row.ReceivedAt >= staleBefore).ToArray();
        if (fresh.Length == 0)
            return new TransactionValueResult(null, MarketPulseFactStatus.Stale,
                "All transaction-value observations were older than the freshness threshold.",
                "IntradayTradeSnapshots", JoinProviders(latest.Select(row => row.ProviderName)),
                latest.Max(row => row.ReceivedAt), 0, instrumentIds.Length);
        var excluded = instrumentIds.Length - fresh.Select(row => row.TradingInstrumentId).Distinct().Count();
        return new TransactionValueResult(
            MarketPulseCalculator.CalculateTransactionValue(fresh.Select(row => row.TotalCapital)),
            excluded == 0 ? MarketPulseFactStatus.Available : MarketPulseFactStatus.Partial,
            excluded == 0 ? null : "Instruments without a fresh transaction-value observation were excluded.",
            "IntradayTradeSnapshots", JoinProviders(fresh.Select(row => row.ProviderName)),
            fresh.Max(row => row.ReceivedAt), fresh.Length, excluded);
    }

    private async Task<IReadOnlyCollection<MarketPulseComparison>> CalculateComparisonsAsync(
        Guid[] instrumentIds,
        DateOnly tradingDate,
        decimal? currentValue,
        CancellationToken cancellationToken)
    {
        if (instrumentIds.Length == 0)
            return
            [
                MarketPulseCalculator.CalculateComparison("weekly", 5, 3, currentValue, []),
                MarketPulseCalculator.CalculateComparison("monthly", 20, 10, currentValue, [])
            ];
        var dates = await dbContext.DailyInstrumentTrades.AsNoTracking()
            .Where(row => row.ProviderName == sourcePriority.PrimarySourceName &&
                          instrumentIds.Contains(row.TradingInstrumentId) && row.TradingDate < tradingDate)
            .Select(row => row.TradingDate)
            .Distinct()
            .OrderByDescending(date => date)
            .Take(20)
            .ToArrayAsync(cancellationToken);
        var daily = dates.Length == 0
            ? []
            : await dbContext.DailyInstrumentTrades.AsNoTracking()
                .Where(row => row.ProviderName == sourcePriority.PrimarySourceName &&
                              instrumentIds.Contains(row.TradingInstrumentId) && dates.Contains(row.TradingDate))
                .ToArrayAsync(cancellationToken);
        var values = daily
            .GroupBy(row => row.TradingDate)
            .OrderByDescending(group => group.Key)
            .Select(group => group
                .GroupBy(row => row.TradingInstrumentId)
                .Select(instrument => instrument.OrderByDescending(row => row.SourceInsertedAt).First().TotalCapital)
                .Sum())
            .ToArray();
        return
        [
            MarketPulseCalculator.CalculateComparison("weekly", 5, 3, currentValue, values),
            MarketPulseCalculator.CalculateComparison("monthly", 20, 10, currentValue, values)
        ];
    }

    private static IReadOnlyCollection<MarketPulseFact> BuildFacts(TransactionValueResult transaction) =>
    [
        new("TRANSACTION_VALUE", "ارزش معاملات", transaction.Value, "IRR", transaction.Status, transaction.Reason),
        Unsupported("SMALL_TRADE_VALUE", "ارزش معاملات خرد", "Per-trade classification is not present in canonical normalized storage."),
        Unsupported("EQUITY_REAL_MONEY_FLOW", "ورود پول حقیقی سهام", "Client-type buy/sell values are not present in canonical normalized storage."),
        Unsupported("FIXED_INCOME_REAL_MONEY_FLOW", "ورود پول حقیقی صندوق درآمد ثابت", "Client-type fund flows are not present in canonical normalized storage."),
        Unsupported("BUY_QUEUE_COUNT", "تعداد صف خرید", "Canonical order-book snapshots are not present in normalized storage."),
        Unsupported("BUY_QUEUE_VALUE", "ارزش صف خرید", "Canonical order-book snapshots are not present in normalized storage."),
        Unsupported("SELL_QUEUE_COUNT", "تعداد صف فروش", "Canonical order-book snapshots are not present in normalized storage."),
        Unsupported("SELL_QUEUE_VALUE", "ارزش صف فروش", "Canonical order-book snapshots are not present in normalized storage.")
    ];

    private static MarketPulseFact Unsupported(string code, string label, string reason) =>
        new(code, label, null, code.EndsWith("COUNT", StringComparison.Ordinal) ? "count" : "IRR",
            MarketPulseFactStatus.Unavailable, reason);

    private async Task<MarketPulseSnapshotRow> PersistRevisionAsync(
        string segment,
        MarketPulseCalculator.Session session,
        DateTimeOffset now,
        DateTimeOffset? watermark,
        IReadOnlyCollection<MarketPulseFact> facts,
        MarketPulseBreadth breadth,
        (IReadOnlyCollection<MarketPulseIndustryDriver> Leading, IReadOnlyCollection<MarketPulseIndustryDriver> Lagging) industries,
        IReadOnlyCollection<MarketPulseComparison> comparisons,
        IReadOnlyCollection<MarketPulseEvidence> evidence,
        decimal? transactionValue,
        bool isPartial,
        bool isFinal,
        string inputHash,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational())
            transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using (transaction)
        {
            if (dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                var slotKey = $"market-pulse|{session.TradingDate:yyyy-MM-dd}|{segment}|{session.State}|{session.CadenceSlot}|{DefinitionVersion}";
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({slotKey}, 0))",
                    cancellationToken);
            }

            var current = await dbContext.MarketPulseSnapshots
                .Where(row => row.TradingDate == session.TradingDate &&
                              row.Segment == segment &&
                              row.SessionState == session.State.ToString() &&
                              row.CadenceSlot == session.CadenceSlot &&
                              row.DefinitionVersion == DefinitionVersion && row.IsCurrent)
                .OrderByDescending(row => row.Revision)
                .FirstOrDefaultAsync(cancellationToken);
            if (current?.InputHash == inputHash)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return current;
            }

            if (current is not null) current.IsCurrent = false;
            var row = new MarketPulseSnapshotRow
            {
                Id = Guid.NewGuid(),
                TradingDate = session.TradingDate,
                CapturedAtUtc = now,
                GeneratedAtUtc = now,
                Segment = segment,
                SessionState = session.State.ToString(),
                CadenceSlot = session.CadenceSlot,
                IsPartial = isPartial,
                IsFinal = isFinal,
                IsCurrent = true,
                Revision = (current?.Revision ?? 0) + 1,
                SupersedesSnapshotId = current?.Id,
                DefinitionVersion = DefinitionVersion,
                SourceWatermarkUtc = watermark,
                TransactionValue = transactionValue,
                FactsJson = JsonSerializer.Serialize(facts, JsonOptions),
                BreadthJson = JsonSerializer.Serialize(breadth, JsonOptions),
                LeadingIndustriesJson = JsonSerializer.Serialize(industries.Leading, JsonOptions),
                LaggingIndustriesJson = JsonSerializer.Serialize(industries.Lagging, JsonOptions),
                ComparisonsJson = JsonSerializer.Serialize(comparisons, JsonOptions),
                EvidenceJson = JsonSerializer.Serialize(evidence, JsonOptions),
                InputHash = inputHash
            };
            dbContext.MarketPulseSnapshots.Add(row);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return row;
        }
    }

    private static string HashInput(
        string segment,
        MarketPulseCalculator.Session session,
        IReadOnlyCollection<MarketPulseFact> facts,
        MarketPulseBreadth breadth,
        (IReadOnlyCollection<MarketPulseIndustryDriver> Leading, IReadOnlyCollection<MarketPulseIndustryDriver> Lagging) industries,
        IReadOnlyCollection<MarketPulseComparison> comparisons,
        IReadOnlyCollection<MarketPulseEvidence> evidence,
        bool isFinal)
    {
        var json = JsonSerializer.Serialize(new
        {
            segment,
            session.TradingDate,
            session.State,
            session.CadenceSlot,
            facts,
            breadth,
            industries.Leading,
            industries.Lagging,
            comparisons,
            evidence,
            isFinal,
            DefinitionVersion
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static MarketPulseSnapshot Map(MarketPulseSnapshotRow row) => new(
        row.Id,
        row.TradingDate,
        row.CapturedAtUtc,
        row.GeneratedAtUtc,
        row.Segment,
        Enum.TryParse<MarketPulseSessionState>(row.SessionState, out var state) ? state : MarketPulseSessionState.Unknown,
        row.CadenceSlot,
        row.IsPartial,
        row.IsFinal,
        row.Revision,
        row.SupersedesSnapshotId,
        row.DefinitionVersion,
        row.SourceWatermarkUtc,
        Deserialize<MarketPulseFact[]>(row.FactsJson) ?? [],
        Deserialize<MarketPulseBreadth>(row.BreadthJson) ??
            new MarketPulseBreadth(null, null, null, 0, 0, MarketPulseFactStatus.Unavailable, "Breadth evidence is unavailable."),
        Deserialize<MarketPulseIndustryDriver[]>(row.LeadingIndustriesJson) ?? [],
        Deserialize<MarketPulseIndustryDriver[]>(row.LaggingIndustriesJson) ?? [],
        Deserialize<MarketPulseComparison[]>(row.ComparisonsJson) ?? [],
        Deserialize<MarketPulseEvidence[]>(row.EvidenceJson) ?? [],
        Disclaimer);

    private static T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, JsonOptions);

    private static bool IsSlotConcurrencyConflict(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException postgres } &&
            postgres.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure ||
        exception is PostgresException direct &&
            direct.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure;

    private static string JoinProviders(IEnumerable<string> providers)
    {
        var values = providers.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return values.Length == 0 ? "Unavailable" : string.Join(",", values);
    }

    private sealed record TransactionValueResult(
        decimal? Value,
        MarketPulseFactStatus Status,
        string? Reason,
        string Dataset,
        string Provider,
        DateTimeOffset? Watermark,
        int Included,
        int Excluded)
    {
        public static TransactionValueResult Unavailable(string reason) =>
            new(null, MarketPulseFactStatus.Unavailable, reason, "TransactionValue", "Unavailable", null, 0, 0);
    }
}
