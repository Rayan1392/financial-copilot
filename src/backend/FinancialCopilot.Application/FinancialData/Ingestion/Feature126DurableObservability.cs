using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

public static class Feature126ObservabilityConstants
{
    public const int SchemaVersion = 1;
    public const string LeaseName = "feature126";
}

public interface IFeature126RuntimeLifecycleObserver
{
    void MarkRunning(string runId, DateOnly tehranDate);
}

public enum Feature126LifecycleEventType
{
    RunStarted, Heartbeat, Checkpoint, HandoffStarted, HandoffCompleted,
    HandoffFailed, RunSucceeded, RunPartiallySucceeded, RunFailed,
    RunCancelled, RunTimedOut, RunLeaseLost, AbandonedRunRecovered, StaleOwner
}

public sealed record Feature126EventAppendRequest(
    string EventId,
    string RunId,
    Feature126LifecycleEventType EventType,
    string ExpectedPredecessorState,
    string OwnerId,
    Guid FencingToken,
    string TehranDate,
    string AttemptReason,
    string? RecoveredFromRunId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, object?> Fields,
    int SchemaVersion = Feature126ObservabilityConstants.SchemaVersion,
    long? ExpectedNextSequence = null);

public sealed record Feature126EventAppendAcknowledgement(
    string EventId, string RunId, long EventSequence, bool IsDuplicate,
    bool IsStaleOwner, DateTimeOffset AcknowledgedAtUtc);

public enum Feature126AppendRejection
{
    TerminalConflict, OutOfOrder, StaleOwner, InvalidPredecessor, InvalidSchema,
    EventIdentityConflict
}

public sealed class Feature126EventAppendException(
    Feature126AppendRejection rejection, string message) : InvalidOperationException(message)
{
    public Feature126AppendRejection Rejection { get; } = rejection;
}

public interface IFeature126DurableEventSink
{
    Task<Feature126EventAppendAcknowledgement> AppendAsync(
        Feature126EventAppendRequest request, CancellationToken cancellationToken);
    Task<bool> ProbeAsync(CancellationToken cancellationToken);
}

public interface IFeature126EventAppender
{
    Task<Feature126EventAppendAcknowledgement> AppendAsync(
        Feature126EventAppendRequest request, CancellationToken cancellationToken);
}

public interface IFeature126TerminalEventAppender
{
    Task<Feature126EventAppendAcknowledgement> AppendTerminalAsync(
        Feature126EventAppendRequest request, LeaseState terminalState, CancellationToken cancellationToken);
}

/// Single lifecycle append boundary. All authority is delegated to the durable sink; this process
/// deliberately keeps no run state, acknowledgement cache, or sequence registry.
public sealed class Feature126EventAppender(IFeature126DurableEventSink sink) : IFeature126EventAppender, IFeature126TerminalEventAppender
{
    public async Task<Feature126EventAppendAcknowledgement> AppendAsync(
        Feature126EventAppendRequest request, CancellationToken cancellationToken)
    {
        if (request.SchemaVersion != Feature126ObservabilityConstants.SchemaVersion)
            throw new Feature126EventAppendException(Feature126AppendRejection.InvalidSchema, "Unsupported Feature 126 event schema.");
        if (!Feature126EventOrderingContract.IsValidPredecessor(request.EventType, request.ExpectedPredecessorState))
            throw new Feature126EventAppendException(Feature126AppendRejection.OutOfOrder, "Feature 126 event predecessor is out of order.");
        if (string.IsNullOrWhiteSpace(request.EventId) ||
            !Feature126RunId.IsValid(request.RunId) ||
            string.IsNullOrWhiteSpace(request.OwnerId) ||
            request.FencingToken == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.TehranDate))
            throw new ArgumentException("Feature 126 events require a valid run identity, owner, fencing token, and Tehran date.");
        return await sink.AppendAsync(request, cancellationToken);
    }

    public Task<Feature126EventAppendAcknowledgement> AppendTerminalAsync(
        Feature126EventAppendRequest request, LeaseState terminalState, CancellationToken cancellationToken)
    {
        if (sink is not IFeature126AtomicTerminalEventSink atomicSink)
            throw new NotSupportedException("The configured Feature 126 event sink does not support atomic terminal transitions.");
        return atomicSink.AppendTerminalAsync(request, terminalState, cancellationToken);
    }
}

