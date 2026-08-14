using System.Net;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public enum Feature126WorkerReadiness
{
    disabled, starting, ready, degraded, lease_lost, telemetry_unavailable, database_unavailable, stopping
}

public sealed class Feature126WorkerHealth : IFeature126RuntimeLifecycleObserver
{
    void IFeature126RuntimeLifecycleObserver.MarkRunning(string runId, DateOnly tehranDate) =>
        MarkRunning(runId, tehranDate.ToString("yyyy-MM-dd"));

    private readonly object sync = new();
    private Feature126WorkerReadiness state = Feature126WorkerReadiness.starting;
    private DateTimeOffset? lastAcknowledgedEventUtc;
    private string? runId;
    private string? tehranDate;
    private DateTimeOffset? lastHeartbeatUtc;
    private string? lastRunState;
    private string leaseState = "NotAttempted";
    private string telemetryState = "starting";
    private string? configurationRevision;
    private bool configurationValid;
    private bool databaseAvailable;
    private bool telemetryAvailable;
    private bool leaseAvailable;
    private bool leaseRenewalAvailable;
    private bool runStartedAcknowledged;
    private bool lifecycleHealthy = true;
    private long runsStarted, successfulRuns, failedRuns, runsTerminal, leaseAcquisitions, leaseRenewals, leaseLosses, telemetryExportFailures, eligibleCompanies, handoffCount;
    private DateTimeOffset? lastSuccessfulRunUtc;
    private double lastRunDurationMilliseconds = -1;
    private double lastProviderLatencyMilliseconds = -1;
    private DateTimeOffset? runStartedUtc;
    private readonly Dictionary<string, long> failureCodeCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> endpointResultCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> terminalOutcomeCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> terminalCompanyCounts = new(StringComparer.Ordinal);
    private readonly long[] runDurationBuckets = new long[6];
    private long runDurationCount;
    private double runDurationSumMilliseconds;

