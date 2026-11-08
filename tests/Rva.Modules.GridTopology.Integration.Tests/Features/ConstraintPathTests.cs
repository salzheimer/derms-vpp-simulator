using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Integration.Tests.Hosting;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure.FaultInjection;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Results;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Integration.Tests.Features;

/// <summary>
/// C-03 to C-07, C-12, C-30, C-32, C-33: <see cref="IGridTopologyQueries.GetConstraintPathAsync"/> on fixtures A-D
/// (GT-04, GT-05, GT-07, GT-09, GT-12), and how it fails: never a shorter path, never a success when the database is
/// down (U5).
/// </summary>
public class ConstraintPathTests(PostgresContainerFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_home_under_the_overloaded_transformer_is_constrained_by_every_node_up_to_the_substation()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXD-HOME-01-BESS");

        var path = (await context.QueryAsync(q => q.GetConstraintPathAsync(id, null, Token))).Value;

        var expected = new[]
        {
            new ConstraintNodeSnapshot(new GridNodeRef(await context.GridNodeIdAsync("premises", "FXD-HOME-01"), GridNodeLevels.Premise, "FXD-HOME-01"), new KilovoltAmperes(48)),
            new ConstraintNodeSnapshot(new GridNodeRef(await context.GridNodeIdAsync("neighborhood_transformers", "FXD-XFMR-1"), GridNodeLevels.NeighborhoodTransformer, "FXD-XFMR-1"), new KilovoltAmperes(25)),
            new ConstraintNodeSnapshot(new GridNodeRef(await context.GridNodeIdAsync("feeders", "FXD-FDR"), GridNodeLevels.Feeder, "FXD-FDR"), new KilovoltAmperes(6000)),
            new ConstraintNodeSnapshot(new GridNodeRef(await context.GridNodeIdAsync("substations", "FXD-SUB"), GridNodeLevels.Substation, "FXD-SUB"), new KilovoltAmperes(20000)),
        };
        Assert.Equal(expected, path.Nodes);
        Assert.All(path.Nodes, n => Assert.True(n.IsRated));
        Assert.Equal(new MicrogridSnapshot(
                new GridNodeRef(await context.GridNodeIdAsync("microgrids", "FXD-MG"), GridNodeLevels.Microgrid, "FXD-MG"),
                new Kilowatts(1000), new Kilowatts(500), CanIsland: true),
            path.Microgrid);
        Assert.Equal("FXD-HOME-01", path.Site?.Premise.Code);
    }

    [Fact]
    public async Task A_primary_service_premise_is_served_from_its_feeder_without_a_transformer()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        await context.ExecuteAsync("""
            INSERT INTO topology.premises (feeder_id, premise_code, customer_segment_id, service_capacity_kva)
            SELECT feeder_id, 'FXA-PRIMARY', (SELECT customer_segment_id FROM topology.customer_segments WHERE internal_code = 'commercial_industrial'), 1500
            FROM topology.feeders WHERE feeder_code = 'FXA-FDR';
            INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
            SELECT 'FXA-PRIMARY-BESS', premise_id, (SELECT interconnection_type_id FROM topology.interconnection_types WHERE internal_code = 'btm'), -250, 250, 250
            FROM topology.premises WHERE premise_code = 'FXA-PRIMARY';
            """);
        var id = await context.AssetIdAsync("FXA-PRIMARY-BESS");

        var path = (await context.QueryAsync(q => q.GetConstraintPathAsync(id, null, Token))).Value;

        Assert.Equal(new[] { (GridNodeLevels.Premise, "FXA-PRIMARY"), (GridNodeLevels.Feeder, "FXA-FDR"), (GridNodeLevels.Substation, "FXA-SUB") },
            path.Nodes.Select(n => (n.Node.Level, n.Node.Code)));
    }

    [Fact]
    public async Task A_feeder_connected_battery_starts_at_its_feeder_and_has_no_site_limits()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXD-COMM-BESS");

        var path = (await context.QueryAsync(q => q.GetConstraintPathAsync(id, null, Token))).Value;

        Assert.Equal(new[] { (GridNodeLevels.Feeder, "FXD-FDR"), (GridNodeLevels.Substation, "FXD-SUB") },
            path.Nodes.Select(n => (n.Node.Level, n.Node.Code)));
        Assert.Null(path.Site);
        Assert.Equal(new Kilowatts(500), path.EffectiveExportLimitKw);   // p_max 500, microgrid export 500
    }

    [Fact]
    public async Task Effective_export_limit_is_the_minimum_of_the_asset_site_and_microgrid_limits()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        // GT-12: a per-system limit above the premise cap does not raise it.
        await context.ExecuteAsync("UPDATE topology.assets SET export_limit_kw = 11 WHERE asset_code = 'FXC-BESS-1'");

        Assert.Equal(new Kilowatts(10), await ExportLimitAsync(context, "FXC-BESS-1"));   // premise cap 10 below 11.5 (GT-04)
        Assert.Equal(new Kilowatts(10), await ExportLimitAsync(context, "FXC-BESS-2"));
        Assert.Equal(new Kilowatts(0), await ExportLimitAsync(context, "FXB-PV"));        // non-export premise
        Assert.Equal(new Kilowatts(7.6), await ExportLimitAsync(context, "FXA-ASSET"));   // no limits: p_max

        await context.ExecuteAsync("UPDATE topology.assets SET export_limit_kw = 5 WHERE asset_code = 'FXA-ASSET'");
        Assert.Equal(new Kilowatts(5), await ExportLimitAsync(context, "FXA-ASSET"));     // per-system limit below p_max
    }

    [Fact]
    public async Task Effective_import_limit_is_the_minimum_of_charging_power_service_capacity_and_microgrid_import()
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        Assert.Equal(new Kilowatts(7.6), await ImportLimitAsync(context, "FXA-ASSET"));     // |p_min| 7.6 below the 48 kVA service
        Assert.Equal(new Kilowatts(125), await ImportLimitAsync(context, "FXB-BESS"));
        Assert.Equal(new Kilowatts(0), await ImportLimitAsync(context, "FXB-PV"));          // PV cannot import (p_min 0)
        Assert.Equal(new Kilowatts(500), await ImportLimitAsync(context, "FXD-COMM-BESS")); // microgrid import 1000 above |p_min|

        await context.ExecuteAsync("UPDATE topology.premises SET service_capacity_kva = 5 WHERE premise_code = 'FXA-PREM'");
        Assert.Equal(new Kilowatts(5), await ImportLimitAsync(context, "FXA-ASSET"));

        await context.ExecuteAsync("UPDATE topology.microgrids SET import_limit_kw = 0 WHERE microgrid_code = 'FXD-MG'");
        Assert.Equal(new Kilowatts(0), await ImportLimitAsync(context, "FXD-COMM-BESS"));
        Assert.Equal(new Kilowatts(0), await ImportLimitAsync(context, "FXD-HOME-01-BESS"));
    }

    [Fact]
    public async Task A_null_limit_is_no_limit_at_that_level_while_zero_is_a_limit()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        Assert.Equal(new Kilowatts(7.6), await ExportLimitAsync(context, "FXA-ASSET"));   // premise export NULL

        await context.ExecuteAsync("UPDATE topology.premises SET export_limit_kw = 0 WHERE premise_code = 'FXA-PREM'");
        Assert.Equal(new Kilowatts(0), await ExportLimitAsync(context, "FXA-ASSET"));

        await context.ExecuteAsync("UPDATE topology.microgrids SET export_limit_kw = NULL WHERE microgrid_code = 'FXD-MG'");
        Assert.Equal(new Kilowatts(500), await ExportLimitAsync(context, "FXD-COMM-BESS"));
        var unlimited = (await context.QueryAsync(async q => await q.GetConstraintPathAsync(await context.AssetIdAsync("FXD-COMM-BESS"), null, Token))).Value;
        Assert.Null(unlimited.Microgrid?.ExportLimitKw);

        // GT-12: a non-export PCC holds the 500 kW community battery at 0.
        await context.ExecuteAsync("UPDATE topology.microgrids SET export_limit_kw = 0 WHERE microgrid_code = 'FXD-MG'");
        Assert.Equal(new Kilowatts(0), await ExportLimitAsync(context, "FXD-COMM-BESS"));
    }

    [Fact]
    public async Task A_reparented_premise_has_a_different_path_before_and_after_the_move()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXD-HOME-05-BESS");
        var before = await context.NowAsync();
        await context.ExecuteAsync("""
            UPDATE topology.premises
            SET neighborhood_transformer_id = (SELECT neighborhood_transformer_id FROM topology.neighborhood_transformers WHERE neighborhood_transformer_code = 'FXD-XFMR-2')
            WHERE premise_code = 'FXD-HOME-05'
            """);

        var then = (await context.QueryAsync(q => q.GetConstraintPathAsync(id, before, Token))).Value;
        var now = (await context.QueryAsync(q => q.GetConstraintPathAsync(id, null, Token))).Value;

        Assert.Equal("FXD-XFMR-1", then.Nodes[1].Node.Code);
        Assert.Equal(new KilovoltAmperes(25), then.Nodes[1].RatedCapacityKva);
        Assert.Equal("FXD-XFMR-2", now.Nodes[1].Node.Code);
        Assert.Equal(new KilovoltAmperes(75), now.Nodes[1].RatedCapacityKva);
    }

    [Fact]
    public async Task An_unrated_feeder_is_reported_as_unrated_never_as_unbounded()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        await context.ExecuteAsync("UPDATE topology.feeders SET rated_capacity_kva = NULL WHERE feeder_code = 'FXA-FDR'");
        var id = await context.AssetIdAsync("FXA-ASSET");

        var path = (await context.QueryAsync(q => q.GetConstraintPathAsync(id, null, Token))).Value;

        var feeder = Assert.Single(path.Nodes, n => n.Node.Level == GridNodeLevels.Feeder);
        Assert.False(feeder.IsRated);
        Assert.Null(feeder.RatedCapacityKva);
        Assert.Equal(4, path.Nodes.Count);
    }

    [Fact]
    public async Task An_unknown_asset_is_not_found()
    {
        await using var context = await ReadContext.CreateAsync(postgres);

        var result = await context.QueryAsync(q => q.GetConstraintPathAsync(new SharedKernel.Identifiers.AssetId(987654), null, Token));

        Assert.Equal((ErrorKind.NotFound, GridTopologyErrors.AssetNotFound), (result.Error.Kind, result.Error.Code));
    }

    // ------------------------------------------------------------------ failures

    [Theory]
    [InlineData("substations", "substation_code", "FXA-SUB", "rated_capacity_kva = 31000")]
    [InlineData("feeders", "feeder_code", "FXA-FDR", "rated_capacity_kva = 8100")]
    [InlineData("neighborhood_transformers", "neighborhood_transformer_code", "FXA-XFMR", "rated_capacity_kva = 55")]
    [InlineData("premises", "premise_code", "FXA-PREM", "service_capacity_kva = 50")]
    public async Task A_path_missing_a_level_in_the_as_of_view_is_an_error_logged_at_Error_never_a_shorter_path(
        string table, string codeColumn, string code, string change)
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXA-ASSET");
        var before = await context.NowAsync();
        // FI-6: change the node, so its version valid before the change is in the history table, then lose that row.
        await context.ExecuteAsync($"UPDATE topology.{table} SET {change} WHERE {codeColumn} = '{code}'");
        await context.ExecuteAsync($"DELETE FROM topology.{table}_history WHERE {codeColumn} = '{code}'");

        var path = await context.QueryAsync(q => q.GetConstraintPathAsync(id, before, Token));
        var asset = await context.QueryAsync(q => q.GetAssetAsync(id, before, Token));

        Assert.Equal(GridTopologyErrors.ConstraintPathIncomplete, path.Error.Code);
        Assert.Equal(GridTopologyErrors.ConstraintPathIncomplete, asset.Error.Code);
        Assert.Equal(2, context.Module.Logs.Entries.Count(e => e.Level == LogLevel.Error && e.Message.Contains("FXA-ASSET", StringComparison.Ordinal)));
        // The current view is complete.
        Assert.True((await context.QueryAsync(q => q.GetConstraintPathAsync(id, null, Token))).IsSuccess);
    }

    [Fact]
    public async Task A_missing_microgrid_in_the_as_of_view_is_an_incomplete_path()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXD-HOME-01-BESS");
        var before = await context.NowAsync();
        await context.ExecuteAsync("UPDATE topology.microgrids SET export_limit_kw = 400 WHERE microgrid_code = 'FXD-MG'");
        await context.ExecuteAsync("DELETE FROM topology.microgrids_history WHERE microgrid_code = 'FXD-MG'");

        var path = await context.QueryAsync(q => q.GetConstraintPathAsync(id, before, Token));

        Assert.Equal(GridTopologyErrors.ConstraintPathIncomplete, path.Error.Code);
    }

    [Fact]
    public async Task An_incomplete_path_behind_the_asset_endpoint_is_a_sanitized_500_logged_once()
    {
        var database = await postgres.CreateDatabaseAsync(TestDatabaseTemplate.Fixtures, Token);
        var before = await database.NowAsync(Token);
        await database.ExecuteAsync("UPDATE topology.substations SET rated_capacity_kva = 31000 WHERE substation_code = 'FXA-SUB'", Token);
        await database.ExecuteAsync("DELETE FROM topology.substations_history WHERE substation_code = 'FXA-SUB'", Token);
        await using var host = new TopologyHostFactory(database.ConnectionString);
        using var client = host.CreateClient();

        var asOf = Uri.EscapeDataString(before.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var response = await client.GetAsync($"/api/topology/assets/FXA-ASSET?asOf={asOf}", Token);

        var traceId = await ProblemAssertions.AssertSanitizedProblemAsync(response, HttpStatusCode.InternalServerError, GridTopologyErrors.ConstraintPathIncomplete);
        var error = Assert.Single(host.Logs.Errors);
        Assert.NotNull(error.TraceId);
        Assert.Contains(error.TraceId.Value.ToHexString(), traceId, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/topology/assets/FXA-ASSET", Token)).StatusCode);
    }

    [Fact]
    public async Task A_read_cancelled_by_the_caller_throws_and_logs_no_error()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        // FI-7: cancel when the read reaches the premises.
        var hook = new CommandHook(sql => sql.Contains("premises", StringComparison.Ordinal), cancellation.Cancel);
        await using var context = await ReadContext.CreateAsync(postgres, configure: services =>
            services.ConfigureDbContext<GridTopologyDbContext>((_, options) => options.AddInterceptors(hook)));
        var id = await context.AssetIdAsync("FXA-ASSET");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.QueryAsync(q => q.GetConstraintPathAsync(id, null, cancellation.Token)));

        Assert.DoesNotContain(context.Module.Logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task A_contract_call_with_the_database_down_throws_rather_than_returning_a_path()
    {
        await using var module = ModuleServices.Create(HostWiringTests.Unreachable);
        await using var scope = module.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IGridTopologyQueries>();

        // EF wraps the connection failure; what matters is that the caller gets an exception carrying a transient
        // database fault (the host's 503), never a Result it could mistake for "no constraint".
        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => queries.GetConstraintPathAsync(new SharedKernel.Identifiers.AssetId(1), null, Token));
        Assert.Contains(Chain(thrown), e => e is NpgsqlException { IsTransient: true });
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            yield return current;
    }

    private static async Task<Kilowatts> ExportLimitAsync(ReadContext context, string assetCode)
    {
        var id = await context.AssetIdAsync(assetCode);
        return (await context.QueryAsync(q => q.GetConstraintPathAsync(id, null, Token))).Value.EffectiveExportLimitKw;
    }

    private static async Task<Kilowatts> ImportLimitAsync(ReadContext context, string assetCode)
    {
        var id = await context.AssetIdAsync(assetCode);
        return (await context.QueryAsync(q => q.GetConstraintPathAsync(id, null, Token))).Value.EffectiveImportLimitKw;
    }
}
