using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Domain.GridState;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Tests.Domain;

/// <summary>
/// D-01: the effective grid state derivation (GT-18), table-driven against the rules of its executable spec,
/// <c>pg_temp.asset_grid_states</c>: walking from the substation down (substation, feeder, microgrid, transformer,
/// premise), a de-energized or disconnected node de-energizes everything below it, an islanded node re-energizes what
/// is below it, a later <c>connected_energized</c> below a dead node does not re-energize, and the asset's own
/// <c>disconnected</c> overrides everything.
/// <para>
/// The cause is asserted only where one node or the asset alone produces the state. Which node is the cause when
/// several qualify (ex. a dead substation and a dead feeder) is not decided by the plan, so those cases check the
/// state only.
/// </para>
/// </summary>
public class EffectiveGridStateTests
{
    private const string Energized = GridStates.ConnectedEnergized;
    private const string Deenergized = GridStates.ConnectedDeenergized;
    private const string Islanded = GridStates.Islanded;
    private const string Disconnected = GridStates.Disconnected;

    private static readonly GridNodeRef Substation = new(new GridNodeId(1), GridNodeLevels.Substation, "SUB");
    private static readonly GridNodeRef Feeder = new(new GridNodeId(2), GridNodeLevels.Feeder, "FDR");
    private static readonly GridNodeRef Microgrid = new(new GridNodeId(3), GridNodeLevels.Microgrid, "MG");
    private static readonly GridNodeRef Transformer = new(new GridNodeId(4), GridNodeLevels.NeighborhoodTransformer, "XFMR");
    private static readonly GridNodeRef Premise = new(new GridNodeId(5), GridNodeLevels.Premise, "PREM");

    /// <summary>The nodes of the path in the order given, each with its latest event state (null = no event).</summary>
    private static PathNodeState[] Path(string? substation, string? feeder, string? microgrid, string? transformer, string? premise, bool withMicrogrid = true)
    {
        var nodes = new List<PathNodeState>
        {
            new(Substation, substation),
            new(Feeder, feeder),
        };
        if (withMicrogrid)
            nodes.Add(new(Microgrid, microgrid));
        nodes.Add(new(Transformer, transformer));
        nodes.Add(new(Premise, premise));
        return [.. nodes];
    }