    public Feature126WorkerReadiness State { get { lock (sync) return state; } }
    public bool Live { get { lock (sync) return true; } }
    public void Set(Feature126WorkerReadiness value) { lock (sync) { state = value; } }
    public void Configure(string revision, bool valid = true)
    {
        lock (sync) { configurationRevision = revision; configurationValid = valid; Recompute(); }
    }
    public void MarkDisabled() { lock (sync) { state = Feature126WorkerReadiness.disabled; telemetryState = "disabled"; leaseState = "NotAttempted"; configurationValid = true; databaseAvailable = true; telemetryAvailable = true; leaseAvailable = true; leaseRenewalAvailable = true; runStartedAcknowledged = true; } }
    public void MarkRunning(string? currentRunId = null, string? currentTehranDate = null)
    {
        lock (sync)
        {
            configurationValid = true;
            databaseAvailable = true;
            telemetryAvailable = true;
            telemetryState = "available";
            leaseAvailable = true;
            leaseRenewalAvailable = true;
            runStartedAcknowledged = true;
            leaseState = "Owned";
            lastRunState = "Running";
            runId = currentRunId ?? runId;
            tehranDate = currentTehranDate ?? tehranDate;
            runStartedUtc = DateTimeOffset.UtcNow;
            runsStarted++;
            leaseAcquisitions++;
            Recompute();
        }
    }
    public void MarkRunAttempt() { lock (sync) { runStartedUtc = DateTimeOffset.UtcNow; state = Feature126WorkerReadiness.starting; } }
    public void MarkTelemetryAvailable() { lock (sync) { telemetryAvailable = true; telemetryState = "available"; Recompute(); } }
    public void MarkTelemetryUnavailable() { lock (sync) { telemetryAvailable = false; telemetryState = "unavailable"; state = Feature126WorkerReadiness.telemetry_unavailable; telemetryExportFailures++; } }
    public void MarkDatabaseAvailable() { lock (sync) { databaseAvailable = true; Recompute(); } }
    public void MarkDatabaseUnavailable() { lock (sync) { databaseAvailable = false; state = Feature126WorkerReadiness.database_unavailable; } }
    public void MarkLeaseLost() { lock (sync) { leaseAvailable = false; leaseRenewalAvailable = false; leaseState = "Lost"; state = Feature126WorkerReadiness.lease_lost; leaseLosses++; } }
    public void MarkLeaseRestored() { lock (sync) { leaseAvailable = true; leaseRenewalAvailable = true; leaseState = "Owned"; Recompute(); } }
    public void MarkLeaseReadiness(bool liveRow, bool renewalCapable)
    {
        lock (sync)
        {
            leaseAvailable = liveRow;
            leaseRenewalAvailable = renewalCapable;
            leaseState = liveRow ? "Available" : "NotAttempted";
            Recompute();
        }
    }
    public void MarkStartupAcknowledgement(bool acknowledged)
    {
        lock (sync) { runStartedAcknowledged = acknowledged; Recompute(); }
    }
    public void MarkOwnershipConflict()
    {
        lock (sync) { configurationValid = false; state = Feature126WorkerReadiness.degraded; }
    }
    public void RecordProviderLatency(double milliseconds) { lock (sync) { lastProviderLatencyMilliseconds = Math.Max(0, milliseconds); } }
    public void RecordRun(Feature126IngestionRunResult result)
    {
        lock (sync)
        {
            var summary = result.OperationalSummary;
            lastAcknowledgedEventUtc = DateTimeOffset.UtcNow;
            runId = result.CorrelationId;
            tehranDate = result.TehranDate.ToString("yyyy-MM-dd");
            lastRunState = summary?.RunState.ToString() ?? "Unknown";
            leaseState = summary?.LeaseStatus.ToString() ?? leaseState;
            leaseAvailable = summary?.LeaseStatus is Feature126LeaseStatus.Owned or Feature126LeaseStatus.Recovered;
            eligibleCompanies = summary?.EligibleCompanies ?? eligibleCompanies;
            handoffCount += summary?.HandoffStatus == Feature126HandoffStatus.NotApplicable ? 0 : 1;
            runsTerminal++;
            var succeeded = summary?.RunState is Feature126RunState.Success or Feature126RunState.PartialSuccess;
            if (succeeded) { successfulRuns++; lastSuccessfulRunUtc = DateTimeOffset.UtcNow; }
            else if (summary?.RunState is not (Feature126RunState.Disabled or Feature126RunState.CurrentDaySucceededNoOp)) failedRuns++;
            lastRunDurationMilliseconds = summary?.DurationMilliseconds ?? -1;
            if (summary is not null)
            {
                terminalOutcomeCounts[summary.RunState.ToString()] = terminalOutcomeCounts.GetValueOrDefault(summary.RunState.ToString()) + 1;
                terminalCompanyCounts["succeeded"] = terminalCompanyCounts.GetValueOrDefault("succeeded") + (summary.SucceededCompanies ?? 0);
                terminalCompanyCounts["failed"] = terminalCompanyCounts.GetValueOrDefault("failed") + (summary.FailedCompanies ?? 0);
                if (summary.DurationMilliseconds is { } duration)
                {
                    runDurationCount++;
                    runDurationSumMilliseconds += duration;
                    var bucket = duration <= 1000 ? 0 : duration <= 5000 ? 1 : duration <= 30000 ? 2 : duration <= 120000 ? 3 : duration <= 600000 ? 4 : 5;
                    for (var index = bucket; index < runDurationBuckets.Length; index++) runDurationBuckets[index]++;
                }
                foreach (var pair in summary.FailureCodeCounts) failureCodeCounts[pair.Key] = (failureCodeCounts.GetValueOrDefault(pair.Key) + (pair.Value ?? 0));
                foreach (var pair in summary.EndpointCounts)
                {
                    endpointResultCounts[$"{pair.Key}:attempted"] = endpointResultCounts.GetValueOrDefault($"{pair.Key}:attempted") + (pair.Value.Attempted ?? 0);
                    endpointResultCounts[$"{pair.Key}:succeeded"] = endpointResultCounts.GetValueOrDefault($"{pair.Key}:succeeded") + (pair.Value.Succeeded ?? 0);
                    endpointResultCounts[$"{pair.Key}:failed"] = endpointResultCounts.GetValueOrDefault($"{pair.Key}:failed") + (pair.Value.Failed ?? 0);
                }
            }
            state = lastRunState == nameof(Feature126RunState.LeaseLost) ? Feature126WorkerReadiness.lease_lost : state;
            if (runStartedUtc is not null) runStartedUtc = null;
            Recompute();
        }
    }
    public void Acknowledge(string? currentRunId = null, string? currentTehranDate = null)
    {
        lock (sync) { lastAcknowledgedEventUtc = DateTimeOffset.UtcNow; runId = currentRunId ?? runId; tehranDate = currentTehranDate ?? tehranDate; }
    }
    public void Heartbeat() { lock (sync) { lastHeartbeatUtc = DateTimeOffset.UtcNow; leaseRenewals++; leaseRenewalAvailable = true; lastAcknowledgedEventUtc = lastHeartbeatUtc; } }
    public void MarkStopping() { lock (sync) { state = Feature126WorkerReadiness.stopping; } }
    public void MarkConfigurationInvalid() { lock (sync) { configurationValid = false; state = Feature126WorkerReadiness.degraded; } }
    private void Recompute()
    {
        if (!configurationValid) { state = Feature126WorkerReadiness.degraded; return; }
        if (!databaseAvailable) { state = Feature126WorkerReadiness.database_unavailable; return; }
        if (!telemetryAvailable) { state = Feature126WorkerReadiness.telemetry_unavailable; return; }
        if (!leaseAvailable || !leaseRenewalAvailable) { state = Feature126WorkerReadiness.lease_lost; return; }
        if (!runStartedAcknowledged || !lifecycleHealthy) { state = Feature126WorkerReadiness.starting; return; }
        state = Feature126WorkerReadiness.ready;
    }
    public Feature126HealthSnapshot Snapshot()
    {
        lock (sync)
        {
            return new Feature126HealthSnapshot(state.ToString(), state != Feature126WorkerReadiness.disabled, lastAcknowledgedEventUtc,
                runId, tehranDate, lastHeartbeatUtc is null ? -1 : Math.Max(0, (DateTimeOffset.UtcNow - lastHeartbeatUtc.Value).TotalMilliseconds),
                lastRunState, leaseState, telemetryState, configurationRevision, runsStarted, runsTerminal,
                leaseAcquisitions, leaseRenewals, leaseLosses, telemetryExportFailures, eligibleCompanies, handoffCount,
                runStartedUtc is null ? 0 : Math.Max(0, (DateTimeOffset.UtcNow - runStartedUtc.Value).TotalMilliseconds),
                successfulRuns, failedRuns, lastSuccessfulRunUtc, lastRunDurationMilliseconds, lastProviderLatencyMilliseconds,
                new Dictionary<string, long>(failureCodeCounts), new Dictionary<string, long>(endpointResultCounts),
                new Dictionary<string, long>(terminalOutcomeCounts), new Dictionary<string, long>(terminalCompanyCounts),
                (long[])runDurationBuckets.Clone(), runDurationCount, runDurationSumMilliseconds);
        }
    }
}

