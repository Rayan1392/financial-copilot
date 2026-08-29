using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesPsSyncOptions
{
    public const string SectionName = "CyclicalWavesPsSync";
    public bool Enabled { get; init; }
    public int SnapshotCadenceMinutes { get; init; } = 1440;
    public int HistoryCadenceHours { get; init; } = 168;
    public int MaxConcurrency { get; init; } = 4;
    public int RequestDelayMilliseconds { get; init; }
    public int MaxCompaniesPerRun { get; init; } = 250;
    public int MaxRunDurationMinutes { get; init; } = 90;
    public int MaxResponseBytes { get; init; } = 5 * 1024 * 1024;
    public int MaxHistoryPointsPerCompany { get; init; } = 10_000;
    public int LeaseDurationMinutes { get; init; } = 120;
    public int LeaseRenewalMinutes { get; init; } = 10;
    public bool AllowManualSyncWhenWorkerDisabled { get; init; } = true;
    public decimal MaximumAbsoluteRatio { get; init; } = 1000m;
    public decimal CurrentHistoryTolerance { get; init; } = 0.0001m;
}

public sealed class NoavaranEligibleCompanyPsScopeReader(FinancialIngestionDbContext dbContext) : IPsEligibleCompanyScopeReader
{
    // CyclicalWaves P/S endpoints only publish data for these two eligible markets.
    // Keep the allow-list at the scope boundary so no provider request is made for
    // companies outside the supported market universe.
    private const string SupportedMarketIds =
        "'037c69ad-f519-419f-ae62-59003b6b2428'::uuid, 'a3ccb30a-caed-4f26-a84a-ac0eb8c78c76'::uuid";

    private static readonly Regex IsinPattern = new("^[A-Z0-9]{12}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<PsEligibleCompanyScope> ReadAsync(int? maxCompanies, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database.SqlQueryRaw<EligibleCompanyRow>(
            $"SELECT \"Id\" AS \"CompanyId\", \"SymbolIsin\", \"CompanySymbol\" FROM \"NoavaranEligibleCompanies\" " +
            $"WHERE \"MarketId\" = ANY (ARRAY[{SupportedMarketIds}])")
            .ToListAsync(cancellationToken);
        var issues = new List<PsScopeIssue>();
        var candidates = new List<PsEligibleCompany>();
        foreach (var row in rows)
        {
            var isin = NormalizeIsin(row.SymbolIsin);
            if (isin is null) { issues.Add(new PsScopeIssue(row.CompanyId, row.SymbolIsin, "MissingOrInvalidIsin")); continue; }
            if (string.IsNullOrWhiteSpace(row.CompanySymbol)) { issues.Add(new PsScopeIssue(row.CompanyId, isin, "MissingCompanySymbol")); continue; }
            candidates.Add(new PsEligibleCompany(row.CompanyId, isin, row.CompanySymbol.Trim()));
        }
        var distinct = candidates.Distinct().ToArray();
        foreach (var group in distinct.GroupBy(x => x.CompanyId).Where(x => x.Select(i => i.SymbolIsin).Distinct(StringComparer.Ordinal).Count() > 1))
            foreach (var item in group) issues.Add(new PsScopeIssue(item.CompanyId, item.SymbolIsin, "CompanyMappedToMultipleIsins"));
        foreach (var group in distinct.GroupBy(x => x.SymbolIsin, StringComparer.Ordinal).Where(x => x.Select(i => i.CompanyId).Distinct().Count() > 1))
            foreach (var item in group) issues.Add(new PsScopeIssue(item.CompanyId, item.SymbolIsin, "IsinMappedToMultipleCompanies"));
        var conflictedCompanies = issues.Where(x => x.CompanyId.HasValue && x.Code != "MissingOrInvalidIsin").Select(x => x.CompanyId!.Value).ToHashSet();
        var conflictedIsins = issues.Where(x => x.SymbolIsin is not null && x.Code != "MissingOrInvalidIsin").Select(x => x.SymbolIsin!).ToHashSet(StringComparer.Ordinal);
        var valid = distinct.Where(x => !conflictedCompanies.Contains(x.CompanyId) && !conflictedIsins.Contains(x.SymbolIsin))
            .OrderBy(x => x.CompanyId).Take(maxCompanies is > 0 ? maxCompanies.Value : int.MaxValue).ToArray();
        return new PsEligibleCompanyScope(rows.Count, candidates.Count - distinct.Length, rows.Count - candidates.Count, valid, issues);
    }

    private static string? NormalizeIsin(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is not null && IsinPattern.IsMatch(normalized) ? normalized : null;
    }

    private sealed class EligibleCompanyRow { public Guid CompanyId { get; set; } public string? SymbolIsin { get; set; } public string? CompanySymbol { get; set; } }
}

