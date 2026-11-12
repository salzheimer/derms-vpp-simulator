using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Integration.Tests.Parity;

/// <summary>
/// D-02 (GT-18): for every asset in fixtures A-D, at each step of a scripted sequence of grid state events, the
/// effective state and registry eligibility from <see cref="IGridTopologyQueries.GetGridStateAsync"/> equal the pgTAP
/// reference queries <c>pg_temp.asset_grid_states</c> and <c>pg_temp.dispatch_eligible</c>. Events are written with SQL,
/// so only the read side is under test.
/// </summary>
public class GridStateParityTests(PostgresContainerFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Csharp_grid_states_and_eligibility_equal_the_reference_queries_at_every_step()
    {
        await using var context = await ReadContext.CreateAsync(postgres);
        // As in gt18_effective_grid_state.sql: a premise on FXD-FDR outside the microgrid, to tell members from non-members.
        await context.ExecuteAsync("""
            INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id)
            SELECT neighborhood_transformer_id, 'D02-NONMEMBER', (SELECT customer_segment_id FROM topology.customer_segments WHERE internal_code = 'residential')
            FROM topology.neighborhood_transformers WHERE neighborhood_transformer_code = 'FXD-XFMR-4';
            INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva,
                                         asset_lifecycle_state_id, participation_state_id, in_service_date)
            SELECT 'D02-NONMEMBER-BESS', premise_id, (SELECT interconnection_type_id FROM topology.interconnection_types WHERE internal_code = 'btm'),
                   -5, 5, 5, topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), DATE '2024-06-01'
            FROM topology.premises WHERE premise_code = 'D02-NONMEMBER';
            """);
        var start = await context.NowAsync();

        string[][] script =
        [
            [GridStateEventSql.Node(GridNodeLevels.Feeder, "FXD-FDR", GridStates.ConnectedDeenergized)],
            [GridStateEventSql.Node(GridNodeLevels.Premise, "FXD-SCHOOL", GridStates.Islanded)],
            [GridStateEventSql.Node(GridNodeLevels.Microgrid, "FXD-MG", GridStates.Islanded)],
            [GridStateEventSql.Node(GridNodeLevels.NeighborhoodTransformer, "FXD-XFMR-2", GridStates.ConnectedDeenergized)],
            [
                GridStateEventSql.Asset("FXD-HOME-01-BESS", GridStates.Disconnected),
                "UPDATE topology.assets SET participation_state_id = topology.participation_state_id('suspended') WHERE asset_code = 'FXD-HOME-02-BESS'",
            ],
            [
                // A restoration below the dead feeder, an asset reconnect, and a late report that happened at the start.
                GridStateEventSql.Node(GridNodeLevels.NeighborhoodTransformer, "FXD-XFMR-3", GridStates.ConnectedEnergized),
                GridStateEventSql.Asset("FXD-HOME-04-BESS", GridStates.ConnectedEnergized),
                GridStateEventSql.Asset("FXD-OTHER-HOME-BESS", GridStates.Disconnected, start),
            ],
            [
                GridStateEventSql.Node(GridNodeLevels.Microgrid, "FXD-MG", GridStates.ConnectedEnergized),
                GridStateEventSql.Node(GridNodeLevels.Feeder, "FXD-FDR-2", GridStates.Disconnected),
                GridStateEventSql.Node(GridNodeLevels.Substation, "FXA-SUB", GridStates.ConnectedDeenergized),
                GridStateEventSql.Node(GridNodeLevels.Premise, "FXB-PREM", GridStates.Islanded),
            ],
            [
                GridStateEventSql.Node(GridNodeLevels.Feeder, "FXD-FDR", GridStates.ConnectedEnergized),
                GridStateEventSql.Asset("FXD-HOME-01-BESS", GridStates.ConnectedEnergized),
                GridStateEventSql.Node(GridNodeLevels.Premise, "FXA-PREM", GridStates.Disconnected),
            ],
        ];

        var checkpoints = new List<DateTimeOffset> { start };
        foreach (var step in script)
        {
            foreach (var sql in step)
                await context.ExecuteAsync(sql);
            checkpoints.Add(await context.NowAsync());
        }

        // Checked after the whole script, so every earlier checkpoint is read as of a time in the past; then current.
        foreach (var at in checkpoints.Cast<DateTimeOffset?>().Append(null))
        {
            var reference = await ReferenceQueries.AssetGridStatesAsync(context.Database, at ?? await context.NowAsync(), Token);
            Assert.Equal(31, reference.Count);   // A 1, B 2, C 3, D 24, and the non-member

            foreach (var (code, expected) in reference)
            {
                var id = await context.AssetIdAsync(code);
                var state = (await context.QueryAsync(q => q.GetGridStateAsync(id, at, Token))).Value;
                Assert.Equal((code, expected.GridState, expected.Eligible), (state.AssetCode, state.State, state.IsRegistryEligible));
                Assert.Equal(id, state.AssetId);
            }
        }

        // The script really moves states: not every checkpoint has the same answer.
        var first = await ReferenceQueries.AssetGridStatesAsync(context.Database, checkpoints[0], Token);
        var middle = await ReferenceQueries.AssetGridStatesAsync(context.Database, checkpoints[5], Token);
        Assert.NotEqual(first.Values.Count(v => v.Eligible), middle.Values.Count(v => v.Eligible));
    }
}
