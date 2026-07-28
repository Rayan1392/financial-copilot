using System.Text.Json.Serialization;

namespace FinancialCopilot.Domain.Financial.Reports;

[JsonConverter(typeof(JsonStringEnumConverter<MarketReportScope>))]
public enum MarketReportScope
{
    PublicMarket,
    IntradayMarket,
    PersonalDigest
}

[JsonConverter(typeof(JsonStringEnumConverter<MarketReportStatus>))]
public enum MarketReportStatus
{
    Pending,
    Generated,
    Fallback,
    Failed,
    Superseded
}

public sealed class MarketReport
{
    private MarketReport(
        Guid id,
        MarketReportScope scope,
        Guid? tenantId,
        Guid? actorId,
        string? actorType,
        DateOnly tradingDate,
        string windowKey,
        int revision,
        string reportVersion,
        string evidenceHash,
        string generationIdempotencyKey,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Report id is required.", nameof(id));
        if (scope == MarketReportScope.PersonalDigest &&
            (!tenantId.HasValue || !actorId.HasValue || string.IsNullOrWhiteSpace(actorType)))
            throw new ArgumentException("Personal digests require a canonical actor and tenant.");
        if (scope != MarketReportScope.PersonalDigest &&
            (tenantId.HasValue || actorId.HasValue || !string.IsNullOrWhiteSpace(actorType)))
            throw new ArgumentException("Public reports must not carry actor identity.");
        if (string.IsNullOrWhiteSpace(windowKey)) throw new ArgumentException("Window key is required.", nameof(windowKey));
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (string.IsNullOrWhiteSpace(reportVersion)) throw new ArgumentException("Report version is required.", nameof(reportVersion));
        if (string.IsNullOrWhiteSpace(evidenceHash)) throw new ArgumentException("Evidence hash is required.", nameof(evidenceHash));
        if (string.IsNullOrWhiteSpace(generationIdempotencyKey)) throw new ArgumentException("Idempotency key is required.", nameof(generationIdempotencyKey));

        Id = id;
        Scope = scope;
        TenantId = tenantId;
        ActorId = actorId;
        ActorType = actorType;
        TradingDate = tradingDate;
        WindowKey = windowKey;
        Revision = revision;
        ReportVersion = reportVersion;
        EvidenceHash = evidenceHash;
        GenerationIdempotencyKey = generationIdempotencyKey;
        CreatedAtUtc = createdAtUtc;
        Status = MarketReportStatus.Pending;
    }

    public Guid Id { get; }
    public MarketReportScope Scope { get; }
    public Guid? TenantId { get; }
    public Guid? ActorId { get; }
    public string? ActorType { get; }
    public DateOnly TradingDate { get; }
    public string WindowKey { get; }
    public int Revision { get; }
    public string ReportVersion { get; }
    public string EvidenceHash { get; }
    public string GenerationIdempotencyKey { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public MarketReportStatus Status { get; private set; }
    public string? Narrative { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? GeneratedAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public static MarketReport Start(
        MarketReportScope scope,
        Guid? tenantId,
        Guid? actorId,
        string? actorType,
        DateOnly tradingDate,
        string windowKey,
        int revision,
        string reportVersion,
        string evidenceHash,
        string generationIdempotencyKey,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), scope, tenantId, actorId, actorType, tradingDate, windowKey,
            revision, reportVersion, evidenceHash, generationIdempotencyKey, now);

    public static MarketReport ResumePending(
        Guid id,
        MarketReportScope scope,
        Guid? tenantId,
        Guid? actorId,
        string? actorType,
        DateOnly tradingDate,
        string windowKey,
        int revision,
        string reportVersion,
        string evidenceHash,
        string generationIdempotencyKey,
        DateTimeOffset createdAtUtc) =>
        new(id, scope, tenantId, actorId, actorType, tradingDate, windowKey,
            revision, reportVersion, evidenceHash, generationIdempotencyKey, createdAtUtc);

    public void PublishGenerated(string narrative, DateTimeOffset now) =>
        Publish(narrative, MarketReportStatus.Generated, now);

    public void PublishFallback(string narrative, string reason, DateTimeOffset now)
    {
        Publish(narrative, MarketReportStatus.Fallback, now);
        FailureReason = Require(reason, nameof(reason));
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        EnsurePending();
        Status = MarketReportStatus.Failed;
        FailureReason = Require(reason, nameof(reason));
        GeneratedAtUtc = now;
    }

    private void Publish(string narrative, MarketReportStatus status, DateTimeOffset now)
    {
        EnsurePending();
        Narrative = Require(narrative, nameof(narrative));
        Status = status;
        GeneratedAtUtc = now;
        PublishedAtUtc = now;
    }

    private void EnsurePending()
    {
        if (Status != MarketReportStatus.Pending)
            throw new InvalidOperationException($"A {Status} report cannot be completed again.");
    }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", name)
            : value.Trim();
}
