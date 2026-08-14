using System.Globalization;
using System.Text;
using System.Collections.Concurrent;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum Feature126RunState { Disabled, ActivationGuardRejected, CurrentDaySucceededNoOp, Success, PartialSuccess, Failed, Cancelled, Timeout, LeaseLost, HandoffFailed }
public enum Feature126LeaseStatus { NotAttempted, Owned, Recovered, Lost, Contended }
public enum Feature126HandoffStatus { NotApplicable, Succeeded, Failed }

public static class Feature126FailureCodes
{
    public static readonly string[] Ordered = ["NoData", "Timeout", "RateLimited", "RemoteServerFailure", "AuthenticationFailed", "InvalidPayload", "ResponseTooLarge", "IdentityMismatch", "InvalidValue", "InvalidNonPositiveInput", "InputQualityFailure", "MappingFailed", "MissingAdmissionIdentity", "PersistenceRejected", "LeaseContended", "MissingConfigurationRevision", "MissingDeploymentIdentifier", "ConflictingOwnerActivation", "NetworkFailure", "LeaseLost", "HandoffFailed", "Cancelled", "UnexpectedFailure"];

    public static string Canonicalize(string? code) => code switch
    {
        null or "" => "UnexpectedFailure",
        "InvalidNonPositiveInput" => "InvalidNonPositiveInput",
        "PersistenceRejected" => "PersistenceRejected",
        "MissingAdmissionIdentity" => "MissingAdmissionIdentity",
        "LeaseContended" => "LeaseContended",
        "MissingConfigurationRevision" => "MissingConfigurationRevision",
        "MissingDeploymentIdentifier" => "MissingDeploymentIdentifier",
        "ConflictingOwnerActivation" => "ConflictingOwnerActivation",
        _ when Ordered.Contains(code, StringComparer.Ordinal) => code,
        _ => "UnexpectedFailure"
    };
}

public sealed record Feature126MetricCounts(long? Accepted, long? Unchanged, long? Failed);
public sealed record Feature126EndpointCounts(long? Attempted, long? Succeeded, long? Failed);

public sealed record Feature126OperationalSummary(
    string CorrelationId, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    long? DurationMilliseconds, string TehranDate, bool Enabled, Feature126RunState RunState,
    Feature126LeaseStatus LeaseStatus, bool RecoveredLease, long? EligibleCompanies,
    long? AttemptedCompanies, long? SucceededCompanies, long? FailedCompanies,
    IReadOnlyDictionary<string, Feature126MetricCounts> MetricCounts,
    IReadOnlyDictionary<string, long?> FailureCodeCounts,
    IReadOnlyDictionary<string, Feature126EndpointCounts> EndpointCounts,
    string TerminationCode, Feature126HandoffStatus HandoffStatus, long? PublishedCount,
    long? InconclusiveCount);

public sealed record Feature126OperationalSummaryPublication(
    Feature126OperationalSummary Summary,
    byte[] CanonicalJson);

public interface IFeature126OperationalSummarySink
{
    void Publish(Feature126OperationalSummary summary);
    IReadOnlyList<Feature126OperationalSummaryPublication> ReadRecent();
}

public sealed class Feature126OperationalSummaryRegistry : IFeature126OperationalSummarySink
{
    private const int Capacity = 256;
    private readonly ConcurrentQueue<Feature126OperationalSummaryPublication> publications = new();

    public void Publish(Feature126OperationalSummary summary)
    {
        publications.Enqueue(new(summary, Feature126CanonicalJsonSerializer.Serialize(summary)));
        while (publications.Count > Capacity)
            publications.TryDequeue(out _);
    }

    public IReadOnlyList<Feature126OperationalSummaryPublication> ReadRecent() => publications.ToArray();
}

