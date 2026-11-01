namespace Rva.SharedKernel.Identifiers;

/// <summary>
/// Identifies a grid node in the shared id space across levels (<c>topology.grid_nodes.grid_node_id</c>).
/// Any value is accepted: EF Core gives unsaved rows temporary (negative) keys.
/// </summary>
public readonly record struct GridNodeId
{
    public GridNodeId(long value) => Value = value;

    public long Value { get; }

    public static explicit operator long(GridNodeId id) => id.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
