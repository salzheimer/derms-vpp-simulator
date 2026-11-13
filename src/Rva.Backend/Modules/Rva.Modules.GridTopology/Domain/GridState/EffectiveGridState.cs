using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;

namespace Rva.Modules.GridTopology.Domain.GridState;

/// <summary>One node on an asset's path with the state of its latest grid state event known at the time, if any.</summary>
/// <param name="Node">The node; its level decides where it sits on the walk.</param>
/// <param name="State">The latest event's state code (one of <c>GridStates</c>); null = no event, the normal state.</param>
internal sealed record PathNodeState(GridNodeRef Node, string? State);

/// <summary>
/// An asset's effective grid state, derived from the latest events on the nodes of its path and its own latest event
/// (GT-18). Pure: the executable spec is <c>pg_temp.asset_grid_states</c> in the pgTAP helpers.
/// </summary>
/// <param name="State">One of <c>GridStates</c>.</param>
/// <param name="CauseNode">The node whose event gave the asset this state; null when no node did.</param>
/// <param name="CausedByAsset">True when the asset's own event gave it this state.</param>
internal sealed record EffectiveGridState(string State, GridNodeRef? CauseNode, bool CausedByAsset)
{
    /// <summary>
    /// Walks <paramref name="path"/> from the substation down, in the order substation, feeder, microgrid,
    /// neighborhood transformer, premise, whatever order the nodes are given in, then applies the asset's own state.
    /// </summary>
    /// <param name="path">The asset's path nodes, any order; levels the asset sits above are absent.</param>
    /// <param name="assetState">The asset's own latest event state; null = no event.</param>
    /// <remarks>
    /// Rules, as in the reference: a node with no event, or whose latest event is <c>connected_energized</c>, changes
    /// nothing; <c>connected_deenergized</c> or <c>disconnected</c> de-energizes everything below it; <c>islanded</c>
    /// re-energizes what is below it from local sources. So a restoration below a dead node does not re-energize, and a
    /// dead node below an island de-energizes. The asset's own <c>disconnected</c> overrides the path; its own
    /// <c>connected_energized</c> (a reconnect) cannot re-energize a dead path. The cause is the last node on the walk
    /// that set the state, the one nearest the asset, or the asset itself.
    /// </remarks>
    public static EffectiveGridState Derive(IReadOnlyCollection<PathNodeState> path, string? assetState)
    {
        var state = GridStates.ConnectedEnergized;
        GridNodeRef? cause = null;

        foreach (var node in path.OrderBy(n => WalkRank(n.Node.Level)))
        {
            switch (node.State)
            {
                case GridStates.Islanded:
                    state = GridStates.Islanded;
                    cause = node.Node;
                    break;
                case GridStates.ConnectedDeenergized or GridStates.Disconnected:
                    state = GridStates.ConnectedDeenergized;
                    cause = node.Node;
                    break;
            }
        }

        return assetState == GridStates.Disconnected
            ? new EffectiveGridState(GridStates.Disconnected, null, CausedByAsset: true)
            : new EffectiveGridState(state, cause, CausedByAsset: false);
    }

    /// <summary>Where a level sits on the walk from the substation down.</summary>
    private static int WalkRank(string level) => level switch
    {
        GridNodeLevels.Substation => 0,
        GridNodeLevels.Feeder => 1,
        GridNodeLevels.Microgrid => 2,
        GridNodeLevels.NeighborhoodTransformer => 3,
        GridNodeLevels.Premise => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Not a grid node level."),
    };
}
