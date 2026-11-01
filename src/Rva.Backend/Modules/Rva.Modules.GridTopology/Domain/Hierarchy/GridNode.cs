using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Domain.Hierarchy;

/// <summary>
/// What every hierarchy level has in common: a natural code (unique within its level only) and an id in the shared
/// grid node id space, assigned by the database on insert and never changed. Not an EF inheritance root: each level is
/// its own table and entity type.
/// </summary>
internal abstract class GridNode
{
    public GridNodeId GridNodeId { get; private set; }
    public string Code { get; protected set; } = "";
}

/// <summary>Registry row giving a node its grid node id (<c>topology.grid_nodes</c>). Append-only.</summary>
internal sealed class GridNodeRegistration
{
    private GridNodeRegistration() { }

    public GridNodeId Id { get; private set; }
    public int GridNodeTypeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