public interface IFeature126AtomicTerminalEventSink
{
    Task<Feature126EventAppendAcknowledgement> AppendTerminalAsync(
        Feature126EventAppendRequest request, LeaseState terminalState, CancellationToken cancellationToken);
}

public static class Feature126EventOrderingContract
{
    public static bool IsValidPredecessor(Feature126LifecycleEventType eventType, string predecessor) => eventType switch
    {
        Feature126LifecycleEventType.RunStarted => predecessor == "None",
        Feature126LifecycleEventType.Heartbeat or Feature126LifecycleEventType.Checkpoint => predecessor is "Running" or "Handoff",
        Feature126LifecycleEventType.HandoffStarted => predecessor == "Running",
        Feature126LifecycleEventType.HandoffCompleted => predecessor == "Handoff",
        Feature126LifecycleEventType.AbandonedRunRecovered => predecessor == "Running",
        Feature126LifecycleEventType.StaleOwner => predecessor is "Running" or "Handoff",
        _ => predecessor is "Running" or "Handoff"
    };

    public static string NextState(Feature126LifecycleEventType eventType, string predecessor) => eventType switch
    {
        Feature126LifecycleEventType.RunStarted => "Running",
        Feature126LifecycleEventType.HandoffStarted => "Handoff",
        Feature126LifecycleEventType.HandoffCompleted => "Handoff",
        Feature126LifecycleEventType.AbandonedRunRecovered => "LeaseLost",
        Feature126LifecycleEventType.StaleOwner => predecessor,
        Feature126LifecycleEventType.RunSucceeded => "Success",
        Feature126LifecycleEventType.RunPartiallySucceeded => "PartialSuccess",
        Feature126LifecycleEventType.RunFailed => "Failed",
        Feature126LifecycleEventType.RunCancelled => "Cancelled",
        Feature126LifecycleEventType.RunTimedOut => "Timeout",
        Feature126LifecycleEventType.RunLeaseLost => "LeaseLost",
        Feature126LifecycleEventType.HandoffFailed => "HandoffFailed",
        _ => predecessor
    };
}

public sealed class Feature126TelemetryOptions
{
    public const string SectionName = "Feature126:Observability";
    public bool Enabled { get; init; } = true;
    public string SeqEndpoint { get; init; } = "";
    public string SeqApiKey { get; init; } = "";
    public string Environment { get; init; } = "Production";
    public string Stream { get; init; } = "feature126";
    public int RequestTimeoutSeconds { get; init; } = 10;
    public int MaxRetryAttempts { get; init; } = 3;
    public int RetryBudgetSeconds { get; init; } = 20;
    public int RetentionDays { get; init; } = 730;
}

