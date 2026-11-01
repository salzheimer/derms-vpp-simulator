using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Npgsql;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresContainerFixture))]

namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

/// <summary>Which template a test database is copied from.</summary>
public enum TestDatabaseTemplate
{
    /// <summary>rva_core_init.sql only: the schema and its seeded lookups.</summary>
    Empty,

    /// <summary>The schema plus pgTAP fixtures A-D (codes FXA- to FXD-).</summary>
    Fixtures,

    /// <summary>A bare database without the rva_core schema (copied from template0).</summary>
    NoSchema,
}

/// <summary>
/// One PostgreSQL container per test run, from the pgTAP test image (infrastructure/postgres/tests/Dockerfile), built
/// the way compose and run_tests.sh build rva_core: infrastructure/postgres/core is mounted over the whole init
/// directory, so the image's own PostGIS init (which would create a "topology" schema) never runs. The initialized
/// rva_core is the empty template; a copy with fixtures A-D loaded by psql is the fixtures template. Each test gets its
/// own database copied from one of them, so tests never share rows.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private const string Password = "test";
    private const string EmptyTemplateName = "rva_core";
    private const string FixturesTemplateName = "rva_fixtures";

    /// <summary>Where infrastructure/postgres/seed is mounted in the container.</summary>
    public const string SeedDirectory = "/seed";

    private static readonly string[] FixtureFiles =
    [
        "/tests/fixtures/_helpers.sql",
        "/tests/fixtures/fixture_a_hybrid_home.sql",
        "/tests/fixtures/fixture_b_nonexport_warehouse.sql",
        "/tests/fixtures/fixture_c_large_residential.sql",
        "/tests/fixtures/fixture_d_community_microgrid.sql",
    ];

    // CREATE DATABASE ... TEMPLATE fails if another session is using the template; one at a time across test classes.
    private readonly SemaphoreSlim createGate = new(1, 1);
    private IFutureDockerImage? image;
    private PostgreSqlContainer? container;
    private int databaseCount;

    private PostgreSqlContainer Container => container ?? throw new InvalidOperationException("The container has not started.");

    public async ValueTask InitializeAsync()
    {
        var postgresDir = Path.Combine(RepositoryPaths.Root, "infrastructure", "postgres");
        var testsDir = Path.Combine(postgresDir, "tests");

        image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(testsDir)
            .WithDockerfile("Dockerfile")
            .WithName("rva-core-db-test:dotnet-tests")
            .Build();
        await image.CreateAsync();

        container = new PostgreSqlBuilder(image)
            .WithDatabase(EmptyTemplateName)
            .WithUsername("postgres")
            .WithPassword(Password)
            .WithBindMount(Path.Combine(postgresDir, "core"), "/docker-entrypoint-initdb.d", AccessMode.ReadOnly)
            .WithBindMount(testsDir, "/tests", AccessMode.ReadOnly)
            .WithBindMount(Path.Combine(postgresDir, "seed"), SeedDirectory, AccessMode.ReadOnly)
            .Build();
        await container.StartAsync();
        await WaitForInitAsync();

        await PsqlAsync("postgres", ["-c", $"CREATE DATABASE {FixturesTemplateName} TEMPLATE {EmptyTemplateName}"]);
        // One session and one transaction: the fixtures use the pg_temp helpers _helpers.sql creates in its session.
        await PsqlAsync(FixturesTemplateName, ["-1", .. FixtureFiles.SelectMany(f => new[] { "-f", f })]);
    }

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
        if (image is not null)
            await image.DisposeAsync();
        createGate.Dispose();
    }

    /// <summary>A new database copied from <paramref name="template"/>, used by one test only.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync(TestDatabaseTemplate template, CancellationToken cancellationToken)
    {
        var name = $"test_{Interlocked.Increment(ref databaseCount)}";
        var source = template switch
        {
            TestDatabaseTemplate.Empty => EmptyTemplateName,
            TestDatabaseTemplate.Fixtures => FixturesTemplateName,
            TestDatabaseTemplate.NoSchema => "template0",
            _ => throw new ArgumentOutOfRangeException(nameof(template), template, null),
        };

        await createGate.WaitAsync(cancellationToken);
        try
        {
            // Not pooled: a pooled idle session on the postgres database is harmless, but keep admin work self-contained.
            await using var connection = new NpgsqlConnection(ConnectionStringFor("postgres") + ";Pooling=false");
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name} TEMPLATE {source}", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            createGate.Release();
        }

        return new TestDatabase(ConnectionStringFor(name));
    }

    /// <summary>
    /// Runs seed.sh from <paramref name="seedDirectory"/> inside the container against <paramref name="database"/>, as a
    /// developer would run it: psql over the local socket, configured by the standard libpq variables.
    /// </summary>
    public async Task<ExecResult> RunSeedAsync(TestDatabase database, CancellationToken cancellationToken, string seedDirectory = SeedDirectory) =>
        await Container.ExecAsync(["env", $"PGDATABASE={database.Name}", "PGUSER=postgres", "bash", $"{seedDirectory}/seed.sh"], cancellationToken);

    /// <summary>Runs SQL files (container paths) on <paramref name="database"/> in one transaction; throws if psql fails.</summary>
    public Task RunSqlFilesAsync(TestDatabase database, params string[] files) =>
        PsqlAsync(database.Name, ["-1", .. files.SelectMany(f => new[] { "-f", f })]);

    /// <summary>Writes a text file into the container, ex. a modified copy of a seed file.</summary>
    public Task CopyToContainerAsync(string content, string path, CancellationToken cancellationToken) =>
        Container.CopyAsync(System.Text.Encoding.UTF8.GetBytes(content), path, ct: cancellationToken);

    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(Container.GetConnectionString()) { Database = database }.ConnectionString;

    // The builder's readiness wait already needs a TCP connection, which the entrypoint only allows after the init
    // scripts; this also checks the init finished, as run_tests.sh does, so a failed init fails loudly here.
    private async Task WaitForInitAsync()
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var (stdout, stderr) = await Container.GetLogsAsync();
            if ((stdout + stderr).Contains("PostgreSQL init process complete", StringComparison.Ordinal))
                return;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        var logs = await Container.GetLogsAsync();
        throw new InvalidOperationException("rva_core initialization did not complete:\n" + logs.Stdout + logs.Stderr);
    }

    private async Task PsqlAsync(string database, IEnumerable<string> arguments)
    {
        var result = await Container.ExecAsync(["psql", "-X", "-q", "-v", "ON_ERROR_STOP=1", "-U", "postgres", "-d", database, .. arguments]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"psql on {database} failed ({result.ExitCode}):\n{result.Stdout}{result.Stderr}");
    }
}