public sealed class Feature126ManagementOptions
{
    public const string SectionName = "Feature126:Management";
    public bool Enabled { get; init; } = true;
    public string Address { get; init; } = "http://127.0.0.1:5096/";
}

public sealed class Feature126ManagementServer(
    IOptions<Feature126ManagementOptions> options,
    IOptions<Feature126Options> featureOptions,
    IOptions<RelativeValuationIngestionOptions> ingestionOptions,
    IServiceScopeFactory scopeFactory,
    Feature126WorkerHealth health,
    ILogger<Feature126ManagementServer> logger) : BackgroundService
{
    private HttpListener? listener;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var management = options.Value;
        var ingestion = featureOptions.Value.Enabled ? ingestionOptions.Value : null;
        if (ingestion is null || !ingestion.Enabled)
        {
            // The management listener remains available so disabled is observable.
            health.MarkDisabled();
        }
        else
        {
            health.Configure(ingestion.ConfigurationRevision,
                !string.IsNullOrWhiteSpace(ingestion.ConfigurationRevision) &&
                !string.IsNullOrWhiteSpace(ingestion.DeploymentIdentifier) &&
                ingestion.DailyCadenceMinutes > 0 && ingestion.LeaseMinutes > 0 &&
                !(ingestion.LegacyFeature114PsOwnerEnabled || ingestion.NadpcoFeature125TriggerEnabled));
            if (string.IsNullOrWhiteSpace(ingestion.ConfigurationRevision) || string.IsNullOrWhiteSpace(ingestion.DeploymentIdentifier))
                health.MarkConfigurationInvalid();
            if (ingestion.LegacyFeature114PsOwnerEnabled || ingestion.NadpcoFeature125TriggerEnabled)
                health.MarkOwnershipConflict();
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
                if (await db.Database.CanConnectAsync(stoppingToken)) health.MarkDatabaseAvailable();
                else health.MarkDatabaseUnavailable();

                var leaseReadinessProbe = scope.ServiceProvider.GetService<IFeature126LeaseReadinessProbe>();
                if (leaseReadinessProbe is not null)
                {
                    var lease = await leaseReadinessProbe.ProbeReadinessAsync(stoppingToken);
                    health.MarkLeaseReadiness(lease.LiveRow, lease.RenewalCapable);
                }

                var durableEvents = scope.ServiceProvider.GetRequiredService<IFeature126DurableEventSink>();
                try { if (await durableEvents.ProbeAsync(stoppingToken)) health.MarkTelemetryAvailable(); else health.MarkTelemetryUnavailable(); }
                catch { health.MarkTelemetryUnavailable(); }
                health.MarkStartupAcknowledgement(await db.Feature126Events.AnyAsync(
                    x => x.EventType == Feature126LifecycleEventType.RunStarted.ToString(), stoppingToken));
            }
            catch { health.MarkDatabaseUnavailable(); }
        }
        if (!management.Enabled) { await Task.Delay(Timeout.Infinite, stoppingToken); return; }
        listener = new HttpListener();
        listener.Prefixes.Add(management.Address.EndsWith('/') ? management.Address : management.Address + "/");
        try { listener.Start(); } catch (Exception ex) { health.Set(Feature126WorkerReadiness.degraded); logger.LogError(ex, "Feature 126 management listener failed to start."); return; }
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(stoppingToken);
                await HandleAsync(context, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (HttpListenerException) when (stoppingToken.IsCancellationRequested) { }
        finally { health.MarkStopping(); listener?.Stop(); }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken token)
    {
        var path = context.Request.Url?.AbsolutePath;
        if (path == "/health/live") await WriteAsync(context, 200, "{\"status\":\"live\"}", "application/json");
        else if (path == "/health/ready")
        {
            var snapshot = health.Snapshot();
            await WriteAsync(context, IsReady(snapshot) ? 200 : 503,
                JsonSerializer.Serialize(snapshot), "application/json");
        }
        else if (path == "/metrics") await WriteAsync(context, 200, Feature126PrometheusMetrics.Render(health.Snapshot()), "text/plain; version=0.0.4");
        else await WriteAsync(context, 404, "", "text/plain");
    }

    private static async Task WriteAsync(HttpListenerContext context, int status, string body, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status; context.Response.ContentType = contentType; context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
    }

    public static bool IsReady(Feature126HealthSnapshot snapshot) =>
        snapshot.State is "ready" or "disabled";
}

