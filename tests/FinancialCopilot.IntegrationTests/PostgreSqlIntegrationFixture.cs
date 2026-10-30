using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Net;
using System.Text.RegularExpressions;
using Testcontainers.PostgreSql;

namespace FinancialCopilot.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlIntegrationCollection : ICollectionFixture<PostgreSqlIntegrationFixture>
{
    public const string Name = "Isolated PostgreSQL";
}

public sealed class PostgreSqlIntegrationFixture : IAsyncLifetime
{
    private const string TestConnectionStringEnvironmentVariable = "FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING";
    private PostgreSqlContainer? container;
    private string? configuredAdminConnectionString;

    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            var configuredConnectionString = Environment.GetEnvironmentVariable(TestConnectionStringEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredConnectionString))
            {
                var builder = new NpgsqlConnectionStringBuilder(configuredConnectionString);
                var isLoopback = string.Equals(builder.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                    (IPAddress.TryParse(builder.Host, out var address) && IPAddress.IsLoopback(address));
                if (!isLoopback || builder.Port is 5432 or 5433 ||
                    !string.Equals(builder.Database, "postgres", StringComparison.OrdinalIgnoreCase))
                {
                    UnavailableReason =
                        $"The {TestConnectionStringEnvironmentVariable} connection must target loopback, a non-application port, and the postgres admin database.";
                    return;
                }

                builder.Pooling = false;
                configuredAdminConnectionString = builder.ConnectionString;
                await using var connection = new NpgsqlConnection(configuredAdminConnectionString);
                await connection.OpenAsync();
                return;
            }

            container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("postgres")
                .WithUsername("feature125_tests")
                .WithPassword("feature125_tests")
                .Build();
            await container.StartAsync();
            configuredAdminConnectionString = container.GetConnectionString();
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
        var adminConnectionString = GetAdminConnectionString();
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

    public async Task<PostgreSqlTestDatabase> CreateBillingDatabaseAsync(string databasePrefix = "feature139_billing")
    {
        if (UnavailableReason is not null)
            Skip.If(true, UnavailableReason);

        if (!Regex.IsMatch(databasePrefix, "^[a-z0-9_]+$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Database prefix may contain only lowercase letters, digits, and underscores.", nameof(databasePrefix));

        var databaseName = $"{databasePrefix}_{Guid.NewGuid():N}";
        var adminConnectionString = GetAdminConnectionString();
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
        await using (var billing = database.CreateBillingContext())
            await billing.Database.MigrateAsync();
        return database;
    }

    private string GetAdminConnectionString() =>
        configuredAdminConnectionString ?? container?.GetConnectionString() ??
        throw new InvalidOperationException("PostgreSQL was not initialized.");
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

    public AuthDbContext CreateAuthContext(DbCommandInterceptor? commandInterceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(ConnectionString)
            .EnableDetailedErrors();
        if (commandInterceptor is not null)
            builder.AddInterceptors(commandInterceptor);
        return new AuthDbContext(builder.Options);
    }

    public BillingDbContext CreateBillingContext(DbCommandInterceptor? commandInterceptor = null)
    {
        var builder = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(ConnectionString)
            .EnableDetailedErrors();
        if (commandInterceptor is not null)
            builder.AddInterceptors(commandInterceptor);
        return new BillingDbContext(builder.Options);
    }

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
