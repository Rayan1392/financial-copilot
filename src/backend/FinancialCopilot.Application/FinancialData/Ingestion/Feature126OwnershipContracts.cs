using System.Security.Cryptography;
using System.Text;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum Feature126ActivationRejectionReason
{
    MissingConfigurationRevision,
    MissingDeploymentIdentifier,
    ConflictingOwnerActivation
}

public sealed record Feature126OwnerActivationStates(
    bool Feature126Enabled,
    bool LegacyFeature114PsOwnerEnabled,
    bool NadpcoFeature125TriggerEnabled);

public sealed record Feature126ActivationDecision(
    bool Allowed,
    Feature126ActivationRejectionReason? RejectionReason = null)
{
    public static Feature126ActivationDecision Allow() => new(true);
    public static Feature126ActivationDecision Reject(Feature126ActivationRejectionReason reason) => new(false, reason);
}

/// <summary>Pure ownership compatibility policy. It has no runtime or operational inputs.</summary>
public static class Feature126ActivationGuard
{
    public static Feature126ActivationDecision EvaluateActivation(
        string? candidateConfigurationRevision,
        string? deploymentIdentifier,
        Feature126OwnerActivationStates? owners)
    {
        if (string.IsNullOrWhiteSpace(candidateConfigurationRevision))
            return Feature126ActivationDecision.Reject(Feature126ActivationRejectionReason.MissingConfigurationRevision);
        if (string.IsNullOrWhiteSpace(deploymentIdentifier))
            return Feature126ActivationDecision.Reject(Feature126ActivationRejectionReason.MissingDeploymentIdentifier);
        if (owners is null || (owners.Feature126Enabled && (owners.LegacyFeature114PsOwnerEnabled || owners.NadpcoFeature125TriggerEnabled)))
            return Feature126ActivationDecision.Reject(Feature126ActivationRejectionReason.ConflictingOwnerActivation);
        return Feature126ActivationDecision.Allow();
    }
}

public sealed record Feature126RunIdentity(string CorrelationId, DateOnly TehranCalculationDate)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(CorrelationId);
}

public sealed record Feature126SourceFactEvidence(
    Guid CompanyId,
    RelativeValuationSourceKind SourceKind,
    Guid? FactId,
    string Version)
{
    public string? SymbolIsin { get; init; }
    public bool IsMissing => FactId is null && Version.Equals("Missing", StringComparison.Ordinal);
}

public sealed record Feature126SourceSnapshotEvidence(
    DateOnly TehranCalculationDate,
    IReadOnlyList<Feature126SourceFactEvidence> Facts,
    string Digest)
{
    public static Feature126SourceSnapshotEvidence Create(
        DateOnly calculationDate,
        IEnumerable<Feature126SourceFactEvidence> facts)
    {
        var ordered = facts.OrderBy(x => x.SymbolIsin ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.CompanyId)
            .ThenBy(x => x.SourceKind)
            .ToArray();
        var canonical = string.Join('\n', ordered.Select(x =>
            $"{x.SymbolIsin ?? ""}|{x.CompanyId:D}|{x.SourceKind}|{x.FactId?.ToString("D") ?? "Missing"}|{x.Version}"));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{calculationDate:yyyy-MM-dd}\n{canonical}")));
        return new(calculationDate, ordered, digest);
    }

    public static Feature126SourceSnapshotEvidence CreateForAdmittedUniverse(
        DateOnly calculationDate,
        IEnumerable<RelativeValuationEligibleSymbol> admitted,
        IEnumerable<Feature126SourceFactEvidence> availableFacts)
    {
        var factsByKey = availableFacts.ToDictionary(x => (x.CompanyId, x.SourceKind));
        var facts = admitted
            .Where(x => x.CompanyId.HasValue && !string.IsNullOrWhiteSpace(x.SymbolIsin))
            .SelectMany(company => Enum.GetValues<RelativeValuationSourceKind>()
                .Where(kind => kind is RelativeValuationSourceKind.PSGauge or RelativeValuationSourceKind.PEGauge or RelativeValuationSourceKind.EquilibriumGauge)
                .Select(kind => factsByKey.TryGetValue((company.CompanyId!.Value, kind), out var fact)
                    ? fact with { SymbolIsin = company.SymbolIsin }
                    : new Feature126SourceFactEvidence(company.CompanyId.Value, kind, null, "Missing") { SymbolIsin = company.SymbolIsin }))
            .ToArray();
        return Create(calculationDate, facts);
    }
}

