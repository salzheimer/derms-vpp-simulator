using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Contracts.Snapshots;

/// <summary>One grid node: its id in the shared grid node id space, and its level and code.</summary>
/// <param name="GridNodeId">The reference other modules use for node-keyed data; never changes.</param>
/// <param name="Level">One of <see cref="GridNodeLevels"/>.</param>
/// <param name="Code">The node's natural code, unique within its level only.</param>
public sealed record GridNodeRef(GridNodeId GridNodeId, string Level, string Code);