/// Awaited Seq ingestion client for required lifecycle events. It never logs the API key or payload
/// values and only retries transient transport/status failures.
public sealed class SeqFeature126EventSink(
    HttpClient httpClient,
    Feature126TelemetryOptions settings,
    TimeProvider clock,
    Action<Exception, string>? probeFailure = null,
    Action<int, string, string, Feature126LifecycleEventType>? exportResponse = null,
    Action<Exception, string, Feature126LifecycleEventType>? exportFailure = null) : IFeature126DurableEventSink
{
    public async Task<Feature126EventAppendAcknowledgement> AppendAsync(
        Feature126EventAppendRequest request, CancellationToken cancellationToken)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.SeqEndpoint))
            throw new Feature126TelemetryUnavailableException("Feature 126 durable telemetry is not configured.");

        // Seq's /api/events/raw endpoint expects one Serilog CLEF event, not generic
        // application/json. The manual curl success path uses this exact media type.
        var payload = new Dictionary<string, object?>(request.Fields, StringComparer.Ordinal)
        {
            ["@t"] = request.OccurredAtUtc,
            ["@mt"] = "Feature 126 lifecycle event {event_type}",
            ["event_id"] = request.EventId, ["run_id"] = request.RunId,
            ["event_type"] = request.EventType.ToString(), ["event_sequence"] = request.ExpectedNextSequence,
            ["expected_predecessor_state"] = request.ExpectedPredecessorState,
            ["owner_id"] = request.OwnerId, ["fencing_token_reference"] = request.FencingToken.ToString("N"),
            ["tehran_date"] = request.TehranDate, ["attempt_reason"] = request.AttemptReason,
            ["recovered_from_run_id"] = request.RecoveredFromRunId, ["schema_version"] = request.SchemaVersion,
            ["stream"] = settings.Stream, ["environment"] = settings.Environment,
            ["occurred_at_utc"] = request.OccurredAtUtc
        };
        var body = JsonSerializer.Serialize(payload);

        var deadline = clock.GetUtcNow().AddSeconds(Math.Max(1, settings.RetryBudgetSeconds));
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/vnd.serilog.clef");
                using var message = new HttpRequestMessage(HttpMethod.Post, settings.SeqEndpoint.TrimEnd('/') + "/api/events/raw")
                {
                    Content = content
                };
                if (!string.IsNullOrWhiteSpace(settings.SeqApiKey))
                    message.Headers.TryAddWithoutValidation("X-Seq-ApiKey", settings.SeqApiKey);
                using var response = await httpClient.SendAsync(message, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                exportResponse?.Invoke((int)response.StatusCode, responseBody, settings.SeqEndpoint, request.EventType);
                if (response.IsSuccessStatusCode)
                    return new(request.EventId, request.RunId, request.ExpectedNextSequence ?? 0, false, false, clock.GetUtcNow());
                if (!IsTransient(response.StatusCode) || attempt >= Math.Max(1, settings.MaxRetryAttempts) || clock.GetUtcNow() >= deadline)
                    throw new Feature126TelemetryUnavailableException($"Seq rejected Feature 126 event with HTTP {(int)response.StatusCode}. Response body: {responseBody}");
            }
            catch (Feature126TelemetryUnavailableException ex)
            {
                exportFailure?.Invoke(ex, settings.SeqEndpoint, request.EventType);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                exportFailure?.Invoke(ex, settings.SeqEndpoint, request.EventType);
                if (attempt >= Math.Max(1, settings.MaxRetryAttempts) || clock.GetUtcNow() >= deadline)
                    throw new Feature126TelemetryUnavailableException("Seq acknowledgement was not received.", ex);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(2_000, 100 * Math.Pow(2, attempt - 1))), cancellationToken);
        }
    }

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.SeqEndpoint)) return false;
        try { using var response = await httpClient.GetAsync(settings.SeqEndpoint.TrimEnd('/') + "/api", cancellationToken); return response.IsSuccessStatusCode; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            probeFailure?.Invoke(ex, settings.SeqEndpoint);
            return false;
        }
    }

    private static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 || (int)status >= 500;
}

public sealed class Feature126TelemetryUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public static class Feature126RunId
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Create(DateOnly tehranDate, DateTimeOffset nowUtc)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        var time = nowUtc.ToUnixTimeMilliseconds();
        Span<char> ulid = stackalloc char[26];
        for (var i = 9; i >= 0; i--) { ulid[i] = Alphabet[(int)(time & 31)]; time >>= 5; }
        for (var i = 0; i < 16; i++) ulid[10 + i] = Alphabet[bytes[i] & 31];
        return $"fx126-{tehranDate:yyyyMMdd}-{new string(ulid)}";
    }

    public static bool IsValid(string? value) => value is not null &&
        Regex.IsMatch(value, "^fx126-\\d{8}-[0-9A-HJKMNP-TV-Z]{26}$", RegexOptions.CultureInvariant);
}

public sealed record Feature126HealthSnapshot(
    string State, bool Enabled, DateTimeOffset? LastAcknowledgedEventUtc,
    string? CurrentRunId, string? TehranDate, double HeartbeatAgeMilliseconds,
    string? LastRunState = null, string? LeaseState = null, string? TelemetryState = null,
    string? ConfigurationRevision = null, long RunsStarted = 0, long RunsTerminal = 0,
    long LeaseAcquisitions = 0, long LeaseRenewals = 0, long LeaseLosses = 0,
    long TelemetryExportFailures = 0, long EligibleCompanies = 0, long HandoffCount = 0,
    double RunAgeMilliseconds = 0, long SuccessfulRuns = 0, long FailedRuns = 0,
    DateTimeOffset? LastSuccessfulRunUtc = null, double LastRunDurationMilliseconds = -1,
    double LastProviderLatencyMilliseconds = -1,
    IReadOnlyDictionary<string, long>? FailureCodeCounts = null,
    IReadOnlyDictionary<string, long>? EndpointResultCounts = null,
    IReadOnlyDictionary<string, long>? TerminalOutcomeCounts = null,
    IReadOnlyDictionary<string, long>? TerminalCompanyCounts = null,
    IReadOnlyList<long>? RunDurationBucketCounts = null,
    long RunDurationCount = 0,
    double RunDurationSumMilliseconds = 0);
