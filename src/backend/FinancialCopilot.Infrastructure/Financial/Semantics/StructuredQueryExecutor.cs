using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Text;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FinancialCopilot.Infrastructure.Financial.Semantics;

public sealed class StructuredQueryExecutionOptions
{
    public const string SectionName = "Feature141:QueryExecution";
    public bool Enabled { get; set; }
    public bool ExplicitDevelopmentOptIn { get; set; }
    public string EnvironmentName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string DesignatedDatabase { get; set; } = string.Empty;
    public string ReadOnlyRole { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 5;
    public int MaximumRows { get; set; } = 100;
    public int MaximumResultBytes { get; set; } = 1_000_000;
    public int MaximumConcurrency { get; set; } = 2;
    public string DevelopmentScopeId { get; set; } = string.Empty;
    public string AuthorizationProvenance { get; set; } = string.Empty;
    public string[] ReviewedSourceCatalogIds { get; set; } = [];
    public string[] ReviewedRelationshipCatalogIds { get; set; } = [];
    public string[] ReviewedPhysicalObjectCatalogIds { get; set; } = [];
    public string[] ReviewedTenantCompanyBindings { get; set; } = [];
    public string[] ReviewedTenantCompanyDiscoveryScopes { get; set; } = [];
    public string[] ReviewedOperations { get; set; } = [];
}

/// <summary>Rejects incomplete or non-Development executor configuration before the host starts.</summary>
public sealed class StructuredQueryExecutionOptionsValidator(IHostEnvironment hostEnvironment)
    : IValidateOptions<StructuredQueryExecutionOptions>
{
    public ValidateOptionsResult Validate(string? name, StructuredQueryExecutionOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        var errors = new List<string>();
        if (!hostEnvironment.IsDevelopment()
            || !string.Equals(options.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || !options.ExplicitDevelopmentOptIn)
            errors.Add("Feature141 execution requires explicit Development-only opt-in.");
        if (string.IsNullOrWhiteSpace(options.ConnectionString)
            || string.IsNullOrWhiteSpace(options.DesignatedDatabase)
            || string.IsNullOrWhiteSpace(options.ReadOnlyRole))
            errors.Add("Feature141 requires an independent restricted-role connection, database, and role.");
        else
        {
            try
            {
                var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString);
                if (string.IsNullOrWhiteSpace(connection.Host)
                    || !string.Equals(connection.Database, options.DesignatedDatabase, StringComparison.Ordinal)
                    || !string.Equals(connection.Username, options.ReadOnlyRole, StringComparison.Ordinal))
                    errors.Add("Feature141 connection host, database, and role must match the explicit Development scope.");
            }
            catch (ArgumentException)
            {
                errors.Add("Feature141 connection string is invalid.");
            }
        }
        if (string.IsNullOrWhiteSpace(options.DevelopmentScopeId)
            || string.IsNullOrWhiteSpace(options.AuthorizationProvenance))
            errors.Add("Feature141 requires a reviewed Development scope and authorization provenance.");
        if (options.ReviewedSourceCatalogIds.Length == 0
            || options.ReviewedRelationshipCatalogIds.Length == 0
            || options.ReviewedPhysicalObjectCatalogIds.Length == 0)
            errors.Add("Feature141 requires explicit source, relationship, and physical-object allowlists.");
        if (options.ReviewedTenantCompanyBindings.Length == 0
            || options.ReviewedTenantCompanyBindings.Any(binding => !TryParseTenantCompanyBinding(binding, out _, out _)))
            errors.Add("Feature141 requires exact approved tenant/company UUID pairs.");
        if (options.ReviewedTenantCompanyDiscoveryScopes.Any(binding =>
                !TryParseTenantCompanyDiscoveryScope(binding, out _, out _, out _)))
            errors.Add("Feature141 company discovery scope bindings must use tenant-id|scope-id|approval-reference.");
        if (!options.ReviewedOperations.SequenceEqual([nameof(FinancialValueOperation.DirectValueRetrieval)], StringComparer.Ordinal))
            errors.Add("Feature141 currently permits only the reviewed DirectValueRetrieval operation.");
        if (options.CommandTimeoutSeconds is < 1 or > 30 || options.MaximumRows is < 1 or > 100
            || options.MaximumResultBytes is < 1 or > 1_000_000 || options.MaximumConcurrency is < 1 or > 4)
            errors.Add("Feature141 timeout and result bounds are outside supported limits.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    internal static bool TryParseTenantCompanyBinding(string? binding, out Guid tenantId, out Guid companyId)
    {
        tenantId = Guid.Empty;
        companyId = Guid.Empty;
        var parts = binding?.Split(':', StringSplitOptions.TrimEntries);
        return parts is { Length: 2 }
            && Guid.TryParse(parts[0], out tenantId) && tenantId != Guid.Empty
            && Guid.TryParse(parts[1], out companyId) && companyId != Guid.Empty;
    }

    internal static bool TryParseTenantCompanyDiscoveryScope(string? binding, out Guid tenantId,
        out string scopeId, out string approvalReference)
    {
        tenantId = Guid.Empty;
        scopeId = string.Empty;
        approvalReference = string.Empty;
        var parts = binding?.Split('|', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 3 } || !Guid.TryParse(parts[0], out tenantId) || tenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[2])) return false;
        scopeId = parts[1];
        approvalReference = parts[2];
        return true;
    }
}

/// <summary>Issues grants only for a configured, explicitly opted-in development test scope.</summary>
public sealed class DevelopmentStructuredQueryAuthorizer(IOptions<StructuredQueryExecutionOptions> options, IHostEnvironment hostEnvironment)
    : IDevelopmentStructuredQueryAuthorizer
{
    private readonly StructuredQueryExecutionOptions _options = options.Value;

    public StructuredQueryExecutionGrant? Authorize(CompiledReadOnlyQuery query, Guid tenantId)
    {
        if (!_options.Enabled || !_options.ExplicitDevelopmentOptIn || !hostEnvironment.IsDevelopment()
            || !string.Equals(_options.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || tenantId == Guid.Empty || query.TenantId != tenantId
            || string.IsNullOrWhiteSpace(_options.DesignatedDatabase)
            || string.IsNullOrWhiteSpace(_options.ReadOnlyRole)
            || string.IsNullOrWhiteSpace(_options.DevelopmentScopeId)
            || string.IsNullOrWhiteSpace(_options.AuthorizationProvenance)
            || !query.RetrievalComplete
            || !query.SemanticReviewApproved || !query.QueryAuthorized
            || query.SourceCatalogIds.Count == 0)
            return null;
        var allowedSources = _options.ReviewedSourceCatalogIds.ToHashSet(StringComparer.Ordinal);
        var allowedRelationships = _options.ReviewedRelationshipCatalogIds.ToHashSet(StringComparer.Ordinal);
        var allowedObjects = _options.ReviewedPhysicalObjectCatalogIds.ToHashSet(StringComparer.Ordinal);
        var allowedBindings = _options.ReviewedTenantCompanyBindings
            .Select(value => StructuredQueryExecutionOptionsValidator.TryParseTenantCompanyBinding(value, out var allowedTenant, out var allowedCompany)
                ? (TenantId: allowedTenant, CompanyId: allowedCompany) : (TenantId: Guid.Empty, CompanyId: Guid.Empty))
            .Where(binding => binding.TenantId != Guid.Empty && binding.CompanyId != Guid.Empty).ToHashSet();
        var discoveryScopes = _options.ReviewedTenantCompanyDiscoveryScopes
            .Select(value => StructuredQueryExecutionOptionsValidator.TryParseTenantCompanyDiscoveryScope(value,
                out var allowedTenant, out var allowedScopeId, out var allowedApproval)
                ? (TenantId: allowedTenant, ScopeId: allowedScopeId, Approval: allowedApproval)
                : (TenantId: Guid.Empty, ScopeId: string.Empty, Approval: string.Empty)).ToArray();
        var discoveryScopeAuthorized = query.IsPopulationDiscoveryQuery && discoveryScopes.Any(scope =>
            scope.TenantId == tenantId && scope.ScopeId == query.CompanyDiscoveryScopeId
            && scope.Approval == query.CompanyDiscoveryApprovalReference);
        var allowedOperations = _options.ReviewedOperations
            .Select(value => Enum.TryParse<FinancialValueOperation>(value, ignoreCase: false, out var operation)
                ? operation : (FinancialValueOperation?)null)
            .Where(operation => operation.HasValue).Select(operation => operation!.Value).ToHashSet();
        var companyIds = query.CompanyIds.Distinct().ToArray();
        var companyScopeAuthorized = query.IsPopulationDiscoveryQuery
            ? companyIds.Length == 0 && discoveryScopeAuthorized
                && allowedBindings.Any(binding => binding.TenantId == tenantId)
            : companyIds.Length > 0 && companyIds.All(companyId => companyId != Guid.Empty
                && allowedBindings.Contains((tenantId, companyId)));
        if (!query.SourceCatalogIds.All(allowedSources.Contains)
            || !query.RelationshipCatalogIds.All(allowedRelationships.Contains)
            || !query.PhysicalObjectCatalogIds.All(allowedObjects.Contains)
            || !companyScopeAuthorized
            || !allowedOperations.Contains(query.Operation)) return null;
        var provenance = $"{_options.DevelopmentScopeId}:{_options.AuthorizationProvenance}";
        var grantCompanyIds = query.IsPopulationDiscoveryQuery
            ? allowedBindings.Where(binding => binding.TenantId == tenantId).Select(binding => binding.CompanyId).ToHashSet()
            : companyIds.ToHashSet();
        return new StructuredQueryExecutionGrant(tenantId, _options.EnvironmentName, _options.DesignatedDatabase,
            _options.ReadOnlyRole, query.CatalogHash, allowedSources, allowedRelationships, allowedObjects,
            grantCompanyIds, allowedOperations, provenance, DateTimeOffset.UtcNow.AddMinutes(5),
            query.IsPopulationDiscoveryQuery ? query.CompanyDiscoveryScopeId : null);
    }
}

/// <summary>
/// PostgreSQL executor for compiler-sealed SELECT queries. Defaults to disabled and refuses any
/// database/role/grant mismatch before opening a connection. The DB principal must separately have
/// SELECT-only grants on the reviewed view/table set.
/// </summary>
public sealed class NpgsqlReadOnlyStructuredQueryExecutor : IReadOnlyStructuredQueryExecutor
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private readonly StructuredQueryExecutionOptions _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<NpgsqlReadOnlyStructuredQueryExecutor> _logger;

    public NpgsqlReadOnlyStructuredQueryExecutor(IOptions<StructuredQueryExecutionOptions> options,
        ILogger<NpgsqlReadOnlyStructuredQueryExecutor> logger, IHostEnvironment hostEnvironment)
    {
        _options = options.Value;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
    }

    public async Task<StructuredQueryExecutionResult> ExecuteAsync(CompiledReadOnlyQuery query,
        StructuredQueryExecutionGrant grant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(grant);
        if (!IsAuthorized(query, grant)) return Result(StructuredQueryExecutionStatus.Unauthorized, query, grant, "execution_authorization_rejected");

        var gate = Gates.GetOrAdd(_options.DesignatedDatabase, _ => new SemaphoreSlim(_options.MaximumConcurrency, _options.MaximumConcurrency));
        var timer = Stopwatch.StartNew();
        var acquired = false;
        try
        {
            await gate.WaitAsync(cancellationToken);
            acquired = true;
            var builder = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
            if (!string.Equals(builder.Database, _options.DesignatedDatabase, StringComparison.Ordinal)
                || !string.Equals(builder.Username, _options.ReadOnlyRole, StringComparison.Ordinal))
                return Result(StructuredQueryExecutionStatus.Unauthorized, query, grant, "database_role_mismatch");

            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
                await readOnly.ExecuteNonQueryAsync(cancellationToken);
            var timeoutMilliseconds = Math.Clamp(_options.CommandTimeoutSeconds, 1, 30) * 1000;
            await using (var timeout = new NpgsqlCommand($"SET LOCAL statement_timeout = '{timeoutMilliseconds}ms'", connection, transaction))
                await timeout.ExecuteNonQueryAsync(cancellationToken);
            await using (var searchPath = new NpgsqlCommand("SET LOCAL search_path TO pg_catalog, public", connection, transaction))
                await searchPath.ExecuteNonQueryAsync(cancellationToken);
            await using (var identity = new NpgsqlCommand("SELECT current_database(), current_user, current_setting('transaction_read_only')", connection, transaction))
            await using (var reader = await identity.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)
                    || !string.Equals(reader.GetString(0), _options.DesignatedDatabase, StringComparison.Ordinal)
                    || !string.Equals(reader.GetString(1), _options.ReadOnlyRole, StringComparison.Ordinal)
                    || !string.Equals(reader.GetString(2), "on", StringComparison.Ordinal))
                    return Result(StructuredQueryExecutionStatus.Unauthorized, query, grant, "database_read_only_identity_check_failed");
            }

            await using var command = new NpgsqlCommand(query.Sql, connection, transaction)
            {
                CommandTimeout = Math.Clamp(_options.CommandTimeoutSeconds, 1, 30)
            };
            foreach (var parameter in query.Parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            if (query.IsPopulationDiscoveryQuery)
                command.Parameters.AddWithValue("company_ids", grant.AllowedCompanyIds.ToArray());
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            long bytes = 0;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        object? value = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                        bytes += EstimateBytes(value);
                        if (bytes > _options.MaximumResultBytes)
                            return Result(StructuredQueryExecutionStatus.Truncated, query, grant, "result_byte_limit_exceeded", rows, timer.Elapsed, bytes);
                        row[reader.GetName(i)] = value;
                    }
                    rows.Add(row);
                    if (rows.Count > Math.Min(_options.MaximumRows, query.MaximumRows))
                        return Result(StructuredQueryExecutionStatus.Truncated, query, grant, "result_row_limit_exceeded", rows.Take(query.MaximumRows).ToArray(), timer.Elapsed, bytes);
                }
            }
            await transaction.CommitAsync(cancellationToken);
            timer.Stop();
            if (query.IsMonthlyValueQuery && rows.Any(row => MoreThanOne(row, "SamePeriodCandidateCount")
                    || MoreThanOne(row, "EligibleReportCount") || Positive(row, "DuplicateProductCount")
                    || MoreThanOne(row, "UnitCompatibilityCount") || Positive(row, "InvalidMultiplicityCount")))
                return Result(StructuredQueryExecutionStatus.Ambiguous, query, grant,
                    "monthly_product_period_has_multiple_source_rows", rows, timer.Elapsed, bytes);
            if (query.IsPopulationDiscoveryQuery && rows.Any(row => MoreThanOne(row, "SamePeriodCandidateCount")
                    || Positive(row, "InvalidMultiplicityCount")
                    || row.GetValueOrDefault("SourceMultiplicity") is not null
                        && Convert.ToInt32(row["SourceMultiplicity"], System.Globalization.CultureInfo.InvariantCulture) != 1))
                return Result(StructuredQueryExecutionStatus.Ambiguous, query, grant,
                    "population_discovery_conflicting_product_observations", rows, timer.Elapsed, bytes);
            if (query.IsMonthlyValueQuery)
            {
                var comparisonFailure = rows.Select(row => row.GetValueOrDefault("ComparisonStatus") as string)
                    .FirstOrDefault(value => value is "missing-comparison-period" or "zero-denominator" or "negative-baseline-unsupported");
                if (rows.Any(row => row.GetValueOrDefault("ComparisonStatus") as string == "period-window-mismatch"))
                    return Result(StructuredQueryExecutionStatus.Ambiguous, query, grant,
                        "monthly_comparison_reporting_windows_not_comparable", rows, timer.Elapsed, bytes);
                if (comparisonFailure is not null)
                    return Result(StructuredQueryExecutionStatus.Incomplete, query, grant,
                        comparisonFailure switch
                        {
                            "missing-comparison-period" => "monthly_comparison_period_missing",
                            "zero-denominator" => "monthly_growth_zero_denominator",
                            _ => "monthly_growth_negative_baseline_unsupported"
                        }, rows, timer.Elapsed, bytes);
            }
            var populationCoverageIncomplete = query.IsPopulationDiscoveryQuery
                && (!query.CompanyDiscoveryCoverageComplete
                    || !string.Equals(query.ProductMappingCoverageStatus, "complete", StringComparison.OrdinalIgnoreCase));
            var status = populationCoverageIncomplete
                ? StructuredQueryExecutionStatus.Incomplete
                : rows.Count == 0 ? StructuredQueryExecutionStatus.NoData : StructuredQueryExecutionStatus.Succeeded;
            if (populationCoverageIncomplete)
                return Result(status, query, grant, !query.CompanyDiscoveryCoverageComplete
                    ? "population_discovery_authorized_company_coverage_incomplete"
                    : "population_discovery_product_alias_coverage_incomplete", rows, timer.Elapsed, bytes);
            if (query.ResultShape == PlanPresentation.Scalar && rows.Count > 1)
            {
                if (query.IsMonthlyValueQuery)
                    return Result(StructuredQueryExecutionStatus.Ambiguous, query, grant,
                        "monthly_product_period_has_multiple_source_rows", rows, timer.Elapsed, bytes);
                var resolution = ResolveStatementTie(rows);
                if (resolution.Status != StructuredQueryExecutionStatus.Succeeded)
                    return Result(resolution.Status, query, grant, resolution.DiagnosticCode, resolution.Rows, timer.Elapsed, bytes);
                rows = resolution.Rows.ToList();
            }
            return Result(status, query, grant, null, rows, timer.Elapsed, bytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(StructuredQueryExecutionStatus.Cancelled, query, grant, "query_cancelled", elapsed: timer.Elapsed);
        }
        catch (PostgresException ex) when (ex.SqlState == "57014")
        {
            _logger.LogWarning("Feature141 structured query timed out. Fingerprint={Fingerprint}", query.QueryFingerprint);
            return Result(StructuredQueryExecutionStatus.TimedOut, query, grant, "database_statement_timeout", elapsed: timer.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Feature141 structured query failed. Fingerprint={Fingerprint} ErrorType={ErrorType}",
                query.QueryFingerprint, ex.GetType().Name);
            return Result(StructuredQueryExecutionStatus.Failed, query, grant, "database_query_failed", elapsed: timer.Elapsed);
        }
        finally
        {
            if (acquired) gate.Release();
        }
    }

    private bool IsAuthorized(CompiledReadOnlyQuery query, StructuredQueryExecutionGrant grant) =>
        _options.Enabled
        && _options.ExplicitDevelopmentOptIn
        && _hostEnvironment.IsDevelopment()
        && string.Equals(_options.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
        && string.Equals(grant.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(_options.DesignatedDatabase)
        && string.Equals(grant.DatabaseName, _options.DesignatedDatabase, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(_options.ReadOnlyRole)
        && string.Equals(grant.DatabaseRole, _options.ReadOnlyRole, StringComparison.Ordinal)
        && grant.TenantId != Guid.Empty
        && grant.TenantId == query.TenantId
        && grant.ExpiresAt > DateTimeOffset.UtcNow
        && !string.IsNullOrWhiteSpace(grant.Provenance)
        && string.Equals(grant.CatalogHash, query.CatalogHash, StringComparison.Ordinal)
        && query.RetrievalComplete
        && query.SemanticReviewApproved
        && query.QueryAuthorized
        && query.SourceCatalogIds.Count > 0
        && query.SourceCatalogIds.All(grant.AllowedSourceCatalogIds.Contains)
        && query.RelationshipCatalogIds.Count > 0
        && query.RelationshipCatalogIds.All(grant.AllowedRelationshipCatalogIds.Contains)
        && query.PhysicalObjectCatalogIds.Count > 0
        && query.PhysicalObjectCatalogIds.All(grant.AllowedPhysicalObjectCatalogIds.Contains)
        && (query.IsPopulationDiscoveryQuery
            ? query.CompanyIds.Count == 0 && grant.AllowedCompanyIds.Count > 0
                && string.Equals(query.CompanyDiscoveryScopeId, grant.CompanyDiscoveryScopeId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(grant.CompanyDiscoveryScopeId)
            : query.CompanyIds.Count > 0
                && query.CompanyIds.All(companyId => companyId != Guid.Empty && grant.AllowedCompanyIds.Contains(companyId)))
        && grant.AllowedOperations.Contains(query.Operation)
        && query.Operation is FinancialValueOperation.DirectValueRetrieval or FinancialValueOperation.ApprovedPeriodComparison
        && query.MaximumRows is > 0 and <= 100
        && _options.MaximumRows is > 0 and <= 100
        && _options.MaximumResultBytes is > 0 and <= 1_000_000
        && _options.MaximumConcurrency is > 0 and <= 4
        && !string.IsNullOrWhiteSpace(_options.ConnectionString);

    private static long EstimateBytes(object? value) => value switch
    {
        null or DBNull => 1,
        string text => Encoding.UTF8.GetByteCount(text),
        byte[] bytes => bytes.Length,
        _ => 32
    };

    private static bool MoreThanOne(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null
        && Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) > 1;

    private static bool Positive(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null
        && Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) > 0;

    private static DateTime? ReportingPeriod(object? value) => value switch
    {
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        DateTime dateTime => dateTime,
        DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
        string text when DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) => parsed,
        _ => null
    };

    internal static (StructuredQueryExecutionStatus Status, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
        string? DiagnosticCode) ResolveStatementTie(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return (StructuredQueryExecutionStatus.NoData, [], null);
        if (rows.Count == 1) return (rows[0].GetValueOrDefault("Value") is null
            ? StructuredQueryExecutionStatus.NoData : StructuredQueryExecutionStatus.Succeeded, rows, null);
        var periods = rows.Select(row => ReportingPeriod(row.GetValueOrDefault("PeriodEnd"))).ToArray();
        if (periods.Any(period => !period.HasValue))
            return (StructuredQueryExecutionStatus.Ambiguous, rows, "reporting_period_evidence_unavailable");
        var latestEnd = periods.Max(period => period!.Value);
        var latest = rows.Where((_, index) => periods[index] == latestEnd).ToArray();
        if (latest.Length == 0) return (StructuredQueryExecutionStatus.NoData, [], null);
        // Period type and start are selection dimensions; audit, representation and revision are
        // competing statement identities within one otherwise comparable period.
        var periodScopes = latest.Select(row => (Start: row.GetValueOrDefault("PeriodStart"),
                Type: row.GetValueOrDefault("PeriodType"), Scope: row.GetValueOrDefault("IsComposing")))
            .Distinct().ToArray();
        if (periodScopes.Length != 1)
            return (StructuredQueryExecutionStatus.Ambiguous, latest, "different_reporting_period_scopes");
        if (latest.Any(row => row.GetValueOrDefault("Value") is null))
            return (StructuredQueryExecutionStatus.Incomplete, latest, "eligible_statement_observation_missing");
        var values = latest.Select(row => row.GetValueOrDefault("Value")).ToArray();
        if (values.Skip(1).Any(value => !Equals(value, values[0])))
            return (StructuredQueryExecutionStatus.Ambiguous, latest, "requested_item_values_conflict");
        var resolved = new Dictionary<string, object?>(latest[0], StringComparer.Ordinal);
        // A representative row must not imply that its statement was preferred. The complete
        // competing identities are emitted together and the singular identity is removed.
        resolved.Remove("StatementId");
        resolved.Remove("IsAudited");
        resolved.Remove("IsRepresented");
        resolved.Remove("ExternalStatementId");
        resolved.Remove("SourcePayloadChecksum");
        resolved.Remove("PublishedAt");
        resolved.Remove("LastSynchronizedAt");
        resolved.Remove("WarningsJson");
        resolved.Remove("LogicalVendor");
        resolved.Remove("SourceMode");
        resolved["StatementIdentities"] = latest.Select(row => row.GetValueOrDefault("StatementId")).ToArray();
        resolved["SameValueTieResolved"] = true;
        resolved["MatchingStatementCount"] = latest.Length;
        return (StructuredQueryExecutionStatus.Succeeded, [resolved], null);
    }

    private static StructuredQueryExecutionResult Result(StructuredQueryExecutionStatus status,
        CompiledReadOnlyQuery query, StructuredQueryExecutionGrant grant, string? diagnostic,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows = null, TimeSpan? elapsed = null, long? bytes = null) =>
        new(status, rows ?? [], query.SourceCatalogIds, query.CatalogVersion, query.CatalogHash,
            query.QueryFingerprint, elapsed, bytes, grant.Provenance, [], diagnostic,
            query.EffectiveUnit, query.EffectiveUnitStatus, query.EffectiveUnitProvenance);
}