public static class Feature126OperationalSummaryFactory
{
    public static Feature126OperationalSummary Create(
        string correlationId, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc,
        DateOnly tehranDate, bool enabled, Feature126RunState state,
        Feature126LeaseStatus leaseStatus = Feature126LeaseStatus.NotAttempted,
        bool recoveredLease = false, long? eligibleCompanies = null, long? attemptedCompanies = null,
        long? succeededCompanies = null, long? failedCompanies = null,
        IReadOnlyDictionary<string, Feature126MetricCounts>? metrics = null,
        IReadOnlyDictionary<string, long?>? failures = null,
        IReadOnlyDictionary<string, Feature126EndpointCounts>? endpoints = null,
        string? terminationCode = null, Feature126HandoffStatus handoffStatus = Feature126HandoffStatus.NotApplicable,
        long? publishedCount = null, long? inconclusiveCount = null)
    {
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64) throw new ArgumentException("CorrelationId must be 1-64 characters.", nameof(correlationId));
        var workStarted = state is not (Feature126RunState.Disabled or Feature126RunState.ActivationGuardRejected or Feature126RunState.CurrentDaySucceededNoOp)
            && !(state == Feature126RunState.Failed && leaseStatus == Feature126LeaseStatus.Contended);
        var metricMap = metrics is null ? null : Feature126Maps.Metrics(metrics);
        var failureMap = failures is null ? null : Feature126Maps.Failures(failures);
        var endpointMap = endpoints is null ? null : Feature126Maps.Endpoints(endpoints);
        if (workStarted)
        {
            metricMap = metricMap?.ToDictionary(x => x.Key, x => new Feature126MetricCounts(x.Value.Accepted ?? 0, x.Value.Unchanged ?? 0, x.Value.Failed ?? 0));
            failureMap = failureMap?.ToDictionary(x => x.Key, x => (long?)(x.Value ?? 0L), StringComparer.Ordinal);
            endpointMap = endpointMap?.ToDictionary(x => x.Key, x => new Feature126EndpointCounts(x.Value.Attempted ?? 0, x.Value.Succeeded ?? 0, x.Value.Failed ?? 0));
        }
        return new(correlationId, Round(startedAtUtc), Round(completedAtUtc),
            workStarted ? Math.Max(0, (long)(Round(completedAtUtc) - Round(startedAtUtc)).TotalMilliseconds) : null,
            tehranDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), enabled, state, leaseStatus,
            recoveredLease, eligibleCompanies, attemptedCompanies, succeededCompanies, failedCompanies,
            metricMap ?? (workStarted ? Feature126Maps.ZeroMetrics() : Feature126Maps.NullMetrics()),
            failureMap ?? (workStarted ? Feature126Maps.ZeroFailures() : Feature126Maps.NullFailures()),
            endpointMap ?? (workStarted ? Feature126Maps.ZeroEndpoints() : Feature126Maps.NullEndpoints()), terminationCode ?? state.ToString(), handoffStatus,
            publishedCount, inconclusiveCount);
    }

    private static DateTimeOffset Round(DateTimeOffset value) => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Millisecond, TimeSpan.Zero);
}

internal static class Feature126Maps
{
    public static IReadOnlyDictionary<string, Feature126MetricCounts> Metrics(IReadOnlyDictionary<string, Feature126MetricCounts> source) => new Dictionary<string, Feature126MetricCounts>(StringComparer.Ordinal) { ["PSGauge"] = source.GetValueOrDefault("PSGauge") ?? new(null, null, null), ["PEGauge"] = source.GetValueOrDefault("PEGauge") ?? new(null, null, null), ["EquilibriumGauge"] = source.GetValueOrDefault("EquilibriumGauge") ?? new(null, null, null) };
    public static IReadOnlyDictionary<string, long?> Failures(IReadOnlyDictionary<string, long?> source)
    {
        var counts = Feature126FailureCodes.Ordered.ToDictionary(key => key, _ => (long?)0, StringComparer.Ordinal);
        foreach (var pair in source)
        {
            var key = Feature126FailureCodes.Canonicalize(pair.Key);
            counts[key] = (counts[key] ?? 0) + (pair.Value ?? 0);
        }
        return Feature126FailureCodes.Ordered.ToDictionary(key => key, key => counts[key], StringComparer.Ordinal);
    }
    public static IReadOnlyDictionary<string, Feature126EndpointCounts> Endpoints(IReadOnlyDictionary<string, Feature126EndpointCounts> source) => new Dictionary<string, Feature126EndpointCounts>(StringComparer.Ordinal) { ["PSGauge"] = source.GetValueOrDefault("PSGauge") ?? new(null, null, null), ["PEGauge"] = source.GetValueOrDefault("PEGauge") ?? new(null, null, null), ["EquilibriumGauge"] = source.GetValueOrDefault("EquilibriumGauge") ?? new(null, null, null) };
    public static IReadOnlyDictionary<string, Feature126MetricCounts> NullMetrics() => new Dictionary<string, Feature126MetricCounts> { ["PSGauge"] = new(null, null, null), ["PEGauge"] = new(null, null, null), ["EquilibriumGauge"] = new(null, null, null) };
    public static IReadOnlyDictionary<string, long?> NullFailures() => Ordered(null, Feature126FailureCodes.Ordered);
    public static IReadOnlyDictionary<string, Feature126MetricCounts> ZeroMetrics() => new Dictionary<string, Feature126MetricCounts> { ["PSGauge"] = new(0, 0, 0), ["PEGauge"] = new(0, 0, 0), ["EquilibriumGauge"] = new(0, 0, 0) };
    public static IReadOnlyDictionary<string, long?> ZeroFailures() => Ordered(Feature126FailureCodes.Ordered.ToDictionary(x => x, _ => (long?)0), Feature126FailureCodes.Ordered);
    public static IReadOnlyDictionary<string, Feature126EndpointCounts> ZeroEndpoints() => new Dictionary<string, Feature126EndpointCounts> { ["PSGauge"] = new(0, 0, 0), ["PEGauge"] = new(0, 0, 0), ["EquilibriumGauge"] = new(0, 0, 0) };
    public static IReadOnlyDictionary<string, Feature126EndpointCounts> NullEndpoints() => new Dictionary<string, Feature126EndpointCounts> { ["PSGauge"] = new(null, null, null), ["PEGauge"] = new(null, null, null), ["EquilibriumGauge"] = new(null, null, null) };
    private static IReadOnlyDictionary<string, long?> Ordered(IReadOnlyDictionary<string, long?>? source, IEnumerable<string> keys) => keys.ToDictionary(x => x, x => source?.GetValueOrDefault(x), StringComparer.Ordinal);
}

