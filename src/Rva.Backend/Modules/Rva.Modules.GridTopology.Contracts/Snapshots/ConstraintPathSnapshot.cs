using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Contracts.Snapshots;

/// <summary>
/// The nodes and limits that constrain one asset at a point in time. Phase 1 derives the path from the normal
/// configuration of the hierarchy; a later network model may derive it from the energized path, so callers use this
/// instead of walking parent ids, and never cache it across topology versions (GT-09).
/// </summary>
/// <param name="Nodes">
/// The asset's connection node first, then each node above it up to and including the substation: premise →
/// neighborhood transformer → feeder → substation for a secondary-service premise, with levels the asset sits above left
/// out (a primary-service premise has no transformer; a feeder-connected asset starts at the feeder). Never missing a
/// level: a path that cannot be completed is the error <see cref="GridTopologyErrors.ConstraintPathIncomplete"/>.
/// </param>
/// <param name="Site">The premise and its site limits; null for an asset connected above the meter.</param>
/// <param name="Microgrid">The utility microgrid and its PCC limits; null = not a member.</param>
/// <param name="EffectiveExportLimitKw">
/// The most the asset may export at its connection point: the minimum of its rated maximum power, its per-system export
/// limit, the site export limit, and the microgrid export limit, never below 0. Null limits are skipped (D20).
/// </param>
/// <param name="EffectiveImportLimitKw">
/// The most the asset may import, as a non-negative magnitude: the minimum of its rated charging power (|p_min|), the
/// site service capacity, and the microgrid import limit. Null limits are skipped (D20).
/// </param>
/// <remarks>
/// Node ratings are shared by every asset below the node, so they are not folded into the per-asset effective limits:
/// dispatch allocates them.
/// </remarks>
public sealed record ConstraintPathSnapshot(
    AssetId AssetId,
    string AssetCode,
    IReadOnlyList<ConstraintNodeSnapshot> Nodes,
    SiteSnapshot? Site,
    MicrogridSnapshot? Microgrid,
    Kilowatts EffectiveExportLimitKw,
    Kilowatts EffectiveImportLimitKw);

/// <summary>One node on a constraint path, with its capacity rating.</summary>
/// <param name="RatedCapacityKva">
/// The node's rating (for a premise, its service capacity); null when the node has none recorded. An unrated node is
/// never unconstrained: treat it as unknown (Q1).
/// </param>
public sealed record ConstraintNodeSnapshot(GridNodeRef Node, KilovoltAmperes? RatedCapacityKva)
{
    /// <summary>Whether the node has a rating recorded.</summary>
    public bool IsRated => RatedCapacityKva is not null;
}
