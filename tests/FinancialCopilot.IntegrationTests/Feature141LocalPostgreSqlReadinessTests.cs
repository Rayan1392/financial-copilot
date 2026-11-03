using Npgsql;
using Xunit.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Opt-in, read-only readiness probe for the dedicated Feature 141 local database.</summary>
public sealed class Feature141LocalPostgreSqlReadinessTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task ReaderConnectionAndEffectivePrivilegesAreInspectedWithoutWrites()
    {
        Skip.IfNot(Feature141LocalPostgreSqlTestConfiguration.IsExplicitlyOptedIn(),
            "Set FEATURE141_RUN_LOCAL_POSTGRES=1 to run the local Feature 141 readiness probe.");

        var connectionBuilder =
            Feature141LocalPostgreSqlTestConfiguration.LoadFromUserSecretsForOptedInExecution();
        connectionBuilder.Pooling = false;
        connectionBuilder.Timeout = 5;
        connectionBuilder.CommandTimeout = 5;
        connectionBuilder.PersistSecurityInfo = false;
        connectionBuilder.Options = "-c default_transaction_read_only=on -c statement_timeout=5000 -c lock_timeout=1000";

        await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep provider messages and connection details (including any credentials) out of test output.
            throw new InvalidOperationException(
                $"Feature 141 reader connection failed; error category={exception.GetType().Name}.");
        }

        await using (var begin = new NpgsqlCommand("BEGIN READ ONLY", connection))
            await begin.ExecuteNonQueryAsync();

        try
        {
            await AssertTransactionIsReadOnlyAsync(connection);
            await InspectIdentityAndRoleAsync(connection);
            var databasePrivileges = await InspectDatabaseConnectPrivilegesAsync(connection);
            await InspectSchemaAndObjectPrivilegesAsync(connection);
            await InspectExistingFixtureMarkersAsync(connection);
            Assert.Equal(0, databasePrivileges.NonTestConnectGrants);
        }
        finally
        {
            await using var rollback = new NpgsqlCommand("ROLLBACK", connection);
            await rollback.ExecuteNonQueryAsync();
        }
    }

    private async Task AssertTransactionIsReadOnlyAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT current_setting('transaction_read_only')", connection);
        var setting = (string?)await command.ExecuteScalarAsync();
        output.WriteLine($"Current transaction read-only: {setting}.");
        Assert.Equal("on", setting);
    }

    private async Task InspectIdentityAndRoleAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT current_database(), current_user, current_setting('server_version'),
                   inet_server_addr()::text, inet_server_port(), r.rolsuper, r.rolcreatedb,
                   r.rolcreaterole, r.rolreplication, r.rolbypassrls,
                   pg_has_role(current_user, 'pg_read_all_data', 'MEMBER'),
                   pg_has_role(current_user, 'pg_write_all_data', 'MEMBER')
            FROM pg_catalog.pg_roles AS r
            WHERE r.rolname = current_user
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Authenticated role was not visible in pg_roles.");

        Assert.Equal("financialcopilot_feature141_test", reader.GetString(0));
        Assert.Equal("feature141_reader", reader.GetString(1));
        output.WriteLine($"Authenticated target: database={reader.GetString(0)}; role={reader.GetString(1)}; PostgreSQL={reader.GetString(2)}; server={reader.GetString(3)}:{reader.GetInt32(4)}.");
        output.WriteLine($"Role flags: superuser={reader.GetBoolean(5)}; createdb={reader.GetBoolean(6)}; createrole={reader.GetBoolean(7)}; replication={reader.GetBoolean(8)}; bypassrls={reader.GetBoolean(9)}; pg_read_all_data={reader.GetBoolean(10)}; pg_write_all_data={reader.GetBoolean(11)}.");

        Assert.False(reader.GetBoolean(5), "Reader role is a superuser.");
        Assert.False(reader.GetBoolean(6), "Reader role can create databases.");
        Assert.False(reader.GetBoolean(7), "Reader role can create roles.");
        Assert.False(reader.GetBoolean(8), "Reader role has replication privilege.");
        Assert.False(reader.GetBoolean(9), "Reader role bypasses row-level security.");
        Assert.False(reader.GetBoolean(11), "Reader role inherits pg_write_all_data.");
    }

    private async Task<(int NonTestConnectGrants, bool FinancialCopilotConnect, bool FinancialCopilotTemp)> InspectDatabaseConnectPrivilegesAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT d.datname,
                   pg_catalog.has_database_privilege(current_user, d.oid, 'CONNECT'),
                   pg_catalog.has_database_privilege(current_user, d.oid, 'CREATE'),
                   pg_catalog.has_database_privilege(current_user, d.oid, 'TEMP')
            FROM pg_catalog.pg_database AS d
            WHERE d.datallowconn AND NOT d.datistemplate
            ORDER BY d.datname
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var nonTestConnectGrants = 0;
        var financialCopilotConnect = false;
        var financialCopilotTemp = false;
        var nonTestTempGrants = 0;
        while (await reader.ReadAsync())
        {
            var database = reader.GetString(0);
            var canConnect = reader.GetBoolean(1);
            var canCreate = reader.GetBoolean(2);
            var canTemp = reader.GetBoolean(3);
            if (database != "financialcopilot_feature141_test" && canConnect)
                nonTestConnectGrants++;
            if (database != "financialcopilot_feature141_test" && canTemp)
                nonTestTempGrants++;
            if (database == "financial_copilot")
            {
                financialCopilotConnect = canConnect;
                financialCopilotTemp = canTemp;
            }
            if (database == "financialcopilot_feature141_test")
            {
                output.WriteLine($"Test database privileges: connect={canConnect}; create={canCreate}; temp={canTemp}.");
                Assert.False(canCreate, "Reader role can create schemas in the test database.");
                Assert.False(canTemp, "Reader role can create temporary tables in the test database.");
            }
        }

        output.WriteLine($"Protected database access: financial_copilot connect={financialCopilotConnect}; temp={financialCopilotTemp}; non-test databases with CONNECT={nonTestConnectGrants}; non-test databases with TEMP={nonTestTempGrants}.");
        return (nonTestConnectGrants, financialCopilotConnect, financialCopilotTemp);
    }

    private async Task InspectSchemaAndObjectPrivilegesAsync(NpgsqlConnection connection)
    {
        const string schemaSql = """
            SELECT n.nspname,
                   pg_catalog.has_schema_privilege(current_user, n.oid, 'USAGE'),
                   pg_catalog.has_schema_privilege(current_user, n.oid, 'CREATE')
            FROM pg_catalog.pg_namespace AS n
            WHERE n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg_%'
            ORDER BY n.nspname
            """;
        await using (var command = new NpgsqlCommand(schemaSql, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var schema = reader.GetString(0);
                var usage = reader.GetBoolean(1);
                var create = reader.GetBoolean(2);
                output.WriteLine($"Schema privilege: {schema}; usage={usage}; create={create}.");
                Assert.False(create, $"Reader role can create objects in schema {schema}.");
            }
        }

        const string objectSql = """
            SELECT n.nspname, c.relname, c.relkind,
                   pg_catalog.has_table_privilege(current_user, c.oid, 'SELECT'),
                   pg_catalog.has_table_privilege(current_user, c.oid, 'INSERT'),
                   pg_catalog.has_table_privilege(current_user, c.oid, 'UPDATE'),
                   pg_catalog.has_table_privilege(current_user, c.oid, 'DELETE'),
                   pg_catalog.has_table_privilege(current_user, c.oid, 'TRUNCATE')
            FROM pg_catalog.pg_class AS c
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p', 'v', 'm', 'f')
              AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg_%'
            ORDER BY n.nspname, c.relname
            """;
        await using var objectCommand = new NpgsqlCommand(objectSql, connection);
        await using var objectReader = await objectCommand.ExecuteReaderAsync();
        while (await objectReader.ReadAsync())
        {
            var schema = objectReader.GetString(0);
            var relation = objectReader.GetString(1);
            var kind = objectReader.GetChar(2);
            var select = objectReader.GetBoolean(3);
            var insert = objectReader.GetBoolean(4);
            var update = objectReader.GetBoolean(5);
            var delete = objectReader.GetBoolean(6);
            var truncate = objectReader.GetBoolean(7);
            output.WriteLine($"Existing object: {schema}.{relation}; kind={kind}; select={select}; insert={insert}; update={update}; delete={delete}; truncate={truncate}.");
            Assert.False(insert || update || delete || truncate,
                $"Reader role has write privileges on {schema}.{relation}.");
        }
    }

    private async Task InspectExistingFixtureMarkersAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT count(*)
            FROM pg_catalog.pg_class AS c
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p', 'v', 'm')
              AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg_%'
              AND (c.relname ILIKE '%feature141%' OR n.nspname ILIKE '%feature141%')
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        var count = (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Fixture marker inspection returned no result."));
        output.WriteLine($"Existing Feature 141 fixture-named relations: {count}.");
    }
}
