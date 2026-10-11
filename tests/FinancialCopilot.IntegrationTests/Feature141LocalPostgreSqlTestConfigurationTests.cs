using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

public sealed class Feature141LocalPostgreSqlTestConfigurationTests
{
    private const string SentinelPassword = "DO_NOT_EMIT_TEST_SECRET";

    [Fact]
    public void TestAssemblyUsesDedicatedUserSecretsId()
    {
        var attribute = typeof(Feature141TestMarker).Assembly
            .GetCustomAttributes(typeof(UserSecretsIdAttribute), inherit: false)
            .Cast<UserSecretsIdAttribute>()
            .Single();

        Assert.Equal("644b18bc-2435-4b56-b041-cac7987bff32", attribute.UserSecretsId);
    }

    [Fact]
    public void ExplicitlyOptedInConfigurationLoadsDesignatedTestConnection()
    {
        var config = Configuration(ConnectionString());

        var connection = Feature141LocalPostgreSqlTestConfiguration.LoadForOptedInExecution(
            config, OptedInEnvironment());

        Assert.Equal("localhost", connection.Host);
        Assert.Equal(5432, connection.Port);
        Assert.Equal("financialcopilot_feature141_test", connection.Database);
        Assert.Equal("feature141_reader", connection.Username);
        Assert.Equal(SentinelPassword, connection.Password);
    }

    [Fact]
    public void MissingTestSecretIsRejectedWithoutApplicationConnectionFallback()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FinancialCopilot"] = ConnectionString(host: "application-db"),
                ["ConnectionStrings:Feature131PostgreSql"] = ConnectionString(host: "feature131-admin-db")
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Feature141LocalPostgreSqlTestConfiguration.LoadForOptedInExecution(
                config, OptedInEnvironment()));

        Assert.Contains(Feature141LocalPostgreSqlTestConfiguration.ConnectionStringKey, exception.Message);
        Assert.DoesNotContain(SentinelPassword, exception.Message);
    }

    [Fact]
    public void Feature131EnvironmentCannotEnableFeature141Execution()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING"] = ConnectionString(host: "localhost")
        };

        Assert.False(Feature141LocalPostgreSqlTestConfiguration.IsExplicitlyOptedIn(environment));
        Assert.Throws<InvalidOperationException>(() =>
            Feature141LocalPostgreSqlTestConfiguration.LoadForOptedInExecution(
                Configuration(ConnectionString()), environment));
    }

    [Theory]
    [InlineData("remote-host", 5432, "financialcopilot_feature141_test", "feature141_reader")]
    [InlineData("localhost", 5433, "financialcopilot_feature141_test", "feature141_reader")]
    [InlineData("localhost", 5432, "financial_copilot", "feature141_reader")]
    [InlineData("localhost", 5432, "financialcopilot_feature141_test", "maahfit")]
    public void IncorrectEndpointOrRoleIsRejectedBeforeConnection(
        string host, int port, string database, string username)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [Feature141LocalPostgreSqlTestConfiguration.ConnectionStringKey] =
                    ConnectionString(host, port, database, username)
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Feature141LocalPostgreSqlTestConfiguration.LoadForOptedInExecution(
                config, OptedInEnvironment()));

        Assert.DoesNotContain(SentinelPassword, exception.Message);
    }

    [Fact]
    public void MissingOptInRejectsEvenAValidTestConnection()
    {
        var config = Configuration(ConnectionString());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Feature141LocalPostgreSqlTestConfiguration.LoadForOptedInExecution(
                config, new Dictionary<string, string?>()));

        Assert.Contains(Feature141LocalPostgreSqlTestConfiguration.OptInEnvironmentVariable,
            exception.Message);
        Assert.DoesNotContain(SentinelPassword, exception.Message);
    }

    [Fact]
    public void InvalidConnectionStringIsRejectedWithoutEchoingCredentials()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Feature141LocalPostgreSqlTestConfiguration.ValidateConnectionString(
                $"Host=localhost;Database=financialcopilot_feature141_test;Username=feature141_reader;Password={SentinelPassword};UnsupportedKeyword=1"));

        Assert.DoesNotContain(SentinelPassword, exception.Message);
    }

    private static IConfigurationRoot Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [Feature141LocalPostgreSqlTestConfiguration.ConnectionStringKey] = connectionString
            })
            .Build();

    private static Dictionary<string, string?> OptedInEnvironment() => new(StringComparer.OrdinalIgnoreCase)
    {
        [Feature141LocalPostgreSqlTestConfiguration.OptInEnvironmentVariable] = "1"
    };

    private static string ConnectionString(
        string host = "localhost",
        int port = 5432,
        string database = "financialcopilot_feature141_test",
        string username = "feature141_reader") =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = SentinelPassword
        }.ConnectionString;
}
