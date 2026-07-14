using FinancialCopilot.Application.FinancialData.ProfessionalScanners;
using FinancialCopilot.Domain.Financial.ProfessionalScanners;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.ProfessionalScanners;

public sealed class SavedFilterRepository(FinancialIngestionDbContext dbContext) : ISavedFilterRepository
{
    public async Task<IReadOnlyCollection<SavedFilter>> ListAsync(
        SavedFilterActor actor, int page, int pageSize, CancellationToken cancellationToken) =>
        (await dbContext.SavedFilters.AsNoTracking()
            .Where(row => row.TenantId == actor.TenantId && row.ActorId == actor.ActorId &&
                          row.ActorType == actor.ActorType && row.RemovedAtUtc == null)
            .OrderByDescending(row => row.UpdatedAtUtc).ThenBy(row => row.Id)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 100))
            .Take(Math.Clamp(pageSize, 1, 100)).ToArrayAsync(cancellationToken))
        .Select(Map).ToArray();

    public Task<int> CountAsync(SavedFilterActor actor, CancellationToken cancellationToken) =>
        dbContext.SavedFilters.CountAsync(row => row.TenantId == actor.TenantId &&
            row.ActorId == actor.ActorId && row.ActorType == actor.ActorType && row.RemovedAtUtc == null,
            cancellationToken);

    public async Task<SavedFilter?> FindAsync(
        SavedFilterActor actor, Guid id, bool includeRemoved, CancellationToken cancellationToken)
    {
        var row = await dbContext.SavedFilters.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id &&
            item.TenantId == actor.TenantId && item.ActorId == actor.ActorId && item.ActorType == actor.ActorType &&
            (includeRemoved || item.RemovedAtUtc == null), cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task SaveAsync(SavedFilter value, CancellationToken cancellationToken)
    {
        var row = await dbContext.SavedFilters.SingleOrDefaultAsync(item => item.Id == value.Id, cancellationToken);
        if (row is null)
        {
            if (value.Version != 1) throw new SavedFilterValidationException("A new saved filter must start at version 1.");
            row = new SavedFilterRow { Id = value.Id, TenantId = value.Actor.TenantId,
                ActorId = value.Actor.ActorId, ActorType = value.Actor.ActorType, CreatedAtUtc = value.CreatedAtUtc };
            Apply(row, value);
            dbContext.SavedFilters.Add(row);
        }
        else
        {
            if (row.TenantId != value.Actor.TenantId || row.ActorId != value.Actor.ActorId || row.ActorType != value.Actor.ActorType)
                throw new SavedFilterValidationException("Saved filter belongs to another actor.");
            if (row.Version != value.Version - 1)
                throw new SavedFilterValidationException("Saved filter was changed by another request.");
            Apply(row, value);
        }
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException?.Message.Contains("UIX_SavedFilters_Actor_Name_Active", StringComparison.Ordinal) == true)
        { throw new SavedFilterValidationException("A saved filter with this name already exists."); }
    }

    private static SavedFilter Map(SavedFilterRow row) => SavedFilter.Rehydrate(row.Id,
        new SavedFilterActor(row.TenantId, row.ActorId, row.ActorType), row.Name, row.FilterCode,
        row.FilterVersion, row.ParametersJson, row.Version, row.ConcurrencyToken,
        row.CreatedAtUtc, row.UpdatedAtUtc, row.RemovedAtUtc);

    private static void Apply(SavedFilterRow row, SavedFilter value)
    {
        row.Name = value.Name;
        row.NormalizedName = value.Name.Trim().ToUpperInvariant();
        row.FilterCode = value.FilterCode;
        row.FilterVersion = value.FilterVersion;
        row.ParametersJson = value.ParametersJson;
        row.Version = value.Version;
        row.ConcurrencyToken = value.ConcurrencyToken;
        row.UpdatedAtUtc = value.UpdatedAtUtc;
        row.RemovedAtUtc = value.RemovedAtUtc;
    }
}
