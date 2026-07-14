using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Radar;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Radar;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Radar;

public sealed class RadarRepository(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : IRadarRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RadarProfileSnapshot?> FindAsync(
        RadarActor actor,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.RadarProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.TenantId == actor.TenantId && item.ActorId == actor.ActorId && item.ActorType == actor.ActorType &&
                (includeRemoved || item.State != nameof(RadarState.Removed)), cancellationToken);
        return row is null ? null : await MapSnapshotAsync(row, cancellationToken);
    }

    public async Task<IReadOnlyCollection<RadarProfileSnapshot>> GetActiveAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rows = await dbContext.RadarProfiles.AsNoTracking()
            .Where(row => row.State == nameof(RadarState.Active) &&
                          (row.NextAttemptAtUtc == null || row.NextAttemptAtUtc <= now) &&
                          (row.LeaseExpiresAtUtc == null || row.LeaseExpiresAtUtc <= now))
            .OrderBy(row => row.LastEvaluatedAtUtc)
            .ThenBy(row => row.CreatedAtUtc)
            .Take(Math.Clamp(maximumCount, 1, 1_000))
            .ToArrayAsync(cancellationToken);
        var snapshots = new List<RadarProfileSnapshot>(rows.Length);
        foreach (var row in rows) snapshots.Add(await MapSnapshotAsync(row, cancellationToken));
        return snapshots;
    }

    public async Task SaveProfileAsync(
        RadarProfile profile,
        string auditAction,
        string source,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.RadarProfiles.SingleOrDefaultAsync(item =>
            item.TenantId == profile.Actor.TenantId && item.ActorId == profile.Actor.ActorId &&
            item.ActorType == profile.Actor.ActorType, cancellationToken);
        if (row is null)
        {
            if (profile.Version != 1) throw new RadarValidationException("A new radar profile must start at version 1.");
            row = ToRow(profile);
            dbContext.RadarProfiles.Add(row);
        }
        else
        {
            if (row.Version != profile.Version - 1)
                throw new RadarValidationException("Radar profile was changed by another request.");
            Apply(row, profile);
        }

        AddAudit(profile, auditAction, source, null);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveOverrideAsync(
        RadarProfile profile,
        RadarSymbolOverride symbolOverride,
        string auditAction,
        string source,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.RadarSymbolOverrides.SingleOrDefaultAsync(item =>
            item.RadarProfileId == profile.Id && item.ExternalCompanyId == symbolOverride.ExternalCompanyId,
            cancellationToken);
        if (row is null)
        {
            if (symbolOverride.Version != 1) throw new RadarValidationException("A new radar override must start at version 1.");
            row = ToRow(symbolOverride);
            dbContext.RadarSymbolOverrides.Add(row);
        }
        else
        {
            if (row.Version != symbolOverride.Version - 1)
                throw new RadarValidationException("Radar symbol override was changed by another request.");
            Apply(row, symbolOverride);
        }

        AddAudit(profile, auditAction, source, symbolOverride);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<RadarProfileSnapshot> MapSnapshotAsync(
        RadarProfileRow row,
        CancellationToken cancellationToken)
    {
        var overrides = await dbContext.RadarSymbolOverrides.AsNoTracking()
            .Where(item => item.RadarProfileId == row.Id && item.State != nameof(RadarState.Removed))
            .OrderBy(item => item.ExternalCompanyId)
            .ToArrayAsync(cancellationToken);
        return new RadarProfileSnapshot(ToDomain(row), overrides.Select(ToDomain).ToArray(),
            row.LastEvaluatedAtUtc, row.LastSourceFreshnessUtc);
    }

    internal static RadarProfile ToDomain(RadarProfileRow row) => RadarProfile.Rehydrate(
        row.Id, new RadarActor(row.TenantId, row.ActorId, row.ActorType), Enum.Parse<RadarState>(row.State),
        DeserializeTypes(row.EventTypesJson) ?? [], Enum.Parse<InsightSeverity>(row.MinimumSeverity),
        row.MinimumImportance, Enum.Parse<RadarSensitivity>(row.Sensitivity),
        Enum.Parse<RadarDeliveryMode>(row.DeliveryMode), row.Version, row.ConcurrencyToken,
        row.CreatedAtUtc, row.UpdatedAtUtc, row.RemovedAtUtc);

    internal static RadarSymbolOverride ToDomain(RadarSymbolOverrideRow row) => RadarSymbolOverride.Rehydrate(
        row.Id, row.RadarProfileId, row.ExternalCompanyId, Enum.Parse<RadarState>(row.State),
        DeserializeTypes(row.EventTypesJson), ParseNullable<InsightSeverity>(row.MinimumSeverity),
        row.MinimumImportance, ParseNullable<RadarSensitivity>(row.Sensitivity), row.Version,
        row.ConcurrencyToken, row.CreatedAtUtc, row.UpdatedAtUtc, row.RemovedAtUtc);

    private static RadarProfileRow ToRow(RadarProfile profile)
    {
        var row = new RadarProfileRow { Id = profile.Id, TenantId = profile.Actor.TenantId,
            ActorId = profile.Actor.ActorId, ActorType = profile.Actor.ActorType };
        Apply(row, profile);
        row.CreatedAtUtc = profile.CreatedAtUtc;
        return row;
    }

    private static void Apply(RadarProfileRow row, RadarProfile profile)
    {
        row.State = profile.State.ToString();
        row.EventTypesJson = JsonSerializer.Serialize(profile.EventTypes.Select(type => type.ToString()), JsonOptions);
        row.MinimumSeverity = profile.MinimumSeverity.ToString();
        row.MinimumImportance = profile.MinimumImportance;
        row.Sensitivity = profile.Sensitivity.ToString();
        row.DeliveryMode = profile.DeliveryMode.ToString();
        row.Version = profile.Version;
        row.ConcurrencyToken = profile.ConcurrencyToken;
        row.UpdatedAtUtc = profile.UpdatedAtUtc;
        row.RemovedAtUtc = profile.RemovedAtUtc;
    }

    private static RadarSymbolOverrideRow ToRow(RadarSymbolOverride value)
    {
        var row = new RadarSymbolOverrideRow { Id = value.Id, RadarProfileId = value.ProfileId,
            ExternalCompanyId = value.ExternalCompanyId };
        Apply(row, value);
        row.CreatedAtUtc = value.CreatedAtUtc;
        return row;
    }

    private static void Apply(RadarSymbolOverrideRow row, RadarSymbolOverride value)
    {
        row.State = value.State.ToString();
        row.EventTypesJson = value.EventTypes is null ? null :
            JsonSerializer.Serialize(value.EventTypes.Select(type => type.ToString()), JsonOptions);
        row.MinimumSeverity = value.MinimumSeverity?.ToString();
        row.MinimumImportance = value.MinimumImportance;
        row.Sensitivity = value.Sensitivity?.ToString();
        row.Version = value.Version;
        row.ConcurrencyToken = value.ConcurrencyToken;
        row.UpdatedAtUtc = value.UpdatedAtUtc;
        row.RemovedAtUtc = value.RemovedAtUtc;
    }

    private void AddAudit(
        RadarProfile profile,
        string action,
        string source,
        RadarSymbolOverride? symbolOverride)
    {
        dbContext.RadarPreferenceAudits.Add(new RadarPreferenceAuditRow
        {
            Id = Guid.NewGuid(), RadarProfileId = profile.Id, TenantId = profile.Actor.TenantId,
            ActorId = profile.Actor.ActorId, ActorType = profile.Actor.ActorType,
            Action = action, Source = source, Version = symbolOverride?.Version ?? profile.Version,
            SnapshotJson = JsonSerializer.Serialize<object>(symbolOverride is null
                ? new { profile.State, EventTypes = profile.EventTypes, profile.MinimumSeverity,
                    profile.MinimumImportance, profile.Sensitivity, profile.DeliveryMode }
                : new { symbolOverride.ExternalCompanyId, symbolOverride.State, EventTypes = symbolOverride.EventTypes,
                    symbolOverride.MinimumSeverity, symbolOverride.MinimumImportance, symbolOverride.Sensitivity }, JsonOptions),
            OccurredAtUtc = timeProvider.GetUtcNow()
        });
    }

    private static IReadOnlyCollection<InsightType>? DeserializeTypes(string? json) =>
        json is null ? null : (JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [])
            .Select(value => Enum.Parse<InsightType>(value)).ToArray();

    private static T? ParseNullable<T>(string? value) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : Enum.Parse<T>(value);
}
