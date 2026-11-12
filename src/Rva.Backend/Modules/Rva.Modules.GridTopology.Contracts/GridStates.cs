namespace Rva.Modules.GridTopology.Contracts;

/// <summary>
/// The grid states, as the codes of <c>topology.grid_states</c>: connectivity and energization of a grid node or asset.
/// A node or asset with no grid state event is in its normal state, <see cref="ConnectedEnergized"/>.
/// </summary>
public static class GridStates
{
    /// <summary>Connected to the utility grid and energized: the normal state.</summary>
    public const string ConnectedEnergized = "connected_energized";

    /// <summary>Connected to the utility grid but de-energized, ex. by an upstream outage.</summary>
    public const string ConnectedDeenergized = "connected_deenergized";

    /// <summary>Disconnected from the utility grid but energized from local sources.</summary>
    public const string Islanded = "islanded";

    /// <summary>Disconnected and de-energized, ex. an asset's own disconnect opened.</summary>
    public const string Disconnected = "disconnected";
}