public sealed class CyclicalWavesPsVisualizationSyncService(
    FinancialIngestionDbContext db,
    IPsEligibleCompanyScopeReader scopeReader,
    ICyclicalWavesPsProviderClient provider,
    IOptions<CyclicalWavesPsSyncOptions> options,
    TimeProvider clock,
    ILogger<CyclicalWavesPsVisualizationSyncService> logger) : ICyclicalWavesPsVisualizationSyncService, ICompanyPsVisualizationReader, IFeature114AcceptedPsVisualizationPersistence
{
    private const string ProviderName = "CyclicalWaves";
    private readonly CyclicalWavesPsSyncOptions _options = options.Value;

    public async Task<PsVisualizationSyncResult> SyncAsync(PsVisualizationSyncRequest request, CancellationToken cancellationToken)
    {
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId.Trim();
        if (!await TryAcquireLeaseAsync(correlationId, cancellationToken))
            return new PsVisualizationSyncResult(correlationId, 0, 0, 0, 0, 0, new[] { new PsScopeIssue(null, null, "LeaseContended") });
        try
        {
        var scope = await scopeReader.ReadAsync(request.MaxCompanies ?? _options.MaxCompaniesPerRun, cancellationToken);
        var companies = request.CompanyId is { } companyId ? scope.Companies.Where(x => x.CompanyId == companyId).ToArray() : scope.Companies;
        if (request.DryRun) return new PsVisualizationSyncResult(correlationId, companies.Count(), 0, 0, 0, 0, scope.Issues);
        var snapshotSucceeded = 0; var historySucceeded = 0; var failed = 0; var unchanged = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(_options.MaxRunDurationMinutes));
        foreach (var company in companies)
        {
            if (deadline.IsCancellationRequested) break;
            if (!await RenewLeaseAsync(correlationId, deadline.Token))
            {
                failed++;
                break;
            }
            try
            {
                if (!request.HistoryOnly)
                {
                    var snapshot = await SyncSnapshotAsync(company, correlationId, deadline.Token);
                    if (snapshot.Changed) snapshotSucceeded++; else unchanged++;
                    if (snapshot.ClientError) continue;
                }
                if (!request.SnapshotOnly && await IsHistoryDueAsync(company.CompanyId, deadline.Token))
                {
                    var changed = await SyncHistoryAsync(company, correlationId, deadline.Token);
                    if (changed) historySucceeded++; else unchanged++;
                }
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "CyclicalWaves P/S sync failed for canonical company {CompanyId}; processing continues.", company.CompanyId);
            }
            if (_options.RequestDelayMilliseconds > 0) await Task.Delay(_options.RequestDelayMilliseconds, deadline.Token);
        }
        return new PsVisualizationSyncResult(correlationId, companies.Count(), snapshotSucceeded, historySucceeded, failed, unchanged, scope.Issues);
        }
        finally
        {
            await ReleaseLeaseAsync(correlationId, CancellationToken.None);
        }
    }

    public async Task PersistAcceptedGaugeAsync(Guid companyId, string symbolIsin, PsGaugeDistribution gauge,
        DateTimeOffset fetchedAtUtc, string? correlationId, CancellationToken cancellationToken)
    {
        if (gauge.GaugeClose <= 0m || gauge.GaugeAverage <= 0m) return;
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);
        var symbol = new PsEligibleCompany(companyId, symbolIsin.Trim().ToUpperInvariant(), company?.Ticker ?? string.Empty);
        var observationDate = DateOnly.FromDateTime(fetchedAtUtc.UtcDateTime);
        var current = new PsCurrentValues(symbol.CompanySymbol, symbol.CompanySymbol, gauge.GaugeClose, gauge.GaugeAverage, observationDate);
        var normalizedHash = HashSnapshot(symbol, gauge, current);
        var existing = await db.CompanyPsGaugeSnapshots.SingleOrDefaultAsync(x =>
            x.ProviderName == ProviderName && x.CompanyId == companyId && x.ObservationDate == observationDate, cancellationToken);
        if (existing is not null && existing.NormalizedSnapshotHash == normalizedHash) return;
        var now = clock.GetUtcNow();
        var total = gauge.BucketA + gauge.BucketB + gauge.BucketC + gauge.BucketD + gauge.BucketE + gauge.BucketF;
        var row = existing ?? new CompanyPsGaugeSnapshotRow { Id = Guid.NewGuid(), CompanyId = companyId, ProviderName = ProviderName, ObservationDate = observationDate, FirstSeenAtUtc = now };
        row.SourceCompanyIsin = symbol.SymbolIsin; row.TtmPsRatio = gauge.GaugeClose; row.ForwardPsRatio = gauge.GaugeAverage;
        row.GaugeClose = gauge.GaugeClose; row.GaugeAverage = gauge.GaugeAverage; row.BoundaryStart = gauge.BoundaryStart;
        row.BoundaryMin = gauge.BoundaryMin; row.BoundaryAverage = gauge.BoundaryAverage; row.BoundaryMax = gauge.BoundaryMax; row.BoundaryEnd = gauge.BoundaryEnd;
        row.BucketA = gauge.BucketA; row.BucketB = gauge.BucketB; row.BucketC = gauge.BucketC; row.BucketD = gauge.BucketD; row.BucketE = gauge.BucketE; row.BucketF = gauge.BucketF; row.BucketTotal = total;
        row.ProviderSymbol = symbol.CompanySymbol; row.GaugeFetchedAtUtc = fetchedAtUtc; row.CurrentValuesFetchedAtUtc = fetchedAtUtc; row.LastSyncedAtUtc = fetchedAtUtc;
        row.CompletenessStatus = PsVisualizationComponentStatus.Complete.ToString(); row.GaugeRenderabilityStatus = total > 0 ? GaugeRenderabilityStatus.Renderable.ToString() : GaugeRenderabilityStatus.InvalidBucketTotal.ToString(); row.QualityStatus = "Valid";
        row.GaugePayloadHash = HashGauge(gauge); row.CurrentValuesPayloadHash = HashCurrent(current); row.NormalizedSnapshotHash = normalizedHash;
        if (existing is null) db.CompanyPsGaugeSnapshots.Add(row);
        var state = await GetOrCreateStateAsync(symbol, cancellationToken);
        state.SourceCompanyIsin = symbol.SymbolIsin; state.LastSuccessfulSnapshotId = row.Id; state.LastSuccessfulSnapshotDate = observationDate;
        state.LastGaugeSuccessAtUtc = fetchedAtUtc; state.LastCurrentValuesSuccessAtUtc = fetchedAtUtc; state.LastSuccessfulCorrelationId = correlationId;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PsVisualizationReadModel?> GetAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var snapshot = await db.CompanyPsGaugeSnapshots.AsNoTracking().Where(x => x.CompanyId == companyId && x.GaugeRenderabilityStatus == GaugeRenderabilityStatus.Renderable.ToString()).OrderByDescending(x => x.ObservationDate).FirstOrDefaultAsync(cancellationToken);
        var state = await db.CompanyPsSeriesSyncStates.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ProviderName == ProviderName, cancellationToken);
        var points = await db.CompanyPsHistoryPoints.AsNoTracking().Where(x => x.CompanyId == companyId && x.ProviderName == ProviderName && x.IsActiveInLatestSuccessfulSeries).OrderBy(x => x.ObservationDate).ThenBy(x => x.ProviderPointId).Select(x => new PsHistoryPoint(x.ProviderPointId, x.ObservationDate, x.PsRatio)).ToArrayAsync(cancellationToken);
        if (snapshot is null && state is null && points.Length == 0) return null;
        var warnings = state is null ? Array.Empty<string>() : ParseWarnings(state.LastWarningCodesJson);
        var lastPs = snapshot is null ? null : await ReadLatestLastPsAsync(
            companyId,
            snapshot.SourceCompanyIsin,
            cancellationToken);
        return new PsVisualizationReadModel(
            companyId,
            snapshot is null ? PsVisualizationComponentStatus.Partial : PsVisualizationComponentStatus.Complete,
            snapshot is null ? GaugeRenderabilityStatus.UnverifiedSemantics : GaugeRenderabilityStatus.Renderable,
            lastPs?.ObservationDate ?? snapshot?.ObservationDate,
            snapshot?.LastSyncedAtUtc,
            state?.LastHistorySuccessAtUtc,
            warnings,
            points,
            snapshot is null ? null : new PsPersistedSnapshotFacts(
                snapshot.ProviderName, snapshot.ProviderSymbol, lastPs?.Value, snapshot.ForwardPsRatio,
                snapshot.GaugeClose, snapshot.BoundaryStart, snapshot.BoundaryMin, snapshot.BoundaryAverage,
                snapshot.BoundaryMax, snapshot.BoundaryEnd, snapshot.BucketA, snapshot.BucketB, snapshot.BucketC,
                snapshot.BucketD, snapshot.BucketE, snapshot.BucketF, snapshot.LastSyncedAtUtc,
                lastPs?.SnapshotId, lastPs?.SymbolIsin, lastPs?.AcquiredAtUtc, lastPs?.ObservationDate));
    }

    private async Task<LastPsMarker?> ReadLatestLastPsAsync(
        Guid companyId,
        string symbolIsin,
        CancellationToken cancellationToken)
    {
        var normalizedIsin = symbolIsin.Trim().ToUpperInvariant();
        if (normalizedIsin.Length == 0) return null;

        var candidates = await db.CyclicalWavesMetricSnapshots.AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.ProviderName == ProviderName &&
                        x.MetricType == "LastPS" &&
                        x.SymbolIsin == normalizedIsin)
            .OrderByDescending(x => x.AcquisitionDateUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0) return null;

        var snapshotIds = candidates.Select(x => x.Id).ToArray();
        var acceptedChecks = await db.CyclicalWavesAcquisitionChecks.AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.ProviderName == ProviderName &&
                        x.MetricType == "LastPS" &&
                        x.SnapshotId.HasValue &&
                        snapshotIds.Contains(x.SnapshotId.Value) &&
                        (x.Result == "Changed" || x.Result == "NoChange"))
            .ToArrayAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var accepted = acceptedChecks
                .Where(x => x.SnapshotId == candidate.Id &&
                            x.ResponseHash == candidate.ResponseHash &&
                            string.Equals(x.SymbolIsin?.Trim(), candidate.SymbolIsin, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CompletedAtUtc)
                .ThenByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();
            if (accepted is null) continue;
            if (!TryReadPsRatio(candidate.RawResponseJson, out var value)) continue;

            return new LastPsMarker(candidate.Id, candidate.SymbolIsin, candidate.AcquisitionDateUtc, candidate.ProviderObservationDate, value);
        }

        return null;
    }

    private static bool TryReadPsRatio(string rawResponseJson, out decimal value)
    {
        value = default;
        try
        {
            using var document = JsonDocument.Parse(rawResponseJson);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("ps_ratio", out var ratio) ||
                ratio.ValueKind != JsonValueKind.Number ||
                !ratio.TryGetDecimal(out value) ||
                value < 0m)
            {
                value = default;
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private sealed record LastPsMarker(
        Guid SnapshotId,
        string SymbolIsin,
        DateTimeOffset AcquiredAtUtc,
        DateOnly? ObservationDate,
        decimal Value);

    private sealed record SnapshotSyncOutcome(bool Changed, bool ClientError);

    private async Task<SnapshotSyncOutcome> SyncSnapshotAsync(PsEligibleCompany company, string correlationId, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var state = await GetOrCreateStateAsync(company, token);
        state.LastGaugeAttemptAtUtc = now;
        var gaugeResult = await provider.GetGaugeAsync(company.SymbolIsin, token);
        if (!gaugeResult.IsSuccess && gaugeResult.IsClientError)
        {
            state.ConsecutiveGaugeFailures++;
            state.LastErrorCode = gaugeResult.ErrorCode.ToString();
            await db.SaveChangesAsync(token);
            return new SnapshotSyncOutcome(false, true);
        }

        state.LastCurrentValuesAttemptAtUtc = now;
        var currentResult = await provider.GetCurrentValuesAsync(company.SymbolIsin, token);
        if (!currentResult.IsSuccess && currentResult.IsClientError)
        {
            state.ConsecutiveGaugeFailures += gaugeResult.IsSuccess ? 0 : 1;
            state.ConsecutiveCurrentValuesFailures++;
            state.LastErrorCode = currentResult.ErrorCode.ToString();
            await db.SaveChangesAsync(token);
            return new SnapshotSyncOutcome(false, true);
        }

        var forwardResult = await provider.GetForwardValuesAsync(company.CompanySymbol, token);
        if (!forwardResult.IsSuccess && forwardResult.IsClientError)
        {
            state.LastErrorCode = forwardResult.ErrorCode.ToString();
            await db.SaveChangesAsync(token);
            return new SnapshotSyncOutcome(false, true);
        }

        if (!gaugeResult.IsSuccess || !currentResult.IsSuccess || !forwardResult.IsSuccess)
        {
            state.ConsecutiveGaugeFailures += gaugeResult.IsSuccess ? 0 : 1;
            state.ConsecutiveCurrentValuesFailures += currentResult.IsSuccess ? 0 : 1;
            state.LastErrorCode = (!gaugeResult.IsSuccess ? gaugeResult.ErrorCode : !currentResult.IsSuccess ? currentResult.ErrorCode : forwardResult.ErrorCode).ToString();
            await db.SaveChangesAsync(token); return new SnapshotSyncOutcome(false, false);
        }
        var gauge = gaugeResult.Value!; var current = currentResult.Value! with { ForwardPsRatio = forwardResult.Value!.ForwardPsRatio };
        if (!string.Equals(current.Ticker.Trim(), company.SymbolIsin, StringComparison.OrdinalIgnoreCase))
        {
            state.LastErrorCode = PsVisualizationSyncErrorCode.IdentityMismatch.ToString(); await db.SaveChangesAsync(token); return new SnapshotSyncOutcome(false, false);
        }
        if (!IsRatioSane(current.TtmPsRatio) || !IsRatioSane(current.ForwardPsRatio) || !IsRatioSane(gauge.GaugeClose))
        {
            state.LastErrorCode = PsVisualizationSyncErrorCode.DataQualityRejected.ToString(); await db.SaveChangesAsync(token); return new SnapshotSyncOutcome(false, false);
        }
        if (new[] { gauge.BucketA, gauge.BucketB, gauge.BucketC, gauge.BucketD, gauge.BucketE, gauge.BucketF }.Any(x => x < 0) ||
            gauge.BoundaryMax <= gauge.BoundaryMin)
        {
            state.LastErrorCode = PsVisualizationSyncErrorCode.DataQualityRejected.ToString(); await db.SaveChangesAsync(token); return new SnapshotSyncOutcome(false, false);
        }
        long total;
        try { total = checked(checked(checked(checked(checked(gauge.BucketA + gauge.BucketB) + gauge.BucketC) + gauge.BucketD) + gauge.BucketE) + gauge.BucketF); }
        catch (OverflowException) { state.LastErrorCode = PsVisualizationSyncErrorCode.DataQualityRejected.ToString(); await db.SaveChangesAsync(token); return new SnapshotSyncOutcome(false, false); }
        var renderability = total <= 0 ? GaugeRenderabilityStatus.InvalidBucketTotal : GaugeRenderabilityStatus.Renderable;
        var warnings = new List<string>(); if (total <= 0) warnings.Add("InvalidBucketTotal");
        var normalizedHash = HashSnapshot(company, gauge, current);
        var existing = await db.CompanyPsGaugeSnapshots.SingleOrDefaultAsync(x => x.ProviderName == ProviderName && x.CompanyId == company.CompanyId && x.ObservationDate == current.ObservationDate, token);
        if (existing is not null && existing.NormalizedSnapshotHash == normalizedHash)
        {
            state.LastGaugeSuccessAtUtc = now; state.LastCurrentValuesSuccessAtUtc = now; state.ConsecutiveGaugeFailures = 0; state.ConsecutiveCurrentValuesFailures = 0; state.LastErrorCode = null; state.LastWarningCodesJson = "[]"; await db.SaveChangesAsync(token); return new SnapshotSyncOutcome(false, false);
        }
        var row = existing ?? new CompanyPsGaugeSnapshotRow { Id = Guid.NewGuid(), CompanyId = company.CompanyId, ProviderName = ProviderName, ObservationDate = current.ObservationDate, FirstSeenAtUtc = now };
        row.SourceCompanyIsin = company.SymbolIsin; row.TtmPsRatio = current.TtmPsRatio; row.ForwardPsRatio = current.ForwardPsRatio; row.GaugeClose = gauge.GaugeClose; row.GaugeAverage = gauge.GaugeAverage;
        row.BoundaryStart = gauge.BoundaryStart; row.BoundaryMin = gauge.BoundaryMin; row.BoundaryAverage = gauge.BoundaryAverage; row.BoundaryMax = gauge.BoundaryMax; row.BoundaryEnd = gauge.BoundaryEnd;
        row.BucketA = gauge.BucketA; row.BucketB = gauge.BucketB; row.BucketC = gauge.BucketC; row.BucketD = gauge.BucketD; row.BucketE = gauge.BucketE; row.BucketF = gauge.BucketF; row.BucketTotal = total;
        row.ProviderSymbol = current.Symbol; row.GaugeFetchedAtUtc = now; row.CurrentValuesFetchedAtUtc = now; row.LastSyncedAtUtc = now; row.CompletenessStatus = PsVisualizationComponentStatus.Complete.ToString(); row.GaugeRenderabilityStatus = renderability.ToString(); row.QualityStatus = "Valid"; row.QualityWarningsJson = JsonSerializer.Serialize(warnings); row.GaugePayloadHash = HashGauge(gauge); row.CurrentValuesPayloadHash = HashCurrent(current); row.NormalizedSnapshotHash = normalizedHash;
        if (existing is null) db.CompanyPsGaugeSnapshots.Add(row);
        state.LastSuccessfulSnapshotId = row.Id; state.LastSuccessfulSnapshotDate = row.ObservationDate; state.LastGaugeSuccessAtUtc = now; state.LastCurrentValuesSuccessAtUtc = now; state.ConsecutiveGaugeFailures = 0; state.ConsecutiveCurrentValuesFailures = 0; state.LastWarningCodesJson = JsonSerializer.Serialize(warnings); state.LastSuccessfulCorrelationId = correlationId;
        state.LastErrorCode = null;
        await db.SaveChangesAsync(token); return new SnapshotSyncOutcome(true, false);
    }

    private async Task<bool> SyncHistoryAsync(PsEligibleCompany company, string correlationId, CancellationToken token)
    {
        var now = clock.GetUtcNow(); var state = await GetOrCreateStateAsync(company, token); state.LastHistoryAttemptAtUtc = now;
        var result = await provider.GetHistoryAsync(company.SymbolIsin, token);
        if (!result.IsSuccess) { state.ConsecutiveHistoryFailures++; state.LastErrorCode = result.ErrorCode.ToString(); await db.SaveChangesAsync(token); return false; }
        var series = result.Value!;
        if (series.Points.Count > _options.MaxHistoryPointsPerCompany || series.Points.Any(x => !IsRatioSane(x.PsRatio))) { state.LastErrorCode = PsVisualizationSyncErrorCode.DataQualityRejected.ToString(); await db.SaveChangesAsync(token); return false; }
        var duplicateIds = series.Points.GroupBy(x => x.ProviderPointId, StringComparer.Ordinal).Where(x => x.Select(p => (p.ObservationDate, p.PsRatio)).Distinct().Count() > 1).ToArray();
        if (duplicateIds.Length > 0) { state.LastErrorCode = PsVisualizationSyncErrorCode.DataQualityRejected.ToString(); await db.SaveChangesAsync(token); return false; }
        var normalized = series.Points.GroupBy(x => x.ProviderPointId, StringComparer.Ordinal).Select(x => x.First()).OrderBy(x => x.ObservationDate).ThenBy(x => x.ProviderPointId, StringComparer.Ordinal).ToArray();
        var actualFirst = normalized.FirstOrDefault()?.ObservationDate; var actualLast = normalized.LastOrDefault()?.ObservationDate;
        var metadataMatches = series.DeclaredCount == normalized.LongLength && series.DeclaredFirstDate == actualFirst && series.DeclaredLastDate == actualLast;
        var warningCodes = metadataMatches ? Array.Empty<string>() : new[] { "DeclaredHistoryMetadataMismatch" };
        var existing = await db.CompanyPsHistoryPoints.Where(x => x.ProviderName == ProviderName && x.CompanyId == company.CompanyId).ToListAsync(token);
        var existingById = existing.ToDictionary(x => x.ProviderPointId, StringComparer.Ordinal);
        if (normalized.Any(point => existingById.TryGetValue(point.ProviderPointId, out var old) && (old.ObservationDate != point.ObservationDate || old.PsRatio != point.PsRatio))) { state.LastErrorCode = "ConflictingProviderPointId"; await db.SaveChangesAsync(token); return false; }
        var hash = HashHistory(normalized, series);
        if (metadataMatches && state.NormalizedLatestSuccessfulSeriesHash == hash) { state.LastHistorySuccessAtUtc = now; state.ConsecutiveHistoryFailures = 0; await db.SaveChangesAsync(token); return false; }
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        foreach (var point in normalized)
        {
            if (!existingById.TryGetValue(point.ProviderPointId, out var row)) { row = new CompanyPsHistoryPointRow { Id = Guid.NewGuid(), CompanyId = company.CompanyId, ProviderName = ProviderName, ProviderPointId = point.ProviderPointId, ObservationDate = point.ObservationDate, PsRatio = point.PsRatio, SourceCompanyIsin = company.SymbolIsin, FirstSeenAtUtc = now }; db.CompanyPsHistoryPoints.Add(row); }
            row.LastSeenAtUtc = now; row.SourcePayloadHash = hash;
            if (metadataMatches) { row.IsActiveInLatestSuccessfulSeries = true; row.LastSeenInSuccessfulSeriesAtUtc = now; }
        }
        if (metadataMatches) foreach (var row in existing.Where(x => !normalized.Any(p => p.ProviderPointId == x.ProviderPointId))) row.IsActiveInLatestSuccessfulSeries = false;
        state.DeclaredFirstHistoryDate = series.DeclaredFirstDate; state.DeclaredLastHistoryDate = series.DeclaredLastDate; state.DeclaredHistoryCount = series.DeclaredCount; state.ActualFirstHistoryDate = actualFirst; state.ActualLastHistoryDate = actualLast; state.ActualHistoryCount = normalized.LongLength; state.LastWarningCodesJson = JsonSerializer.Serialize(warningCodes); state.LastHistorySuccessAtUtc = now; state.ConsecutiveHistoryFailures = 0; state.LastErrorCode = null;
        if (metadataMatches) { state.NormalizedLatestSuccessfulSeriesHash = hash; state.LastCompleteHistoryRefreshAtUtc = now; state.BackfillCompleted = true; state.NextEligibleHistoryRefreshAtUtc = now.AddHours(_options.HistoryCadenceHours); state.LastSuccessfulCorrelationId = correlationId; }
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return true;
    }

    private Task<bool> IsHistoryDueAsync(Guid companyId, CancellationToken token) => db.CompanyPsSeriesSyncStates.AsNoTracking().Where(x => x.CompanyId == companyId && x.ProviderName == ProviderName).Select(x => !x.NextEligibleHistoryRefreshAtUtc.HasValue || x.NextEligibleHistoryRefreshAtUtc <= clock.GetUtcNow()).SingleOrDefaultAsync(token);
    private async Task<CompanyPsSeriesSyncStateRow> GetOrCreateStateAsync(PsEligibleCompany company, CancellationToken token) => await db.CompanyPsSeriesSyncStates.SingleOrDefaultAsync(x => x.CompanyId == company.CompanyId && x.ProviderName == ProviderName, token) ?? AddState(company);
    private CompanyPsSeriesSyncStateRow AddState(PsEligibleCompany company) { var state = new CompanyPsSeriesSyncStateRow { Id = Guid.NewGuid(), CompanyId = company.CompanyId, ProviderName = ProviderName, SourceCompanyIsin = company.SymbolIsin }; db.CompanyPsSeriesSyncStates.Add(state); return state; }
    private bool IsRatioSane(decimal value) => value >= -_options.MaximumAbsoluteRatio && value <= _options.MaximumAbsoluteRatio;
    private static IReadOnlyList<string> ParseWarnings(string json) { try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); } catch (JsonException) { return new[] { "InvalidStoredWarnings" }; } }
    private static string HashGauge(PsGaugeDistribution x) => Hash(string.Join('|', x.BucketA, x.BucketB, x.BucketC, x.BucketD, x.BucketE, x.BucketF, D(x.GaugeClose), D(x.BoundaryStart), D(x.BoundaryMin), D(x.BoundaryAverage), D(x.BoundaryMax), D(x.BoundaryEnd)));
    private static string HashCurrent(PsCurrentValues x) => Hash($"{x.Ticker.Trim().ToUpperInvariant()}|{D(x.TtmPsRatio)}|{D(x.ForwardPsRatio)}|{x.ObservationDate:yyyy-MM-dd}");
    private static string HashSnapshot(PsEligibleCompany company, PsGaugeDistribution gauge, PsCurrentValues current) => Hash($"{company.CompanyId:D}|{company.SymbolIsin}|{HashGauge(gauge)}|{HashCurrent(current)}");
    private static string HashHistory(IEnumerable<PsHistoryPoint> points, PsHistorySeries series) => Hash(string.Join('\n', points.OrderBy(x => x.ObservationDate).ThenBy(x => x.ProviderPointId, StringComparer.Ordinal).Select(x => $"{x.ProviderPointId}|{x.ObservationDate:yyyy-MM-dd}|{D(x.PsRatio)}").Append($"meta|{series.DeclaredFirstDate:yyyy-MM-dd}|{series.DeclaredLastDate:yyyy-MM-dd}|{series.DeclaredCount}")));
    private static string D(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async Task<bool> TryAcquireLeaseAsync(string owner, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var lease = await db.CompanyPsVisualizationLeases.SingleOrDefaultAsync(x => x.LeaseName == "CyclicalWavesPsVisualizationSync", token);
        if (lease is null)
        {
            db.CompanyPsVisualizationLeases.Add(new CompanyPsVisualizationLeaseRow { LeaseName = "CyclicalWavesPsVisualizationSync", Owner = owner, UpdatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(_options.LeaseDurationMinutes) });
            try { await db.SaveChangesAsync(token); return true; }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); return false; }
        }
        if (lease.ExpiresAtUtc > now && !string.Equals(lease.Owner, owner, StringComparison.Ordinal)) return false;
        lease.Owner = owner; lease.UpdatedAtUtc = now; lease.ExpiresAtUtc = now.AddMinutes(_options.LeaseDurationMinutes); await db.SaveChangesAsync(token); return true;
    }

    private async Task<bool> RenewLeaseAsync(string owner, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var affected = await db.CompanyPsVisualizationLeases.Where(x => x.LeaseName == "CyclicalWavesPsVisualizationSync" && x.Owner == owner && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(x => x.SetProperty(row => row.UpdatedAtUtc, now).SetProperty(row => row.ExpiresAtUtc, now.AddMinutes(_options.LeaseDurationMinutes)), token);
        return affected == 1;
    }

    private async Task ReleaseLeaseAsync(string owner, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        await db.CompanyPsVisualizationLeases.Where(x => x.LeaseName == "CyclicalWavesPsVisualizationSync" && x.Owner == owner)
            .ExecuteUpdateAsync(x => x.SetProperty(row => row.ExpiresAtUtc, now).SetProperty(row => row.UpdatedAtUtc, now), token);
    }
}
