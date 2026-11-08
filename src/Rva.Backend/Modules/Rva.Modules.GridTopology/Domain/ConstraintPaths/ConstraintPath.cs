using Rva.Modules.GridTopology.Domain.Hierarchy;

namespace Rva.Modules.GridTopology.Domain.ConstraintPaths;

/// <summary>
/// The nodes whose limits apply to one asset, from its connection node up to its substation, resolved from hierarchy
/// rows that were all read as of one time. Never missing a level: <see cref="ConstraintPathBuilder"/> refuses to build
/// an incomplete one.
/// </summary>
/// <param name="Nodes">Connection node first, substation last.</param>
/// <param name="Premise">The premise the asset is behind; null for an asset connected above the meter.</param>
/// <param name="Microgrid">The utility microgrid the asset is a member of, if any.</param>
internal sealed record ConstraintPath(
    IReadOnlyList<PathNode> Nodes,
    Substation Substation,
    Premise? Premise,
    Microgrid? Microgrid)
{
    public PathNode Connection => Nodes[0];
}

/// <summary>One node on a path, with its level and its capacity rating (a premise's is its service capacity).</summary>
internal sealed record PathNode(GridNode Node, string Level, double? RatedCapacityKva);
