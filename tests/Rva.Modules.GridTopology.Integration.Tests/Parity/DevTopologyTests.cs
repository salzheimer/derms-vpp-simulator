using System.Diagnostics;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Integration.Tests.Parity;

/// <summary>
/// C-14, C-15, C-34: the reads over the development seed (infrastructure/postgres/seed, loaded by seed.sh into the
/// test's own database; never into the fixtures).
/// </summary>
public class DevTopologyTests(PostgresContainerFixture postgres)
{
    private const int PageSize = 37;   // not a divisor of either substation's asset count, so last pages are partial

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>C-34 runs only when RVA_PERF_TESTS=1: it times calls rather than checking behavior.</summary>
    public static bool PerfTestsEnabled => Environment.GetEnvironmentVariable("RVA_PERF_TESTS") == "1";

    [Fact]
    public async Task Paging_every_substation_lists_every_asset_exactly_once_and_ignores_changes_made_while_paging()
    {
        await using var context = await SeededAsync();
        var expected = (await context.Database.ScalarAsync<string>(
            "SELECT string_agg(asset_id::text, ',' ORDER BY asset_id) FROM topology.assets", Token)).Split(',').Select(long.Parse).ToList();

        var listed = new List<long>();
        foreach (var substation in await SubstationCodesAsync(context))
        {
            var first = await ListAsync(context, substation, insertAfterFirstPage: substation == "PDX-SUB-ARBOR");
            var again = await ListAsync(context, substation, insertAfterFirstPage: false, asOf: first.AsOf);
            Assert.Equal(first.Ids, again.Ids);   // stable: the same pinned version gives the same pages
            listed.AddRange(first.Ids);
        }

        Assert.Equal(expected, listed.Order());
        Assert.Equal(listed.Count, listed.Distinct().Count());
    }

    [Fact]
    public async Task Every_dev_asset_has_a_complete_path_to_a_rated_substation_with_grid_node_ids()
    {
        await using var context = await SeededAsync();
        var ids = (await context.Database.ScalarAsync<string>("SELECT string_agg(asset_id::text, ',') FROM topology.assets", Token))
            .Split(',').Select(id => new AssetId(long.Parse(id))).ToList();
        Assert.Equal(554, ids.Count);

        foreach (var id in ids)
        {
            var path = (await context.QueryAsync(q => q.GetConstraintPathAsync(id, null, Token))).Value;
            var substation = path.Nodes[^1];
            Assert.Equal(GridNodeLevels.Substation, substation.Node.Level);
            Assert.True(substation.IsRated);
            Assert.All(path.Nodes, n => Assert.True(n.Node.GridNodeId.Value > 0));
            Assert.Equal(path.Nodes.Count, path.Nodes.Select(n => n.Node.Level).Distinct().Count());
        }
    }

    [Fact(Skip = "Opt-in timing check: set RVA_PERF_TESTS=1", SkipUnless = nameof(PerfTestsEnabled))]
    public async Task GetConstraintPath_stays_within_its_latency_budget_over_the_dev_seed()
    {
        // Input for the dispatch design (risk R6): one fresh read per asset, warm connection pool, local container.
        var budget = TimeSpan.FromMilliseconds(50);
        await using var context = await SeededAsync();
        var ids = (await context.Database.ScalarAsync<string>("SELECT string_agg(asset_id::text, ',') FROM topology.assets", Token))
            .Split(',').Select(id => new AssetId(long.Parse(id))).ToList();
        await context.QueryAsync(q => q.GetConstraintPathAsync(ids[0], null, Token));   // warm-up: lookups, pool, plans

        var timings = new List<TimeSpan>();
        foreach (var id in ids)
        {
            var watch = Stopwatch.StartNew();
            await context.QueryAsync(q => q.GetConstraintPathAsync(id, null, Token));
            timings.Add(watch.Elapsed);
        }

        timings.Sort();
        var p95 = timings[(int)(timings.Count * 0.95)];
        TestContext.Current.SendDiagnosticMessage($"GetConstraintPath over {ids.Count} assets: median {timings[timings.Count / 2].TotalMilliseconds:F1} ms, p95 {p95.TotalMilliseconds:F1} ms");
        Assert.True(p95 <= budget, $"p95 {p95.TotalMilliseconds:F1} ms is over the {budget.TotalMilliseconds} ms budget");
    }

    private async Task<ReadContext> SeededAsync()
    {
        var context = await ReadContext.CreateAsync(postgres, TestDatabaseTemplate.Empty);
        var seed = await postgres.RunSeedAsync(context.Database, Token);
        Assert.True(seed.ExitCode == 0, seed.Stdout + seed.Stderr);
        return context;
    }

    private static async Task<IReadOnlyList<string>> SubstationCodesAsync(ReadContext context) =>
        (await context.Database.ScalarAsync<string>("SELECT string_agg(substation_code, ',' ORDER BY substation_code) FROM topology.substations", Token)).Split(',');

    private static async Task<(List<long> Ids, DateTimeOffset AsOf)> ListAsync(
        ReadContext context, string substation, bool insertAfterFirstPage, DateTimeOffset? asOf = null)
    {
        var ids = new List<long>();
        string? cursor = null;
        DateTimeOffset? pinned = null;
        do
        {
            var page = (await context.QueryAsync(q => q.GetAssetsBySubstationAsync(substation, asOf, cursor, PageSize, Token))).Value;
            Assert.True(page.Assets.Count <= PageSize);
            Assert.All(page.Assets, a => Assert.Equal(substation, a.SubstationCode));
            pinned ??= page.AsOf;
            Assert.Equal(pinned, page.AsOf);
            ids.AddRange(page.Assets.Select(a => a.Id.Value));
            cursor = page.NextCursor;

            if (insertAfterFirstPage)
            {
                // Registered after the listing was pinned, with the highest asset id: a later page must not show it.
                await context.ExecuteAsync($"""
                    INSERT INTO topology.assets (asset_code, substation_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
                    SELECT 'DEV-PAGING-NEW', substation_id, (SELECT interconnection_type_id FROM topology.interconnection_types WHERE internal_code = 'ftm'), -100, 100, 100
                    FROM topology.substations WHERE substation_code = '{substation}'
                    """);
                insertAfterFirstPage = false;
            }
        }
        while (cursor is not null);

        Assert.Equal(ids.Order(), ids);
        return (ids, pinned!.Value);
    }
}