public sealed record Feature126HandoffPackage(
    Feature126RunIdentity RunIdentity,
    Guid FencingToken,
    Feature126SourceSnapshotEvidence SourceSnapshotEvidence)
{
    public IReadOnlyList<RelativeValuationEligibleSymbol> AdmittedUniverse =>
        SourceSnapshotEvidence.Facts
            .Where(fact => !string.IsNullOrWhiteSpace(fact.SymbolIsin))
            .GroupBy(fact => fact.CompanyId)
            .Select(group => new RelativeValuationEligibleSymbol(
                group.Select(fact => fact.SymbolIsin!).OrderBy(x => x, StringComparer.Ordinal).First(),
                group.Key))
            .OrderBy(symbol => symbol.SymbolIsin, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.CompanyId)
            .ToArray();

    public bool IsComplete => RunIdentity is not null &&
                               RunIdentity.IsComplete &&
                               FencingToken != Guid.Empty &&
                               SourceSnapshotEvidence is not null &&
                               SourceSnapshotEvidence.Facts is not null &&
                               SourceSnapshotEvidence.Facts.All(fact =>
                                   fact.CompanyId != Guid.Empty &&
                                   !string.IsNullOrWhiteSpace(fact.Version)) &&
                               !string.IsNullOrWhiteSpace(SourceSnapshotEvidence.Digest) &&
                               SourceSnapshotEvidence.Digest.Equals(
                                   Feature126SourceSnapshotEvidence.Create(
                                       SourceSnapshotEvidence.TehranCalculationDate,
                                       SourceSnapshotEvidence.Facts).Digest,
                                   StringComparison.Ordinal);

    public static Feature126HandoffPackage Create(
        Feature126RunIdentity runIdentity,
        Guid fencingToken,
        IEnumerable<Feature126SourceFactEvidence> facts) =>
        new(runIdentity, fencingToken,
            Feature126SourceSnapshotEvidence.Create(runIdentity.TehranCalculationDate, facts));
}

public enum Feature125HandoffRejectionReason
{
    IncompletePackage,
    StaleFencingToken,
    ChangedSnapshot
}

public sealed record Feature125HandoffValidationResult(
    bool Accepted,
    Feature125HandoffRejectionReason? RejectionReason = null)
{
    public static Feature125HandoffValidationResult Accept() => new(true);
    public static Feature125HandoffValidationResult Reject(Feature125HandoffRejectionReason reason) => new(false, reason);
}

public sealed record Feature126HandoffLeaseState(
    string LeaseName,
    DateOnly CalculationDate,
    LeaseState State,
    Guid FencingToken,
    DateTimeOffset ExpiresAtUtc);

public interface IFeature125HandoffConsumer
{
    Feature125HandoffValidationResult Validate(
        Feature126HandoffPackage package,
        Feature126HandoffLeaseState lease,
        Feature126SourceSnapshotEvidence? currentSnapshot,
        DateTimeOffset nowUtc);
}

/// <summary>Feature 125 validation boundary. Callers must validate before opening side effects.</summary>
public sealed class Feature125HandoffConsumer : IFeature125HandoffConsumer
{
    public Feature125HandoffValidationResult Validate(
        Feature126HandoffPackage package,
        Feature126HandoffLeaseState lease,
        Feature126SourceSnapshotEvidence? currentSnapshot,
        DateTimeOffset nowUtc)
    {
        if (package is null || !package.IsComplete || currentSnapshot is null ||
            !currentSnapshot.Digest.Equals(package.SourceSnapshotEvidence.Digest, StringComparison.Ordinal))
            return Feature125HandoffValidationResult.Reject(
                package is null || !package.IsComplete
                    ? Feature125HandoffRejectionReason.IncompletePackage
                    : Feature125HandoffRejectionReason.ChangedSnapshot);

        if (lease.State != LeaseState.Handoff || lease.ExpiresAtUtc <= nowUtc ||
            lease.FencingToken != package.FencingToken ||
            !lease.CalculationDate.Equals(package.RunIdentity.TehranCalculationDate) ||
            !lease.LeaseName.Equals("feature126", StringComparison.Ordinal))
            return Feature125HandoffValidationResult.Reject(Feature125HandoffRejectionReason.StaleFencingToken);

        return Feature125HandoffValidationResult.Accept();
    }
}

public static class Feature125HandoffValidationBoundary
{
    public static async Task<T?> ExecuteAsync<T>(
        IFeature125HandoffConsumer consumer,
        Feature126HandoffPackage package,
        Feature126HandoffLeaseState lease,
        Feature126SourceSnapshotEvidence currentSnapshot,
        DateTimeOffset nowUtc,
        Func<Task<T>> downstream,
        CancellationToken cancellationToken = default,
        Func<Task<bool>>? sideEffectFence = null)
    {
        var validation = consumer.Validate(package, lease, currentSnapshot, nowUtc);
        if (!validation.Accepted) return default;
        cancellationToken.ThrowIfCancellationRequested();
        if (sideEffectFence is not null && !await sideEffectFence()) return default;
        return await downstream();
    }
}