public static class Feature126CanonicalJsonSerializer
{
    public static byte[] Serialize(Feature126OperationalSummary summary)
    {
        var json = new StringBuilder("{");
        String(json, "CorrelationId", summary.CorrelationId); Comma(json); String(json, "StartedAtUtc", Timestamp(summary.StartedAtUtc)); Comma(json); String(json, "CompletedAtUtc", Timestamp(summary.CompletedAtUtc)); Comma(json); Number(json, "DurationMilliseconds", summary.DurationMilliseconds); Comma(json); String(json, "TehranDate", summary.TehranDate); Comma(json); Bool(json, "Enabled", summary.Enabled); Comma(json); String(json, "RunState", summary.RunState.ToString()); Comma(json); String(json, "LeaseStatus", summary.LeaseStatus.ToString()); Comma(json); Bool(json, "RecoveredLease", summary.RecoveredLease); Comma(json); Number(json, "EligibleCompanies", summary.EligibleCompanies); Comma(json); Number(json, "AttemptedCompanies", summary.AttemptedCompanies); Comma(json); Number(json, "SucceededCompanies", summary.SucceededCompanies); Comma(json); Number(json, "FailedCompanies", summary.FailedCompanies); Comma(json);
        Map(json, "MetricCounts", summary.MetricCounts, (b, v) => { Number(b, "Accepted", v.Accepted); Comma(b); Number(b, "Unchanged", v.Unchanged); Comma(b); Number(b, "Failed", v.Failed); }); Comma(json);
        Map(json, "FailureCodeCounts", summary.FailureCodeCounts, (b, v) => Number(b, "", v), false); Comma(json);
        Map(json, "EndpointCounts", summary.EndpointCounts, (b, v) => { Number(b, "Attempted", v.Attempted); Comma(b); Number(b, "Succeeded", v.Succeeded); Comma(b); Number(b, "Failed", v.Failed); }); Comma(json);
        String(json, "TerminationCode", summary.TerminationCode); Comma(json); String(json, "HandoffStatus", summary.HandoffStatus.ToString()); Comma(json); Number(json, "PublishedCount", summary.PublishedCount); Comma(json); Number(json, "InconclusiveCount", summary.InconclusiveCount); json.Append('}');
        return Encoding.UTF8.GetBytes(json.ToString());
    }
    private static string Timestamp(DateTimeOffset v) => v.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static void Comma(StringBuilder b) => b.Append(',');
    private static void String(StringBuilder b, string name, string value) { Quote(b, name); b.Append(':'); Quote(b, value); }
    private static void Bool(StringBuilder b, string name, bool value) { Quote(b, name); b.Append(':').Append(value ? "true" : "false"); }
    private static void Number(StringBuilder b, string name, long? value) { if (name.Length > 0) { Quote(b, name); b.Append(':'); } b.Append(value?.ToString(CultureInfo.InvariantCulture) ?? "null"); }
    private static void Map<T>(StringBuilder b, string name, IReadOnlyDictionary<string, T> map, Action<StringBuilder, T> write, bool nested = true) { Quote(b, name); b.Append(":{"); var first = true; foreach (var pair in map.OrderBy(x => x.Key, StringComparer.Ordinal)) { if (!first) b.Append(','); first = false; Quote(b, pair.Key); b.Append(':'); if (nested) { b.Append('{'); write(b, pair.Value); b.Append('}'); } else write(b, pair.Value); } b.Append('}'); }
    private static void Quote(StringBuilder b, string value) { b.Append('"'); foreach (var c in value) { switch (c) { case '"': b.Append("\\\""); break; case '\\': b.Append("\\\\"); break; case '\b': b.Append("\\b"); break; case '\f': b.Append("\\f"); break; case '\n': b.Append("\\n"); break; case '\r': b.Append("\\r"); break; case '\t': b.Append("\\t"); break; default: if (c < 0x20) b.Append("\\u00").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture)); else b.Append(c); break; } } b.Append('"'); }
}
