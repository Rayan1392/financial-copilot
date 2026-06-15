using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Provider-agnostic recalculation outbox drain. Reads pending
/// <see cref="MetricRecalculationRequestRow"/> rows, resolves affected symbols + registered
/// dependent metrics, and invokes the existing <see cref="IDerivedMetricRecalculationCommand"/>
/// (calculation stays in the engine — one source of truth). Idempotent on the
/// <c>DerivedMetrics</c> unique key.
/// </summary>
public sealed class MetricRecalculationProcessor(
    FinancialIngestionDbContext dbContext,
    IFinancialMetricRegistry metricRegistry,
    IMetricCalculationPolicyProvider policyProvider,
    INormalizedMetricInputReader inputReader,
    IDerivedMetricRecalculationCommand recalculationCommand,
    TimeProvider timeProvider,
    ILogger<MetricRecalculationProcessor> logger) : IMetricRecalculationProcessor
{
    // Which source metric codes are persisted by each provider dataset. Data-driven, not branched.
    // FinancialRatios/FundamentalIndexes write DerivedMetrics directly (vendor-precomputed) and need no recompute.
    private static readonly IReadOnlyDictionary<ProviderDataset, IReadOnlySet<string>> SourceMetricsByDataset =
        new Dictionary<ProviderDataset, IReadOnlySet<string>>
        {
            [ProviderDataset.FinancialStatements] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "NET_PROFIT", "REVENUE", "GROSS_PROFIT", "OPERATING_PROFIT",
                "EPS", "TOTAL_EQUITY", "FINANCE_COSTS", "INCOME_TAX",
                "OPERATING_CASH_FLOW",
                // Vendor-supplied ratio snapshots (CyclicalWaves) that trigger PE_TTM / PS_TTM passthrough.
                "PE_RATIO", "PS_RATIO",
                // CyclicalWaves vendor-supplied margin line items (Order 66 / Phase 4).
                "NET_PROFIT_MARGIN", "GROSS_PROFIT_MARGIN", "OPERATING_PROFIT_MARGIN",
                // CyclicalWaves vendor-supplied 4-quarter rolling average (Order 66 / Phase 2).
                "AVG_4Q_REVENUE"
            },
            [ProviderDataset.MonthlyProductionSales] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "MONTHLY_SALES",
                // Spec 057: monthly-activity aggregates persisted per company-month for lookup.
                "MONTHLY_SALES_QUANTITY", "MONTHLY_PRODUCTION_QUANTITY", "MONTHLY_SALES_RATE",
                // CyclicalWaves vendor-supplied 12-month rolling average (Order 66 / Phase 2).
                "AVG_12M_MONTHLY_SALES"
            }
        };

    public async Task<MetricRecalculationProcessingResult> ProcessPendingAsync(
        int maximumBatch,
        CancellationToken cancellationToken)
    {
        if (maximumBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBatch));
        }

        var pending = await dbContext.MetricRecalculationRequests
            .Where(row => row.ProcessedAt == null)
            .OrderBy(row => row.RequestedAt)
            .ThenBy(row => row.Id)
            .Take(maximumBatch)
            .ToListAsync(cancellationToken);

        var completed = 0;
        var failed = 0;
        var totalRecomputed = 0;

        foreach (var row in pending)
        {
            var attemptedAt = timeProvider.GetUtcNow();
            row.AttemptCount++;
            row.LastAttemptAt = attemptedAt;

            try
            {
                var dataset = Enum.Parse<ProviderDataset>(row.SourceDataset);
                var recomputed = await ProcessOneAsync(row, dataset, cancellationToken);
                row.ProcessedAt = attemptedAt;
                row.LastError = null;
                completed++;
                totalRecomputed += recomputed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                row.LastError = LimitError(exception.Message);
                failed++;
                logger.LogWarning(
                    exception,
                    "Metric recalculation request {RequestId} failed (dataset {Dataset}, ref {Reference}).",
                    row.Id,
                    row.SourceDataset,
                    row.ExternalReference);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (pending.Count > 0)
        {
            logger.LogInformation(
                "Metric recalculation drained {Processed} requests ({Completed} ok, {Failed} failed); recomputed {Metrics} metrics.",
                pending.Count,
                completed,
                failed,
                totalRecomputed);
        }

        return new MetricRecalculationProcessingResult(pending.Count, completed, totalRecomputed, failed);
    }

    private async Task<int> ProcessOneAsync(
        MetricRecalculationRequestRow row,
        ProviderDataset dataset,
        CancellationToken cancellationToken)
    {
        if (!SourceMetricsByDataset.TryGetValue(dataset, out var sourceCodes) || sourceCodes.Count == 0)
        {
            // Datasets without engine-derived dependents (Symbols, FinancialRatios, FundamentalIndexes, MarketQuotes).
            return 0;
        }

        var reference = row.ExternalReference;
        if (string.IsNullOrWhiteSpace(reference))
        {
            // Cross-company recalc not supported in Phase 1 — would scan every symbol; explicit no-op.
            return 0;
        }

        // ExternalReference is the CanonicalExternalCompanyId written by the normalizer into
        // FinancialStatements/MonthlyReports.ExternalCompanyId. Both lookups use the same value,
        // so no heuristic fallback is needed.
        var company = await dbContext.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ExternalCompanyId == reference, cancellationToken);

        if (company is null)
        {
            return 0;
        }

        var symbolIds = await dbContext.Symbols.AsNoTracking()
            .Where(s => s.CompanyId == company.Id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (symbolIds.Count == 0)
        {
            return 0;
        }

        var externalCompanyId = reference;

        // Determine which registered metrics have a calculator AND depend on at least one source
        // metric persisted by this dataset.
        var candidates = metricRegistry
            .GetSupportedMetrics(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .Where(definition => definition.Dependencies.Any(dep =>
                sourceCodes.Contains(dep.MetricCode.Value)))
            .Where(HasRegisteredCalculator)
            .ToArray();

        if (candidates.Length == 0)
        {
            return 0;
        }

        var recomputed = 0;
        foreach (var definition in candidates)
        {
            var unionInputs = new List<MetricInputObservation>();
            foreach (var dependency in definition.Dependencies)
            {
                var depInputs = await TryLoadInputsAsync(externalCompanyId, dependency.MetricCode, cancellationToken);
                unionInputs.AddRange(depInputs);
            }

            if (unionInputs.Count == 0)
            {
                continue;
            }

            var supportedPeriodTypes = definition.SupportedPeriodTypes.ToHashSet();
            var distinctPeriods = unionInputs
                .Where(input => input.Period.EndDate is not null &&
                    supportedPeriodTypes.Contains(input.Period.Type))
                .Select(input => input.Period)
                .Distinct()
                .ToArray();

            if (distinctPeriods.Length == 0)
            {
                continue;
            }

            var policyVersion = SelectActivePolicyVersion(definition.Code, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
            if (policyVersion is null)
            {
                continue;
            }

            foreach (var symbolId in symbolIds)
            {
                foreach (var period in distinctPeriods)
                {
                    var command = new CalculateDerivedMetricCommand(
                        symbolId,
                        definition.Code,
                        policyVersion,
                        period,
                        unionInputs);

                    try
                    {
                        await recalculationCommand.ExecuteAsync([command], cancellationToken);
                        recomputed++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // Per-metric isolation — a single bad period/symbol must not abort the row.
                        logger.LogDebug(
                            exception,
                            "Metric {Metric} for symbol {Symbol} period {Period} failed; continuing.",
                            definition.Code,
                            symbolId,
                            period);
                        // Evict poisoned derived-metric entries from the shared scope: a failed
                        // SaveChanges leaves them tracked as Added/Modified, and every later save
                        // in this scope would replay the same failing insert (observed as
                        // cascading PK_DerivedMetrics violations). The request-row tracking stays
                        // intact.
                        foreach (var entry in dbContext.ChangeTracker.Entries<DerivedMetricRow>()
                            .Where(item => item.State is EntityState.Added or EntityState.Modified)
                            .ToList())
                        {
                            entry.State = EntityState.Detached;
                        }
                    }
                }
            }
        }

        return recomputed;
    }

    private bool HasRegisteredCalculator(FinancialMetricDefinition definition)
    {
        try
        {
            metricRegistry.ResolveCalculator(definition.Code);
            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyCollection<MetricInputObservation>> TryLoadInputsAsync(
        string externalCompanyId,
        MetricCode metricCode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inputReader.LoadAsync(externalCompanyId, metricCode, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            // No input source registered for this dependency (e.g. SHARES_OUTSTANDING, LATEST_PRICE,
            // MARKET_CAP). The dependent metric is skipped; valuation ratios remain owned by their
            // own ingestion path.
            return [];
        }
    }

    private CalculationPolicyVersion? SelectActivePolicyVersion(MetricCode metricCode, DateOnly asOf)
    {
        var policies = policyProvider.GetPolicies(metricCode);
        var active = policies
            .Where(policy => (policy.EffectiveFrom is null || policy.EffectiveFrom <= asOf) &&
                (policy.EffectiveTo is null || policy.EffectiveTo >= asOf))
            .OrderByDescending(policy => policy.EffectiveFrom ?? DateOnly.MinValue)
            .FirstOrDefault();
        return active?.Version;
    }

    private static string LimitError(string message) =>
        message.Length <= 1000 ? message : message[..1000];
}
