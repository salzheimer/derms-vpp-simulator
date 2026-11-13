using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Integration.Tests.Hosting;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Integration.Tests.Features;

/// <summary>
/// D-04, D-05, D-22: <see cref="IGridTopologyQueries.GetGridStateAsync"/> on fixtures A-D. Events are written with
/// SQL, so only the read side is under test. Parity with the reference queries is D-02 (<c>GridStateParityTests</c>).
/// </summary>
public class GridStateTests(PostgresContainerFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------ D-04: late reports

    [Fact]
    public async Task D04_a_late_asset_report_is_not_seen_as_of_a_time_before_it_was_recorded_and_is_seen_after()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXD-OTHER-HOME-BESS");
        var occurred = await context.NowAsync();
        var between = await context.NowAsync();
        // Happened at "occurred", reported only now.
        await context.ExecuteAsync(GridStateEventSql.Asset("FXD-OTHER-HOME-BESS", GridStates.Disconnected, occurred));
        var recorded = await context.NowAsync();

        var before = (await context.QueryAsync(q => q.GetGridStateAsync(id, between, Token))).Value;
        var after = (await context.QueryAsync(q => q.GetGridStateAsync(id, recorded, Token))).Value;
        var now = (await context.QueryAsync(q => q.GetGridStateAsync(id, null, Token))).Value;

        Assert.True(between > occurred);
        Assert.Equal((GridStates.ConnectedEnergized, true), (before.State, before.IsRegistryEligible));
        Assert.Equal((GridStates.Disconnected, false), (after.State, after.IsRegistryEligible));
        Assert.Equal((GridStates.Disconnected, false), (now.State, now.IsRegistryEligible));
    }

    [Fact]
    public async Task D04_a_late_node_report_is_not_seen_as_of_a_time_before_it_was_recorded_and_is_seen_after()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXD-OTHER-HOME-BESS");
        var occurred = await context.NowAsync();
        var between = await context.NowAsync();
        await context.ExecuteAsync(GridStateEventSql.Node(GridNodeLevels.Feeder, "FXD-FDR-2", GridStates.ConnectedDeenergized, occurred));
        var recorded = await context.NowAsync();

        var before = (await context.QueryAsync(q => q.GetGridStateAsync(id, between, Token))).Value;
        var after = (await context.QueryAsync(q => q.GetGridStateAsync(id, recorded, Token))).Value;

        Assert.Equal(GridStates.ConnectedEnergized, before.State);
        Assert.Equal(GridStates.ConnectedDeenergized, after.State);
    }

    [Fact]
    public async Task D04_an_event_is_not_seen_as_of_a_time_before_it_occurred()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXD-OTHER-HOME-BESS");
        var before = await context.NowAsync();
        await context.ExecuteAsync(GridStateEventSql.Asset("FXD-OTHER-HOME-BESS", GridStates.Disconnected));

        var then = (await context.QueryAsync(q => q.GetGridStateAsync(id, before, Token))).Value;

        Assert.Equal(GridStates.ConnectedEnergized, then.State);
    }

    // ------------------------------------------------------------------ D-05: registry eligibility

    [Fact]
    public async Task D05_commissioned_active_and_energized_assets_are_registry_eligible_including_islanded_ones()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        await context.ExecuteAsync(GridStateEventSql.Node(GridNodeLevels.Feeder, "FXD-FDR", GridStates.ConnectedDeenergized));
        await context.ExecuteAsync(GridStateEventSql.Node(GridNodeLevels.Premise, "FXD-SCHOOL", GridStates.Islanded));

        var energized = await StateAsync(context, "FXD-OTHER-HOME-BESS");
        var islanded = await StateAsync(context, "FXD-SCHOOL-BESS");

        Assert.Equal((GridStates.ConnectedEnergized, true), (energized.State, energized.IsRegistryEligible));
        Assert.Equal((GridStates.Islanded, true), (islanded.State, islanded.IsRegistryEligible));
    }

    [Fact]
    public async Task D05_de_energized_and_disconnected_assets_are_not_registry_eligible()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        await context.ExecuteAsync(GridStateEventSql.Node(GridNodeLevels.Feeder, "FXD-FDR", GridStates.ConnectedDeenergized));
        await context.ExecuteAsync(GridStateEventSql.Asset("FXA-ASSET", GridStates.Disconnected));

        var deenergized = await StateAsync(context, "FXD-HOME-03-BESS");
        var disconnected = await StateAsync(context, "FXA-ASSET");

        Assert.Equal((GridStates.ConnectedDeenergized, false), (deenergized.State, deenergized.IsRegistryEligible));
        Assert.Equal((GridStates.Disconnected, false), (disconnected.State, disconnected.IsRegistryEligible));
    }

    [Theory]
    [InlineData("FXB-BESS", "suspended", null)]          // commissioned, energized, suspended
    [InlineData("FXB-PV", "opted_out", null)]            // commissioned, energized, opted out
    [InlineData("FXC-BESS-1", "not_enrolled", null)]     // commissioned, energized, not enrolled (GT-16)
    [InlineData("FXC-BESS-2", "opted_out", "decommissioned")]
    public async Task D05_energized_assets_that_are_not_commissioned_and_active_are_not_registry_eligible(
        string code, string participation, string? lifecycle)
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        await context.ExecuteAsync($"""
            UPDATE topology.assets
            SET participation_state_id = topology.participation_state_id('{participation}')
                {(lifecycle is null ? "" : $", asset_lifecycle_state_id = topology.asset_lifecycle_state_id('{lifecycle}'), decommissioned_date = DATE '2026-10-01'")}
            WHERE asset_code = '{code}'
            """);

        var state = await StateAsync(context, code);

        Assert.Equal((GridStates.ConnectedEnergized, false), (state.State, state.IsRegistryEligible));
    }

    [Theory]
    [InlineData("planned")]
    [InlineData("installed")]
    public async Task D05_an_asset_that_is_not_yet_commissioned_is_not_registry_eligible(string lifecycle)
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        await context.ExecuteAsync($"""
            INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva, asset_lifecycle_state_id)
            SELECT 'FXA-NEW', premise_id, (SELECT interconnection_type_id FROM topology.interconnection_types WHERE internal_code = 'btm'), -5, 5, 5,
                   topology.asset_lifecycle_state_id('{lifecycle}')
            FROM topology.premises WHERE premise_code = 'FXA-PREM'
            """);

        var state = await StateAsync(context, "FXA-NEW");

        Assert.Equal((GridStates.ConnectedEnergized, false), (state.State, state.IsRegistryEligible));
    }

    [Fact]
    public async Task D05_eligibility_follows_participation_as_of_the_requested_time()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXB-BESS");
        var before = await context.NowAsync();
        await context.ExecuteAsync("UPDATE topology.assets SET participation_state_id = topology.participation_state_id('suspended') WHERE asset_code = 'FXB-BESS'");

        var then = (await context.QueryAsync(q => q.GetGridStateAsync(id, before, Token))).Value;
        var now = (await context.QueryAsync(q => q.GetGridStateAsync(id, null, Token))).Value;

        Assert.True(then.IsRegistryEligible);
        Assert.False(now.IsRegistryEligible);
    }

    // ------------------------------------------------------------------ snapshot fields (plan section 3)

    [Fact]
    public async Task The_snapshot_names_the_asset_and_no_cause_when_nothing_has_happened()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXA-ASSET");

        var state = (await context.QueryAsync(q => q.GetGridStateAsync(id, null, Token))).Value;

        Assert.Equal(new GridStateSnapshot(id, "FXA-ASSET", GridStates.ConnectedEnergized, null, null, IsRegistryEligible: true), state);
    }

    [Fact]
    public async Task The_snapshot_names_the_node_that_de_energized_or_islanded_the_asset()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        await context.ExecuteAsync(GridStateEventSql.Node(GridNodeLevels.Feeder, "FXD-FDR", GridStates.ConnectedDeenergized));
        await context.ExecuteAsync(GridStateEventSql.Node(GridNodeLevels.Microgrid, "FXD-MG", GridStates.Islanded));
        await context.ExecuteAsync(GridStateEventSql.Node(GridNodeLevels.Feeder, "FXD-FDR-2", GridStates.ConnectedDeenergized));

        var islanded = await StateAsync(context, "FXD-HOME-01-BESS");
        var deenergized = await StateAsync(context, "FXD-OTHER-HOME-BESS");

        Assert.Equal(new GridNodeRef(await context.GridNodeIdAsync("microgrids", "FXD-MG"), GridNodeLevels.Microgrid, "FXD-MG"), islanded.CauseNode);
        Assert.Null(islanded.CauseAsset);
        Assert.Equal(new GridNodeRef(await context.GridNodeIdAsync("feeders", "FXD-FDR-2"), GridNodeLevels.Feeder, "FXD-FDR-2"), deenergized.CauseNode);
        Assert.Null(deenergized.CauseAsset);
    }

    [Fact]
    public async Task The_snapshot_names_the_asset_when_its_own_disconnect_caused_the_state()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        var id = await context.AssetIdAsync("FXA-ASSET");
        await context.ExecuteAsync(GridStateEventSql.Asset("FXA-ASSET", GridStates.Disconnected));

        var state = (await context.QueryAsync(q => q.GetGridStateAsync(id, null, Token))).Value;

        Assert.Equal(new GridStateSnapshot(id, "FXA-ASSET", GridStates.Disconnected, null, id, IsRegistryEligible: false), state);
    }

    // ------------------------------------------------------------------ D-22: fail-safe

    [Fact]
    public async Task D22_a_contract_call_with_the_database_down_throws_and_never_reports_energized()
    {
        await using var module = ModuleServices.Create(HostWiringTests.Unreachable);
        await using var scope = module.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IGridTopologyQueries>();

        // FI-1. U5: the caller gets an exception carrying a transient database fault, never a Result it could read as
        // "energized" or "eligible".
        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => queries.GetGridStateAsync(new AssetId(1), null, Token));
        Assert.Contains(Chain(thrown), e => e is NpgsqlException { IsTransient: true });
    }

    private static async Task<GridStateSnapshot> StateAsync(ReadContext context, string assetCode)
    {
        var id = await context.AssetIdAsync(assetCode);
        return (await context.QueryAsync(q => q.GetGridStateAsync(id, null, Token))).Value;
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            yield return current;
    }
}
