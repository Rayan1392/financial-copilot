using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

public sealed class Feature141TestMarker { }

/// <summary>
/// Loads only the test assembly's User Secrets and validates the one approved local Feature 141
/// reader endpoint before any caller can open a database connection.
/// </summary>
public static class Feature141LocalPostgreSqlTestConfiguration
{
    public const string ConnectionStringKey = "ConnectionStrings:Feature141PostgreSql";
    public const string OptInEnvironmentVariable = "FEATURE141_RUN_LOCAL_POSTGRES";

    public static IConfigurationRoot BuildUserSecretsConfiguration() =>
        new ConfigurationBuilder()
            .AddUserSecrets<Feature141TestMarker>(optional: true)
            .Build();

    internal static NpgsqlConnectionStringBuilder LoadForOptedInExecution(
        IConfiguration configuration,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!IsExplicitlyOptedIn(environment))
            throw new InvalidOperationException(
                $"Set {OptInEnvironmentVariable}=1 to enable the isolated Feature 141 local PostgreSQL test path.");

        var connectionString = configuration.GetConnectionString("Feature141PostgreSql");
        return ValidateConnectionString(connectionString);
    }

    public static NpgsqlConnectionStringBuilder LoadFromUserSecretsForOptedInExecution(
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (!IsExplicitlyOptedIn(environment))
            throw new InvalidOperationException(
                $"Set {OptInEnvironmentVariable}=1 to enable the isolated Feature 141 local PostgreSQL test path.");

        var configuration = BuildUserSecretsConfiguration();
        var connectionString = configuration.GetConnectionString("Feature141PostgreSql");
        (configuration as IDisposable)?.Dispose();
        return ValidateConnectionString(connectionString);
    }

    public static bool IsExplicitlyOptedIn(IReadOnlyDictionary<string, string?>? environment = null)
    {
        var value = environment is null
            ? Environment.GetEnvironmentVariable(OptInEnvironmentVariable)
            : environment.GetValueOrDefault(OptInEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.Ordinal);
    }

    internal static NpgsqlConnectionStringBuilder ValidateConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Required test-only connection setting '{ConnectionStringKey}' is missing.");

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                $"Test-only connection setting '{ConnectionStringKey}' is invalid; credential details are omitted.");
        }

        if (!string.Equals(builder.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || builder.Port != 5432
            || !string.Equals(builder.Database, "financialcopilot_feature141_test", StringComparison.Ordinal)
            || !string.Equals(builder.Username, "feature141_reader", StringComparison.Ordinal))
        {
            builder.Clear();
            throw new InvalidOperationException(
                $"Test-only connection setting '{ConnectionStringKey}' must target the designated local Feature 141 reader database and role; credential details are omitted.");
        }

        return builder;
    }
}