    private static GridNodeRef? NodeByName(string? name) => name switch
    {
        null => null,
        "substation" => Substation,
        "feeder" => Feeder,
        "microgrid" => Microgrid,
        "transformer" => Transformer,
        "premise" => Premise,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    // substation, feeder, microgrid, transformer, premise, asset -> state, cause (a node, "asset", or null)
    [Theory]
    [InlineData(null, null, null, null, null, null, Energized, null)]                                     // no events: normal
    [InlineData(Deenergized, null, null, null, null, null, Deenergized, "substation")]                    // dead substation
    [InlineData(null, Deenergized, null, null, null, null, Deenergized, "feeder")]                        // dead feeder
    [InlineData(null, Disconnected, null, null, null, null, Deenergized, "feeder")]                       // a disconnected node de-energizes below it
    [InlineData(null, null, null, Deenergized, null, null, Deenergized, "transformer")]                   // dead transformer
    [InlineData(null, null, null, null, Deenergized, null, Deenergized, "premise")]                       // dead premise
    [InlineData(null, Deenergized, Islanded, null, null, null, Islanded, "microgrid")]                    // islanded microgrid under a dead feeder
    [InlineData(null, Deenergized, null, null, Islanded, null, Islanded, "premise")]                      // island-capable premise under a dead feeder
    [InlineData(null, null, Islanded, Deenergized, null, null, Deenergized, "transformer")]               // dead node below an island
    [InlineData(null, Deenergized, null, Energized, null, null, Deenergized, "feeder")]                   // a later connected_energized below a dead node
    [InlineData(null, null, null, null, null, Disconnected, Disconnected, "asset")]                       // the asset's own disconnect
    [InlineData(null, Deenergized, Islanded, null, null, Disconnected, Disconnected, "asset")]            // ... overrides an island
    [InlineData(Deenergized, Deenergized, null, Deenergized, Deenergized, Disconnected, Disconnected, "asset")] // ... and a dead path
    [InlineData(null, Deenergized, null, null, null, Energized, Deenergized, "feeder")]                   // an asset reconnect cannot re-energize a dead path
    public void Derives_the_state_and_its_single_cause(
        string? substation, string? feeder, string? microgrid, string? transformer, string? premise, string? asset,
        string expectedState, string? expectedCause)
    {
        var result = EffectiveGridState.Derive(Path(substation, feeder, microgrid, transformer, premise), asset);

        Assert.Equal(expectedState, result.State);
        Assert.Equal(expectedCause == "asset", result.CausedByAsset);
        Assert.Equal(expectedCause == "asset" ? null : NodeByName(expectedCause), result.CauseNode);
    }

    // Several nodes could be called the cause; the plan does not say which, so only the state is checked.
    [Theory]
    [InlineData(Deenergized, Deenergized, null, null, null, null, Deenergized)]          // dead substation and dead feeder
    [InlineData(Deenergized, null, Islanded, Deenergized, null, null, Deenergized)]      // dead node below an island under a dead substation
    [InlineData(null, Deenergized, Islanded, null, Islanded, null, Islanded)]            // islanded premise inside an islanded microgrid
    [InlineData(null, Deenergized, Islanded, Deenergized, Islanded, null, Islanded)]     // island below a dead node below an island
    [InlineData(null, null, null, null, null, Energized, Energized)]                     // an explicit asset reconnect on a normal path
    [InlineData(null, Energized, null, Energized, null, null, Energized)]                // explicit restorations on a normal path
    public void Derives_the_state_where_several_events_bear_on_it(
        string? substation, string? feeder, string? microgrid, string? transformer, string? premise, string? asset, string expectedState) =>
        Assert.Equal(expectedState, EffectiveGridState.Derive(Path(substation, feeder, microgrid, transformer, premise), asset).State);

    [Fact]
    public void Walks_the_path_from_the_substation_down_whatever_order_the_nodes_are_given_in()
    {
        // Bottom-up, as a constraint path lists them, with the microgrid last: the walk still puts the feeder above the
        // microgrid and the microgrid above the transformer.
        PathNodeState[] bottomUp =
        [
            new(Premise, null), new(Transformer, Deenergized), new(Feeder, Deenergized), new(Substation, null), new(Microgrid, Islanded),
        ];
        PathNodeState[] islandLast =
        [
            new(Premise, null), new(Transformer, null), new(Feeder, Deenergized), new(Substation, null), new(Microgrid, Islanded),
        ];

        var deadInsideIsland = EffectiveGridState.Derive(bottomUp, null);
        var island = EffectiveGridState.Derive(islandLast, null);

        Assert.Equal((Deenergized, Transformer), (deadInsideIsland.State, deadInsideIsland.CauseNode));
        Assert.Equal((Islanded, Microgrid), (island.State, island.CauseNode));
    }

    [Fact]
    public void A_feeder_connected_asset_outside_a_microgrid_has_only_its_feeder_and_substation_above_it()
    {
        PathNodeState[] path = [new(Feeder, Deenergized), new(Substation, null)];

        var result = EffectiveGridState.Derive(path, null);

        Assert.Equal((Deenergized, Feeder, false), (result.State, result.CauseNode, result.CausedByAsset));
    }

    [Fact]
    public void A_premise_asset_outside_a_microgrid_is_islanded_by_its_premise()
    {
        var result = EffectiveGridState.Derive(Path(null, Deenergized, null, null, Islanded, withMicrogrid: false), null);

        Assert.Equal((Islanded, Premise), (result.State, result.CauseNode));
    }
}
