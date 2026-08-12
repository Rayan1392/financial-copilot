using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FinancialCopilot.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlIntegrationCollection : ICollectionFixture<PostgreSqlIntegrationFixture>
{
    public const string Name = "Isolated PostgreSQL";
}

public sealed class PostgreSqlIntegrationFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;

    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithDatabase("postgres")
                .WithUsername("feature125_tests")
                .WithPassword("feature125_tests")
                .Build();
            await container.StartAsync();
        }
        catch (Exception exception)
        {
            UnavailableReason =
                $"Isolated PostgreSQL is unavailable; Docker/Testcontainers could not start: {exception.GetType().Name}: {exception.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }

    public async Task<PostgreSqlTestDatabase> CreateDatabaseAsync()
    {
        if (UnavailableReason is not null)
            Skip.If(true, UnavailableReason);

        var databaseName = $"feature125_{Guid.NewGuid():N}";
        var adminConnectionString = container!.GetConnectionString();
        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        var database = new PostgreSqlTestDatabase(adminConnectionString, databaseName, builder.ConnectionString);
        // Match the production migration order. One historical ingestion migration truncates
        // ProviderRawPayloads, which is owned by FinancialProviderDbContext in the same database.
        await using (var providerContext = database.CreateProviderContext())
            await providerContext.Database.MigrateAsync();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        return database;
    }
}

public sealed class PostgreSqlTestDatabase(
    string adminConnectionString,
    string databaseName,
    string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public FinancialIngestionDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseNpgsql(ConnectionString)
            .EnableDetailedErrors()
            .Options);

    public FinancialProviderDbContext CreateProviderContext() =>
        new(new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseNpgsql(ConnectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .EnableDetailedErrors()
            .Options);

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using (var terminate = connection.CreateCommand())
        {
            terminate.CommandText =
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid()";
            terminate.Parameters.AddWithValue("database", databaseName);
            await terminate.ExecuteNonQueryAsync();
        }
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await drop.ExecuteNonQueryAsync();
    }
}
