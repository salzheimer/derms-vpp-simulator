using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Contracts.Snapshots;

/// <summary>
/// One asset's effective grid state at a point in time, derived from the grid state events along its path and its own
/// events (GT-18), and whether the registry lets dispatch command it.
/// </summary>
/// <param name="AssetId">The asset.</param>
/// <param name="AssetCode">The asset's natural code.</param>
/// <param name="State">The effective state, one of <see cref="GridStates"/>.</param>
/// <param name="CauseNode">The grid node whose event gave the asset this state; null when no node did.</param>
/// <param name="CauseAsset">The asset itself when its own event gave it this state; null otherwise.</param>
/// <param name="IsRegistryEligible">
/// Commissioned, actively participating, and energized (connected and energized, or islanded). Real-time availability
/// comes from telemetry and is checked separately.
/// </param>
public sealed record GridStateSnapshot(
    AssetId AssetId,
    string AssetCode,
    string State,
    GridNodeRef? CauseNode,
    AssetId? CauseAsset,
    bool IsRegistryEligible);
