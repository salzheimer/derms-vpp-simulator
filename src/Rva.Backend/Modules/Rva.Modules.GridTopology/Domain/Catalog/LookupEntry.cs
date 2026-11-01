namespace Rva.Modules.GridTopology.Domain.Catalog;

/// <summary>
/// A row of one of the topology lookup tables: a stable machine code and its display name. Lookups are seeded by
/// rva_core_init.sql and read by code (D22); the module never writes them. Not an EF inheritance root: each lookup
/// table is its own entity type.
/// </summary>
internal abstract class LookupEntry
{
    public int Id { get; private set; }
    public string Code { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
}

/// <summary>What kind of energy source or storage a component is (<c>topology.technology_types</c>).</summary>
internal sealed class TechnologyType : LookupEntry;

/// <summary>Meter position of an asset: btm, ftm, atm (<c>topology.interconnection_types</c>).</summary>
internal sealed class InterconnectionType : LookupEntry;

/// <summary>Utility service class of a premise (<c>topology.customer_segments</c>).</summary>
internal sealed class CustomerSegment : LookupEntry;

/// <summary>Which node table a grid node lives in (<c>topology.grid_node_types</c>).</summary>
internal sealed class GridNodeType : LookupEntry;

/// <summary>Physical lifecycle of an asset (<c>topology.asset_lifecycle_states</c>).</summary>
internal sealed class AssetLifecycleState : LookupEntry;

/// <summary>Program participation of an asset (<c>topology.participation_states</c>).</summary>
internal sealed class ParticipationState : LookupEntry;

/// <summary>Connectivity and energization of a node or asset (<c>topology.grid_states</c>).</summary>
internal sealed class GridStateType : LookupEntry
{
    public bool IsGridConnected { get; private set; }
    public bool IsEnergized { get; private set; }
}
