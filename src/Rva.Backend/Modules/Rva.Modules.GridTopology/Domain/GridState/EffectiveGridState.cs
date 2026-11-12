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
    public static EffectiveGridState Derive(IReadOnlyCollection<PathNodeState> path, string? assetState) =>
        throw new NotImplementedException();
}