public static class Feature126PrometheusMetrics
{
    public static string Render(Feature126HealthSnapshot snapshot)
    {
        var metrics =
        $"feature126_readiness{{state=\"{snapshot.State}\"}} {(snapshot.State is "ready" or "disabled" ? 1 : 0)}\n" +
        $"feature126_health{{state=\"{snapshot.State}\"}} {(snapshot.State is not "stopping" ? 1 : 0)}\n" +
        $"feature126_heartbeat_age_ms {snapshot.HeartbeatAgeMilliseconds:0}\n" +
        $"feature126_run_age_ms {snapshot.RunAgeMilliseconds:0}\n" +
        $"feature126_runs_started_total {snapshot.RunsStarted}\n" +
        $"feature126_runs_successful_total {snapshot.SuccessfulRuns}\n" +
        $"feature126_runs_failed_total {snapshot.FailedRuns}\n" +
        $"feature126_last_successful_run_timestamp_seconds {(snapshot.LastSuccessfulRunUtc?.ToUnixTimeSeconds() ?? 0)}\n" +
        $"feature126_last_run_duration_ms {snapshot.LastRunDurationMilliseconds:0}\n" +
        $"feature126_provider_latency_ms {snapshot.LastProviderLatencyMilliseconds:0}\n" +
        $"feature126_lease_acquisitions_total {snapshot.LeaseAcquisitions}\n" +
        $"feature126_lease_renewals_total {snapshot.LeaseRenewals}\n" +
        $"feature126_lease_losses_total {snapshot.LeaseLosses}\n" +
        $"feature126_handoff_total {snapshot.HandoffCount}\n" +
        $"feature126_eligible_companies {snapshot.EligibleCompanies}\n" +
        $"feature126_telemetry_export_failures_total {snapshot.TelemetryExportFailures}\n" +
        $"feature126_lease_state{{state=\"{snapshot.LeaseState}\"}} 1\n" +
        $"feature126_telemetry_state{{state=\"{snapshot.TelemetryState}\"}} 1\n" +
        $"feature126_last_run_state{{state=\"{snapshot.LastRunState ?? "none"}\"}} 1\n";
        foreach (var pair in snapshot.FailureCodeCounts ?? new Dictionary<string, long>())
            metrics += $"feature126_failure_code_total{{code=\"{pair.Key}\"}} {pair.Value}\n";
        foreach (var pair in snapshot.EndpointResultCounts ?? new Dictionary<string, long>())
        {
            var split = pair.Key.Split(':', 2);
            metrics += $"feature126_endpoint_attempts_total{{endpoint=\"{split[0]}\",result=\"{split.ElementAtOrDefault(1) ?? "unknown"}\"}} {pair.Value}\n";
        }
        metrics += $"feature126_lifecycle_state{{state=\"{snapshot.LastRunState ?? "none"}\"}} 1\n";
        foreach (var pair in snapshot.TerminalOutcomeCounts ?? new Dictionary<string, long>())
            metrics += $"feature126_runs_terminal_total{{outcome=\"{pair.Key}\",lifecycle_state=\"{pair.Key}\"}} {pair.Value}\n";
        var buckets = snapshot.RunDurationBucketCounts ?? Array.Empty<long>();
        var limits = new[] { "1000", "5000", "30000", "120000", "600000", "+Inf" };
        for (var index = 0; index < Math.Min(buckets.Count, limits.Length); index++)
            metrics += $"feature126_run_duration_ms_bucket{{le=\"{limits[index]}\"}} {buckets[index]}\n";
        metrics += $"feature126_run_duration_ms_count {snapshot.RunDurationCount}\n";
        metrics += $"feature126_run_duration_ms_sum {snapshot.RunDurationSumMilliseconds:0}\n";
        foreach (var pair in snapshot.TerminalCompanyCounts ?? new Dictionary<string, long>())
            metrics += $"feature126_terminal_company_progress_total{{outcome=\"{pair.Key}\"}} {pair.Value}\n";
        return metrics;
    }
}
