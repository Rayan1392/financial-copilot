using FinancialCopilot.Domain.Financial.ProfessionalScanners;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.ProfessionalScanners;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

public sealed class ProfessionalSavedFilterPersistenceTests
{
    [Fact]
    public async Task Repository_EnforcesActorIsolationAndSoftDelete()
    {
        await using var db = CreateDb();
        var repository = new SavedFilterRepository(db);
        var tenant = Guid.NewGuid();
        var owner = new SavedFilterActor(tenant, Guid.NewGuid(), "User");
        var other = new SavedFilterActor(tenant, Guid.NewGuid(), "User");
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        var value = SavedFilter.Create(owner, "low pe", "LOW_PE", "1.0.0", "{\"maxPe\":\"5\"}", now);

        await repository.SaveAsync(value, CancellationToken.None);

        Assert.Single(await repository.ListAsync(owner, 1, 20, CancellationToken.None));
        Assert.Empty(await repository.ListAsync(other, 1, 20, CancellationToken.None));
        Assert.Null(await repository.FindAsync(other, value.Id, true, CancellationToken.None));

        value.Remove(1, now.AddMinutes(1));
        await repository.SaveAsync(value, CancellationToken.None);
        Assert.Empty(await repository.ListAsync(owner, 1, 20, CancellationToken.None));
        Assert.NotNull(await repository.FindAsync(owner, value.Id, true, CancellationToken.None));
    }

    [Fact]
    public void Model_HasActorNameAndCatalogReferenceIndexes()
    {
        using var db = CreateDb();
        var entity = db.Model.FindEntityType(typeof(SavedFilterRow));
        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index => index.GetDatabaseName() == "UIX_SavedFilters_Actor_Name_Active" && index.IsUnique);
        Assert.Contains(entity.GetIndexes(), index => index.GetDatabaseName() == "IX_SavedFilters_CatalogReference");
    }

    private static FinancialIngestionDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
