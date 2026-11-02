using DotNet.Testcontainers.Containers;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

namespace Rva.Modules.GridTopology.Integration.Tests.Seed;

/// <summary>
/// B-01, B-02, B-12, B-20, B-21: infrastructure/postgres/seed/seed.sh, run the way a developer runs it (psql inside the
/// database container), against a database built by rva_core_init.sql.
/// </summary>
public class SeedTests(PostgresContainerFixture postgres)
{
    // The totals dev_topology.sql's header documents; catalog.sql's row counts.
    private static readonly Dictionary<string, long> ExpectedRows = new()
    {
        ["topology.hardware_profiles"] = 14,
        ["topology.protocol_mappings"] = 7,
        ["topology.substations"] = 2,
        ["topology.feeders"] = 6,
        ["topology.neighborhood_transformers"] = 60,
        ["topology.premises"] = 486,
        ["topology.microgrids"] = 1,
        ["topology.assets"] = 554,
        ["topology.asset_components"] = 794,
        ["topology.grid_nodes"] = 2 + 6 + 60 + 486 + 1,
    };

    [Fact]
    public async Task Seeding_an_empty_database_loads_the_catalog_and_a_topology_later_steps_can_rely_on()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, cancellationToken);

        AssertSucceeded(await postgres.RunSeedAsync(database, cancellationToken));

        foreach (var (table, rows) in ExpectedRows)
            Assert.True(rows == await database.CountAsync(table, cancellationToken), $"{table}: expected {rows} rows");

        // What p1-s01c to p1-s01g build on: every asset is complete, sits where the domain allows it, and the
        // topology has the cases dispatch must handle.
        Assert.Equal(0L, await database.ScalarAsync<long>("""
            SELECT count(*) FROM topology.assets a
            WHERE NOT EXISTS (SELECT 1 FROM topology.asset_components c WHERE c.asset_id = a.asset_id)
            """, cancellationToken));
        Assert.Equal(0L, await database.ScalarAsync<long>("""
            SELECT count(*) FROM topology.assets a JOIN topology.interconnection_types it USING (interconnection_type_id)
            WHERE a.premise_id IS NULL AND it.internal_code <> 'ftm'
            """, cancellationToken));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM topology.feeders WHERE rated_capacity_kva IS NULL", cancellationToken));
        Assert.Equal(4L, await database.ScalarAsync<long>("""
            SELECT count(DISTINCT CASE WHEN premise_id IS NOT NULL THEN 'premise'
                                       WHEN neighborhood_transformer_id IS NOT NULL THEN 'transformer'
                                       WHEN feeder_id IS NOT NULL THEN 'feeder'
                                       ELSE 'substation' END)
            FROM topology.assets
            """, cancellationToken));

        // Microgrid members: the 24 homes on PDX-CED-F1-X01..X03, their 2 EV chargers, and the feeder's community
        // battery, all under the microgrid's own feeder.
        Assert.Equal(27L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM topology.assets WHERE microgrid_id IS NOT NULL", cancellationToken));
        Assert.Equal(0L, await database.ScalarAsync<long>("""
            SELECT count(*) FROM topology.assets a
            JOIN topology.microgrids m USING (microgrid_id)
            LEFT JOIN topology.premises p ON p.premise_id = a.premise_id
            LEFT JOIN topology.neighborhood_transformers x
                   ON x.neighborhood_transformer_id = COALESCE(a.neighborhood_transformer_id, p.neighborhood_transformer_id)
            WHERE COALESCE(a.feeder_id, p.feeder_id, x.feeder_id) IS DISTINCT FROM m.feeder_id
            """, cancellationToken));

        // Exactly one transformer has more inverter capacity behind it than its rating.
        Assert.Equal("PDX-ARB-F1-X01", await database.ScalarAsync<string>("""
            SELECT string_agg(x.neighborhood_transformer_code, ', ')
            FROM topology.neighborhood_transformers x
            WHERE x.rated_capacity_kva < (
                SELECT sum(a.rated_kva) FROM topology.assets a
                LEFT JOIN topology.premises p ON p.premise_id = a.premise_id
                WHERE x.neighborhood_transformer_id IN (a.neighborhood_transformer_id, p.neighborhood_transformer_id))
            """, cancellationToken));

        // Every lifecycle and participation state occurs, so later steps have rows of each.
        Assert.Equal(4L, await database.ScalarAsync<long>(
            "SELECT count(DISTINCT asset_lifecycle_state_id) FROM topology.assets", cancellationToken));
        Assert.Equal(4L, await database.ScalarAsync<long>(
            "SELECT count(DISTINCT participation_state_id) FROM topology.assets", cancellationToken));
    }

    [Fact]
    public async Task Running_the_seed_again_adds_no_rows_and_no_history()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, cancellationToken);
        AssertSucceeded(await postgres.RunSeedAsync(database, cancellationToken));
        var before = await CountEveryTopologyTableAsync(database, cancellationToken);

        var second = await postgres.RunSeedAsync(database, cancellationToken);

        AssertSucceeded(second);
        Assert.DoesNotContain("drift", second.Stderr, StringComparison.Ordinal);
        // grid_nodes included: an ON CONFLICT insert on a node table would leave an orphan registry row here.
        AssertSameCounts(before, await CountEveryTopologyTableAsync(database, cancellationToken));
        Assert.Equal(ExpectedRows["topology.grid_nodes"], before["topology.grid_nodes"]);
    }

    [Fact]
    public async Task A_changed_catalog_row_is_left_unchanged_and_reported_as_drift()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, cancellationToken);
        AssertSucceeded(await postgres.RunSeedAsync(database, cancellationToken));
        await database.ExecuteAsync("""
            UPDATE topology.hardware_profiles SET rated_kw = 12 WHERE hardware_profile_code = 'tesla_powerwall_3';
            UPDATE topology.protocol_mappings SET extra_attributes = '{"derType": 80}'
            WHERE protocol_standard = 'IEEE_2030_5' AND asset_type_code = '80';
            """, cancellationToken);

        var rerun = await postgres.RunSeedAsync(database, cancellationToken);

        AssertSucceeded(rerun);
        Assert.Contains("catalog drift", rerun.Stderr, StringComparison.Ordinal);
        Assert.Contains("topology.hardware_profiles.tesla_powerwall_3", rerun.Stderr, StringComparison.Ordinal);
        Assert.Contains("topology.protocol_mappings(IEEE_2030_5, DER, 80)", rerun.Stderr, StringComparison.Ordinal);
        Assert.Equal(12.0, await database.ScalarAsync<double>(
            "SELECT rated_kw FROM topology.hardware_profiles WHERE hardware_profile_code = 'tesla_powerwall_3'", cancellationToken));
        Assert.Equal("""{"derType": 80}""", await database.ScalarAsync<string>(
            "SELECT extra_attributes::text FROM topology.protocol_mappings WHERE protocol_standard = 'IEEE_2030_5' AND asset_type_code = '80'",
            cancellationToken));
    }

    [Fact]
    public async Task A_seed_that_fails_partway_commits_nothing_and_exits_non_zero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Empty, cancellationToken);
        var before = await CountEveryTopologyTableAsync(database, cancellationToken);

        // The real seed files, with an invalid component row (unit_count 0) after everything else has been inserted.
        var brokenDir = $"/tmp/seed-broken-{Guid.NewGuid():N}";
        var seedDir = Path.Combine(RepositoryPaths.Root, "infrastructure", "postgres", "seed");
        foreach (var file in new[] { "seed.sh", "catalog.sql" })
            await postgres.CopyToContainerAsync(await File.ReadAllTextAsync(Path.Combine(seedDir, file), cancellationToken), $"{brokenDir}/{file}", cancellationToken);
        await postgres.CopyToContainerAsync(
            await File.ReadAllTextAsync(Path.Combine(seedDir, "dev_topology.sql"), cancellationToken) + """

            INSERT INTO topology.asset_components (asset_id, technology_type_id, unit_count)
            SELECT asset_id, (SELECT technology_type_id FROM topology.technology_types WHERE internal_code = 'bess'), 0
            FROM topology.assets WHERE asset_code = 'PDX-ARB-F1-X01-H1-A1';
            """,
            $"{brokenDir}/dev_topology.sql", cancellationToken);

        var result = await postgres.RunSeedAsync(database, cancellationToken, brokenDir);

        Assert.NotEqual(0L, result.ExitCode);
        Assert.Contains("unit_count", result.Stderr, StringComparison.Ordinal);
        AssertSameCounts(before, await CountEveryTopologyTableAsync(database, cancellationToken));
    }

    [Fact]
    public async Task Seeding_a_database_without_the_schema_fails_at_once_with_a_clear_message()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.NoSchema, cancellationToken);

        var result = await postgres.RunSeedAsync(database, cancellationToken);

        Assert.Equal(3L, result.ExitCode);
        Assert.Contains("has no rva_core schema", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("rva_core_init.sql", result.Stderr, StringComparison.Ordinal);
    }

    private static void AssertSucceeded(ExecResult result) =>
        Assert.True(result.ExitCode == 0, $"seed.sh exited {result.ExitCode}:\n{result.Stdout}{result.Stderr}");

    /// <summary>Names each table whose row count changed, which a whole-dictionary comparison truncates.</summary>
    private static void AssertSameCounts(Dictionary<string, long> before, Dictionary<string, long> after)
    {
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.Empty(before.Where(b => after[b.Key] != b.Value).Select(b => $"{b.Key}: {b.Value} -> {after[b.Key]}"));
    }

    /// <summary>Row counts of every table in the topology schema, including the _history tables and grid_nodes.</summary>
    private static async Task<Dictionary<string, long>> CountEveryTopologyTableAsync(TestDatabase database, CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        await using (var connection = await database.OpenAsync(cancellationToken))
        await using (var command = new Npgsql.NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'topology' AND table_type = 'BASE TABLE' ORDER BY 1", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                tables.Add("topology." + reader.GetString(0));
        }

        var counts = new Dictionary<string, long>();
        foreach (var table in tables)
            counts[table] = await database.CountAsync(table, cancellationToken);
        return counts;
    }
}
