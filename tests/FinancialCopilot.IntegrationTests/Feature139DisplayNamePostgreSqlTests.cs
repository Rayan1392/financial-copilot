using FinancialCopilot.Application.Administration;
using FinancialCopilot.Infrastructure.Administration;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Feature 139 Revision 9: additive <c>auth_user_profiles.DisplayName</c> on a real PostgreSQL schema.</summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class Feature139DisplayNamePostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private const string Migration = "_Feature139UserProfileDisplayName";

    [SkippableFact]
    public async Task Migration_adds_a_nullable_unindexed_varchar100_column_keeps_existing_profiles_and_rolls_back()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        await using var auth = database.CreateAuthContext();
        var previousMigration = auth.Database.GetMigrations().Last(name => !name.EndsWith(Migration, StringComparison.Ordinal));
        await auth.Database.MigrateAsync(previousMigration);

        var tenantId = Guid.NewGuid();
        var user = NewUser();
        auth.Tenants.Add(new TenantRow { Id = tenantId, Name = "Feature 139 display name tenant" });
        auth.Users.Add(user);
        auth.UserTenants.Add(new UserTenantRow { TenantId = tenantId, UserId = user.Id, IsDefault = true });
        await auth.SaveChangesAsync();
        await ExecuteAsync(database.ConnectionString,
            """INSERT INTO auth_user_profiles ("UserId", "FirstName", "LastName", "CreatedAtUtc", "UpdatedAtUtc", "ProfileSyncedAtUtc") VALUES (@id, 'Ali', 'Rezaei', now(), now(), now())""",
            ("id", user.Id));

        await auth.Database.MigrateAsync();

        var column = await ReadColumnAsync(database.ConnectionString);
        Assert.NotNull(column);
        Assert.Equal("character varying", column!.Value.DataType);
        Assert.Equal(100, column.Value.MaximumLength);
        Assert.Equal("YES", column.Value.IsNullable);
        Assert.Null(column.Value.Default);
        Assert.Equal(0, await ScalarAsync<long>(database.ConnectionString,
            "SELECT count(*) FROM pg_indexes WHERE tablename = 'auth_user_profiles' AND indexdef LIKE '%DisplayName%'"));

        await using (var verify = database.CreateAuthContext())
        {
            var existing = await verify.UserProfiles.AsNoTracking().SingleAsync(row => row.UserId == user.Id);
            Assert.Null(existing.DisplayName);
            Assert.Equal("Ali", existing.FirstName);
            Assert.Equal("Rezaei", existing.LastName);
        }

        // Down path: the rollback drops only the new column.
        await auth.Database.MigrateAsync(previousMigration);
        Assert.Null(await ReadColumnAsync(database.ConnectionString));
        await auth.Database.MigrateAsync();
        Assert.NotNull(await ReadColumnAsync(database.ConnectionString));
    }

    [SkippableFact]
    public async Task Directory_displays_and_searches_DisplayName_with_first_last_fallback_on_postgresql()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        await using (var migrationContext = database.CreateAuthContext())
            await migrationContext.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var official = NewUser();
        var legacy = NewUser();
        var now = DateTimeOffset.UtcNow;
        await using (var seed = database.CreateAuthContext())
        {
            seed.Tenants.Add(new TenantRow { Id = tenantId, Name = "Directory display name tenant" });
            foreach (var user in new[] { official, legacy })
            {
                seed.Users.Add(user);
                seed.UserTenants.Add(new UserTenantRow { TenantId = tenantId, UserId = user.Id });
            }

            seed.UserProfiles.AddRange(
                new UserProfileRow { UserId = official.Id, DisplayName = "کامران عبدی", CreatedAtUtc = now, UpdatedAtUtc = now, ProfileSyncedAtUtc = now },
                new UserProfileRow { UserId = legacy.Id, FirstName = "Ali", LastName = "Rezaei", CreatedAtUtc = now, UpdatedAtUtc = now, ProfileSyncedAtUtc = now });
            await seed.SaveChangesAsync();
        }

        await using var auth = database.CreateAuthContext();
        await using var billing = database.CreateBillingContext();
        var queries = new EfCoreAdminUserQueries(auth, billing, TimeProvider.System, new EphemeralDataProtectionProvider(),
            NullLogger<EfCoreAdminUserQueries>.Instance);

        var byDisplayName = await queries.GetDirectoryAsync(
            tenantId, new AdminUserDirectoryQuery(Search: "عبدی"), false, false, false, CancellationToken.None);
        var officialItem = Assert.Single(byDisplayName.Items);
        Assert.Equal(official.Id, officialItem.UserId);
        Assert.Equal("کامران عبدی", officialItem.DisplayName);

        var byLegacyName = await queries.GetDirectoryAsync(
            tenantId, new AdminUserDirectoryQuery(Search: "ali rezaei"), false, false, false, CancellationToken.None);
        var legacyItem = Assert.Single(byLegacyName.Items);
        Assert.Equal(legacy.Id, legacyItem.UserId);
        Assert.Equal("Ali Rezaei", legacyItem.DisplayName);
    }

    private static FinancialCopilotUser NewUser()
    {
        var userName = $"tahlilapp-{Guid.NewGuid():N}";
        return new FinancialCopilotUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            IsEnabled = true
        };
    }

    private static async Task<(string DataType, int? MaximumLength, string IsNullable, string? Default)?> ReadColumnAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT data_type, character_maximum_length, is_nullable, column_default FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = 'auth_user_profiles' AND column_name = 'DisplayName'";
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, (string Name, object Value) parameter)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }
}
