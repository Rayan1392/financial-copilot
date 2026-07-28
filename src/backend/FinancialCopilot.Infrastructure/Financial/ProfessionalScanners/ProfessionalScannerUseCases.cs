using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.ProfessionalScanners;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Domain.Financial.ProfessionalScanners;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.ProfessionalScanners;

public sealed class ProfessionalScannerUseCases(
    IProfessionalFilterCatalog catalog,
    ISavedFilterRepository savedFilters,
    IProfessionalScannerEntitlementPolicy entitlements,
    IScannerExecutionService scanner,
    IBillingFacadeHook billing,
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<ProfessionalScannerUseCases> logger) : IProfessionalScannerUseCases
{
    private const int MaximumDateRangeDays = 90;
    private const int TimeoutSeconds = 15;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ProfessionalCatalogPage ListCatalog(ProfessionalCatalogQuery query) => catalog.List(query);
    public ProfessionalFilterDefinition GetFilter(string code, string? version = null) => catalog.Get(code, version);
    public ProfessionalAliasResolution ResolveAlias(string text) => catalog.ResolveAlias(text);

    public async Task<ProfessionalScannerExecutionResult> ExecuteAsync(
        ProfessionalExecuteCommand command, CancellationToken cancellationToken)
    {
        ValidateRequest(command);
        var definition = Resolve(command.FilterCodeOrAlias, command.FilterVersion);
        var parameters = catalog.ValidateParameters(definition, command.Parameters);
        var accessMode = await entitlements.ValidateExecuteAsync(command.Actor, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var toDate = command.ToDate ?? DateOnly.FromDateTime(now.UtcDateTime);
        var fromDate = command.FromDate ?? toDate;
        if (toDate < fromDate || toDate.DayNumber - fromDate.DayNumber > MaximumDateRangeDays)
            throw new ProfessionalScannerValidationException($"Date range must be between 0 and {MaximumDateRangeDays} days.");
        var scope = command.Scope ?? new ProfessionalScannerScope();
        ValidateScope(scope, definition);

        BillingReservationHandle? reservation = null;
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        try
        {
            if (accessMode == ProfessionalAccessMode.Metered)
            {
                reservation = await billing.TryReserveAsync(new BillingReservationRequest(
                    command.CorrelationId, command.Actor.TenantId, command.Actor.ActorId,
                    GovernedProfessionalFilterCatalog.EntitlementCode, command.Actor.UserId,
                    command.Actor.ApiClientId), timeout.Token);
                if (reservation is null)
                    throw new ProfessionalScannerValidationException("Scanner credit reservation was rejected.");
            }

            var result = definition.ExecutionKind == ProfessionalFilterExecutionKind.MetricScanner
                ? await ExecuteMetricAsync(definition, parameters, command, scope, fromDate, toDate, accessMode, timeout.Token)
                : await ExecuteEventsAsync(definition, parameters, command, scope, fromDate, toDate, accessMode, timeout.Token);
            stopwatch.Stop();
            result = result with { Duration = stopwatch.Elapsed };
            if (reservation is not null)
                await billing.FinalizeAsync(reservation, new BillingFinalizationRequest("Completed"), cancellationToken);
            logger.LogInformation(
                "Professional scanner {FilterCode}/{FilterVersion} status={Status} resultCount={ResultCount} latencyMs={LatencyMs} evidenceHash={EvidenceHash} access={AccessMode} source={Source}.",
                definition.Code, definition.Version, result.Status, result.TotalCount,
                stopwatch.Elapsed.TotalMilliseconds, result.EvidenceHash, accessMode, command.Source);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (reservation is not null) await billing.ReleaseAsync(reservation, CancellationToken.None);
            logger.LogWarning("Professional scanner {FilterCode}/{FilterVersion} timed out after {TimeoutSeconds}s.",
                definition.Code, definition.Version, TimeoutSeconds);
            throw new ProfessionalScannerValidationException($"Ready filter execution exceeded the {TimeoutSeconds}-second limit.");
        }
        catch
        {
            if (reservation is not null) await billing.ReleaseAsync(reservation, CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyCollection<SavedFilterDto>> ListSavedAsync(
        CurrentActor actor, int page, int pageSize, CancellationToken cancellationToken) =>
        (await savedFilters.ListAsync(ToActor(actor), Math.Max(1, page), Math.Clamp(pageSize, 1, 100), cancellationToken))
        .Select(Map).ToArray();

    public async Task<SavedFilterDto> SaveAsync(SaveProfessionalFilterCommand command, CancellationToken cancellationToken)
    {
        var definition = Resolve(command.FilterCodeOrAlias, command.FilterVersion);
        var parameters = catalog.ValidateParameters(definition, command.Parameters);
        var actor = ToActor(command.Actor);
        await entitlements.ValidateSaveAsync(command.Actor, await savedFilters.CountAsync(actor, cancellationToken), cancellationToken);
        var value = SavedFilter.Create(actor, command.Name, definition.Code, definition.Version,
            JsonSerializer.Serialize(parameters, JsonOptions), timeProvider.GetUtcNow());
        await savedFilters.SaveAsync(value, cancellationToken);
        return Map(value);
    }

    public async Task<SavedFilterDto> UpdateAsync(UpdateProfessionalFilterCommand command, CancellationToken cancellationToken)
    {
        var actor = ToActor(command.Actor);
        var value = await savedFilters.FindAsync(actor, command.Id, false, cancellationToken)
            ?? throw new ProfessionalScannerValidationException("Saved filter was not found.");
        var definition = Resolve(command.FilterCodeOrAlias, command.FilterVersion);
        var parameters = catalog.ValidateParameters(definition, command.Parameters);
        value.Update(command.ExpectedVersion, command.Name, definition.Code, definition.Version,
            JsonSerializer.Serialize(parameters, JsonOptions), timeProvider.GetUtcNow());
        await savedFilters.SaveAsync(value, cancellationToken);
        return Map(value);
    }

    public async Task DeleteAsync(DeleteProfessionalFilterCommand command, CancellationToken cancellationToken)
    {
        var value = await savedFilters.FindAsync(ToActor(command.Actor), command.Id, false, cancellationToken)
            ?? throw new ProfessionalScannerValidationException("Saved filter was not found.");
        value.Remove(command.ExpectedVersion, timeProvider.GetUtcNow());
        await savedFilters.SaveAsync(value, cancellationToken);
    }

    public async Task<ProfessionalScannerExecutionResult> RunSavedAsync(
        RunSavedProfessionalFilterCommand command, CancellationToken cancellationToken)
    {
        var value = await savedFilters.FindAsync(ToActor(command.Actor), command.Id, false, cancellationToken)
            ?? throw new ProfessionalScannerValidationException("Saved filter was not found.");
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(value.ParametersJson, JsonOptions) ?? [];
        return await ExecuteAsync(new ProfessionalExecuteCommand(command.Actor, value.FilterCode, value.FilterVersion,
            parameters, command.FromDate, command.ToDate, command.Scope, command.Page, command.PageSize,
            command.CorrelationId, command.Source), cancellationToken);
    }

    private async Task<ProfessionalScannerExecutionResult> ExecuteMetricAsync(
        ProfessionalFilterDefinition definition, IReadOnlyDictionary<string, string> parameters,
        ProfessionalExecuteCommand command, ProfessionalScannerScope scope, DateOnly fromDate, DateOnly toDate,
        ProfessionalAccessMode accessMode, CancellationToken cancellationToken)
    {
        var metricCodes = definition.Conditions.Select(item => item.MetricCode).ToArray();
        var evidence = await dbContext.DerivedMetrics.AsNoTracking()
            .Where(row => metricCodes.Contains(row.MetricCode) && row.PeriodEnd <= toDate)
            .GroupBy(row => row.MetricCode)
            .Select(group => new { Metric = group.Key, Count = group.Count(), Freshness = group.Max(row => row.ObservedAt) })
            .ToArrayAsync(cancellationToken);
        var messages = metricCodes.Where(code => evidence.All(item => item.Metric != code))
            .Select(code => $"Required dataset DerivedMetrics/{code} is unavailable.").ToList();
        if (messages.Count > 0)
            return Empty(definition, parameters, scope, fromDate, toDate, command, accessMode,
                ProfessionalExecutionStatus.Unavailable, messages);

        var conditions = definition.Conditions.Select(template => new ScannerCondition(
            new ScannerMetricReference(template.MetricCode, new MetricCode(template.MetricCode), new MetricVersion("v1"),
                new CalculationPolicyVersion($"{template.MetricCode}_v1"),
                Enum.Parse<FiscalPeriodType>(template.PeriodType), null), template.Operator,
            decimal.Parse(parameters[template.ParameterName], CultureInfo.InvariantCulture), FilterOrigin.Explicit,
            $"Governed ready filter {definition.Code}/{definition.Version}" )).ToArray();
        var plan = new ScannerQueryPlan(Guid.NewGuid(), definition.Code, "fa-IR", conditions,
            definition.Conditions.Select(item => new ScannerColumnRequest(item.MetricCode, true)).ToArray(),
            false, null, [], [], timeProvider.GetUtcNow(), $"ready-filter:{definition.Code}:{definition.Version}");
        var table = await scanner.ExecuteAsync(new ScannerExecutionRequest(plan, toDate, command.Page,
            command.PageSize, command.Actor.ActorId.ToString("N"), definition.Code,
            new ScannerUniverseScope(scope.IndustryCode, scope.InstrumentClass)), cancellationToken);
        var byMetric = table.Columns.Where(column => column.MetricCode is not null)
            .ToDictionary(column => column.MetricCode!, column => column.Identifier, StringComparer.OrdinalIgnoreCase);
        var rows = table.Rows.Select((row, index) =>
        {
            var values = definition.Conditions.Select(condition =>
            {
                var cell = byMetric.TryGetValue(condition.MetricCode, out var identifier) && row.Cells.TryGetValue(identifier, out var found)
                    ? found : new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null);
                return (condition, cell);
            }).ToArray();
            var matched = values.Where(value => value.cell.Value.HasValue).Select(value => new ProfessionalMatchedValue(
                value.condition.MetricCode, value.cell.Value!.Value, value.condition.Unit,
                value.cell.TradingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ??
                    value.cell.SourceTimestamp?.ToString("O"), value.cell.SourceTimestamp ?? DateTimeOffset.MinValue)).ToArray();
            var reasons = values.Select(value => new ProfessionalMatchReason(value.condition.MetricCode,
                value.condition.Operator.ToString(), value.cell.Value,
                decimal.Parse(parameters[value.condition.ParameterName], CultureInfo.InvariantCulture), value.condition.Unit,
                $"{value.condition.MetricCode} {OperatorText(value.condition.Operator)} {parameters[value.condition.ParameterName]} {value.condition.Unit}; actual={value.cell.Value?.ToString(CultureInfo.InvariantCulture) ?? "missing"}." )).ToArray();
            var freshness = matched.Length == 0 ? DateTimeOffset.MinValue : matched.Min(value => value.SourceFreshnessUtc);
            return new ProfessionalScannerResultRow(row.ExternalCompanyId ?? row.SymbolCode, row.SymbolCode,
                row.CompanyName, (table.ExecutionFacts.Page - 1) * table.ExecutionFacts.PageSize + index + 1,
                matched, reasons, (decimal)row.Score, freshness,
                $"scanner:{definition.Code}:{definition.Version}:{row.ExternalCompanyId ?? row.SymbolCode}");
        }).ToArray();
        var freshest = evidence.Min(item => item.Freshness);
        var status = rows.Length == 0 ? ProfessionalExecutionStatus.Empty :
            timeProvider.GetUtcNow() - freshest > TimeSpan.FromDays(45) ? ProfessionalExecutionStatus.Stale :
            table.MissingDataWarnings.Count > 0 ? ProfessionalExecutionStatus.Partial : ProfessionalExecutionStatus.Complete;
        messages.AddRange(table.MissingDataWarnings);
        if (status == ProfessionalExecutionStatus.Stale) messages.Add($"Required metric evidence is stale; oldest dataset freshness is {freshest:O}.");
        return Build(definition, parameters, scope, fromDate, toDate, rows, table.ExecutionFacts.MatchingSymbolCount,
            table.ExecutionFacts.Page, table.ExecutionFacts.PageSize, table.ExecutionFacts.TotalPages,
            status, accessMode, messages, command.CorrelationId);
    }

    private async Task<ProfessionalScannerExecutionResult> ExecuteEventsAsync(
        ProfessionalFilterDefinition definition, IReadOnlyDictionary<string, string> parameters,
        ProfessionalExecuteCommand command, ProfessionalScannerScope scope, DateOnly fromDate, DateOnly toDate,
        ProfessionalAccessMode accessMode, CancellationToken cancellationToken)
    {
        var type = definition.InsightType!.Value.ToString();
        var minimumImportance = decimal.Parse(parameters["minimumImportance"], CultureInfo.InvariantCulture);
        var start = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var query = dbContext.InsightEvents.AsNoTracking().Where(row => row.InsightType == type &&
            row.DetectedAtUtc >= start && row.DetectedAtUtc < end && row.ImportanceScore >= minimumImportance);
        if (!string.IsNullOrWhiteSpace(scope.IndustryCode)) query = query.Where(row => row.IndustryCode == scope.IndustryCode);
        if (!string.IsNullOrWhiteSpace(scope.InstrumentClass))
        {
            var instrumentClass = scope.InstrumentClass;
            query = query.Where(row => dbContext.Companies.Any(company => company.ExternalCompanyId == row.ExternalCompanyId &&
                dbContext.TradingInstruments.Any(instrument => instrument.NormalizedCompanyId == company.Id &&
                    instrument.IsActive && instrument.InstrumentKind == instrumentClass)));
        }
        var total = await query.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(command.PageSize, 1, 100);
        var pages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
        var page = Math.Min(Math.Max(1, command.Page), pages);
        var events = await query.OrderByDescending(row => row.ImportanceScore)
            .ThenByDescending(row => row.DetectedAtUtc).ThenBy(row => row.Symbol)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        var rows = events.Select((row, index) => new ProfessionalScannerResultRow(row.ExternalCompanyId, row.Symbol,
            null, (page - 1) * pageSize + index + 1,
            [new("IMPORTANCE_SCORE", row.ImportanceScore, "score", row.SourcePeriod, row.DetectedAtUtc),
             new("CONFIDENCE_SCORE", row.ConfidenceScore, "score", row.SourcePeriod, row.DetectedAtUtc)],
            [new(type, "GreaterThanOrEqual", row.ImportanceScore, minimumImportance, "score", row.Reason)],
            row.ImportanceScore, row.DetectedAtUtc, $"insight-event:{row.Id:N}:{row.DeduplicationKey}")).ToArray();
        var datasetExists = total > 0 || await dbContext.InsightEvents.AsNoTracking().AnyAsync(row => row.InsightType == type, cancellationToken);
        var status = !datasetExists ? ProfessionalExecutionStatus.Unavailable : rows.Length == 0
            ? ProfessionalExecutionStatus.Empty : ProfessionalExecutionStatus.Complete;
        var messages = !datasetExists ? new[] { $"Required dataset InsightEvents/{type} is unavailable." } : [];
        return Build(definition, parameters, scope, fromDate, toDate, rows, total, page, pageSize, pages,
            status, accessMode, messages, command.CorrelationId);
    }

    private ProfessionalScannerExecutionResult Empty(ProfessionalFilterDefinition definition,
        IReadOnlyDictionary<string, string> parameters, ProfessionalScannerScope scope, DateOnly fromDate,
        DateOnly toDate, ProfessionalExecuteCommand command, ProfessionalAccessMode accessMode,
        ProfessionalExecutionStatus status, IReadOnlyCollection<string> messages) =>
        Build(definition, parameters, scope, fromDate, toDate, [], 0, 1, Math.Clamp(command.PageSize, 1, 100), 1,
            status, accessMode, messages, command.CorrelationId);

    private ProfessionalScannerExecutionResult Build(ProfessionalFilterDefinition definition,
        IReadOnlyDictionary<string, string> parameters, ProfessionalScannerScope scope, DateOnly fromDate,
        DateOnly toDate, IReadOnlyCollection<ProfessionalScannerResultRow> rows, int total, int page,
        int pageSize, int pages, ProfessionalExecutionStatus status, ProfessionalAccessMode accessMode,
        IReadOnlyCollection<string> messages, string correlationId)
    {
        var canonical = JsonSerializer.Serialize(new { definition.Code, definition.Version, parameters,
            scope, fromDate, toDate, rows = rows.Select(row => new { row.ExternalCompanyId, row.Symbol, row.Rank,
                row.Score, row.EvidenceReference, row.MatchedValues, row.Reasons }) }, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new ProfessionalScannerExecutionResult(definition.Code, definition.Version, status, accessMode,
            parameters, scope, fromDate, toDate, rows, page, pageSize, total, pages, hash,
            timeProvider.GetUtcNow(), TimeSpan.Zero, messages, correlationId, definition.Ranking, definition.TieBreaker);
    }

    private ProfessionalFilterDefinition Resolve(string codeOrAlias, string? version)
    {
        try { return catalog.Get(codeOrAlias, version); }
        catch (ProfessionalScannerValidationException) when (version is null)
        {
            var resolved = catalog.ResolveAlias(codeOrAlias);
            return resolved.Resolved ? resolved.Definition! : throw new ProfessionalScannerValidationException(
                resolved.Message ?? "Ready filter alias could not be resolved.");
        }
    }

    private static void ValidateRequest(ProfessionalExecuteCommand command)
    {
        if (command.Actor.ActorId == Guid.Empty || command.Actor.TenantId == Guid.Empty)
            throw new ProfessionalScannerValidationException("Authenticated actor is required.");
        if (string.IsNullOrWhiteSpace(command.CorrelationId) || command.CorrelationId.Length > 128)
            throw new ProfessionalScannerValidationException("A bounded correlation id is required.");
        if (command.Page < 1 || command.PageSize is < 1 or > 100)
            throw new ProfessionalScannerValidationException("Page must be positive and pageSize must be between 1 and 100.");
    }

    private static void ValidateScope(ProfessionalScannerScope scope, ProfessionalFilterDefinition definition)
    {
        if (scope.IndustryCode?.Length > 64 || scope.InstrumentClass?.Length > 64)
            throw new ProfessionalScannerValidationException("Industry and instrument scope values are limited to 64 characters.");
        if (definition.Category == ProfessionalFilterCategory.Industry && string.IsNullOrWhiteSpace(scope.IndustryCode))
            throw new ProfessionalScannerValidationException("Industry-scoped filters require industryCode.");
    }

    private static string OperatorText(ConditionOperator value) => value switch
    {
        ConditionOperator.GreaterThan => ">", ConditionOperator.GreaterThanOrEqual => ">=",
        ConditionOperator.LessThan => "<", ConditionOperator.LessThanOrEqual => "<=",
        ConditionOperator.Equal => "=", ConditionOperator.NotEqual => "!=", _ => value.ToString()
    };

    private static SavedFilterActor ToActor(CurrentActor actor) =>
        new(actor.TenantId, actor.ActorId, actor.ActorType.ToString());

    private static SavedFilterDto Map(SavedFilter value) => new(value.Id, value.Name, value.FilterCode,
        value.FilterVersion, JsonSerializer.Deserialize<Dictionary<string, string>>(value.ParametersJson, JsonOptions) ?? [],
        value.Version, value.CreatedAtUtc, value.UpdatedAtUtc);
}
