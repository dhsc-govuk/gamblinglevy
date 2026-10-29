using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests.Common;

/// <summary>
/// The one PostgreSQL server a test run shares, and the throwaway database each test class gets on it.
///
/// Every test class used to boot its own Testcontainer and relied on Docker's random host-port mapping to keep the
/// classes apart. On Bitbucket that isolation failed: two classes running side by side intermittently landed on the
/// same Postgres, so one class's truncate or cleanup wiped the other's rows mid-test (ResetTestDataTests emptying
/// FundingSpendTests' organisation and form modules, the "schema gone" guards in ActivityQuestionProtocolTests). It
/// never reproduced on Docker Desktop. The suspected cause is Testcontainers 4.2+ publishing on both IPv4 and IPv6
/// against a daemon that allocates the two host ports separately (moby/moby#42442), so "localhost:port" can reach a
/// different container. Isolation now comes from a unique database name instead of a port: a class can only ever
/// connect to the database it created, and a wiring mistake fails loudly with "database does not exist" rather than
/// silently sharing data.
///
/// Where the server comes from:
/// - <see cref="ServerConnectionVariable"/> set (CI): that server. bitbucket-pipelines.yml runs it as a native
///   service container, so CI needs no Docker-in-Docker, no Testcontainers and no Ryuk.
/// - Unset (local): one Testcontainer started on first use and shared by every class in the run.
/// </summary>
public static class TestPostgres
{
    /// <summary>
    /// A server-level connection string (database <c>postgres</c>) to create the per-class databases on.
    /// </summary>
    public const string ServerConnectionVariable = "TEST_POSTGRES_CONNECTION";

    // A Bitbucket service container is started alongside the build container, not before it, and the postgres image
    // restarts once after initdb, so the first connection attempts can be refused or answered with 57P03.
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);

    private static readonly Lazy<Task<string>> ServerConnectionString = new(ResolveServerAsync);

    public static async Task<TestDatabase> CreateDatabaseAsync()
    {
        var server = await ServerConnectionString.Value;
        var name = $"gl_test_{Guid.NewGuid():N}";

        await ExecuteAsync(server, $"CREATE DATABASE \"{name}\"");

        // Capped well under the server's default max_connections (100), which a Bitbucket service cannot raise:
        // xUnit.MaxParallelThreads=2 means two classes' pools are live at once, and each class disposes its pool
        // before its database is dropped.
        var connectionString = new NpgsqlConnectionStringBuilder(server)
        {
            Database = name,
            MaxPoolSize = 20,
        }.ConnectionString;

        return new TestDatabase(name, connectionString);
    }

    public static async Task DropDatabaseAsync(TestDatabase database)
    {
        var server = await ServerConnectionString.Value;

        // FORCE terminates whatever the class's host still holds open, so a connection the host never returned to the
        // pool cannot leave the database behind.
        await ExecuteAsync(server, $"DROP DATABASE IF EXISTS \"{database.Name}\" WITH (FORCE)");
    }

    private static async Task<string> ResolveServerAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ServerConnectionVariable);
        var server = string.IsNullOrWhiteSpace(configured) ? await StartLocalServerAsync() : configured;

        await WaitUntilReadyAsync(server);

        return server;
    }

    private static async Task<string> StartLocalServerAsync()
    {
        var container = new PostgreSqlBuilder("postgres:15.1-alpine")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await container.StartAsync();

        // xUnit v2 has no assembly-level fixture to dispose this from. Ryuk removes it anyway when it is enabled; this
        // covers a local run with TESTCONTAINERS_RYUK_DISABLED set.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => container.DisposeAsync().AsTask().GetAwaiter().GetResult();

        return container.GetConnectionString();
    }

    private static async Task WaitUntilReadyAsync(string server)
    {
        var deadline = DateTime.UtcNow + ReadinessTimeout;
        while (true)
        {
            try
            {
                await ExecuteAsync(server, "SELECT 1");
                return;
            }
            catch (NpgsqlException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }
    }

    // Unpooled: these are a handful of one-off statements, and a pooled connection to the maintenance database would
    // sit idle for the rest of the run holding one of the server's connection slots.
    private static async Task ExecuteAsync(string server, string sql)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(server) { Pooling = false }.ConnectionString;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed record TestDatabase(string Name, string ConnectionString);