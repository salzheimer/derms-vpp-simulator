using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Domain.GridState;

/// <summary>
/// A change in connectivity or energization of one grid node or one asset (<c>topology.grid_state_events</c>).
/// Append-only: the database rejects updates and deletes. <see cref="RecordedAt"/> and the actor come from the database
/// and the transaction's change context.
/// </summary>
internal sealed class GridStateEvent
{
    private GridStateEvent() { }

    public long Id { get; private set; }
    public GridNodeId? GridNodeId { get; private set; }
    public AssetId? AssetId { get; private set; }
    public int GridStateId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ActorId { get; private set; }
    public string? ActorType { get; private set; }

    /// <summary>Additional event data as JSON text; null = none.</summary>
    public string? Details { get; private set; }
}
