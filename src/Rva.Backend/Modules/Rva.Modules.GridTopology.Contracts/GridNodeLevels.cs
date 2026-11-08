namespace Rva.Modules.GridTopology.Contracts;

/// <summary>
/// The levels of the grid hierarchy, as the codes of <c>topology.grid_node_types</c>. Node codes are unique within a
/// level only, so a node is always named by its level and code together.
/// </summary>
public static class GridNodeLevels
{
    public const string Substation = "substation";
    public const string Feeder = "feeder";
    public const string NeighborhoodTransformer = "neighborhood_transformer";
    public const string Premise = "premise";
    public const string Microgrid = "microgrid";

    /// <summary>Every level, top of the hierarchy first; microgrids last (they group nodes rather than sit between them).</summary>
    public static IReadOnlyList<string> All { get; } = [Substation, Feeder, NeighborhoodTransformer, Premise, Microgrid];

    /// <summary>Whether <paramref name="level"/> is one of <see cref="All"/>, compared ordinally.</summary>
    public static bool IsKnown(string? level) => level is not null && All.Contains(level, StringComparer.Ordinal);
}
