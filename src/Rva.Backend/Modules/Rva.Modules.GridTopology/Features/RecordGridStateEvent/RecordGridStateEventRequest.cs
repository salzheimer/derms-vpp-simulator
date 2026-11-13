namespace Rva.Modules.GridTopology.Features.RecordGridStateEvent;

/// <summary>
/// The body of <c>POST /api/topology/grid-state-events</c>. Exactly one target: a node by level and code, or an asset
/// by code. Every member is nullable so the handler, not model binding, reports what is missing or wrong.
/// </summary>
/// <param name="Node">The grid node whose state changed; null when the target is an asset.</param>
/// <param name="AssetCode">The asset whose own state changed; null when the target is a node.</param>
/// <param name="State">The state the target entered, one of <c>GridStates</c>.</param>
/// <param name="OccurredAt">When the change happened in the field, as reported (ISO 8601 with a UTC offset).</param>
/// <param name="Reason">Why the state changed (D11).</param>
internal sealed record RecordGridStateEventRequest(
    RecordGridStateEventRequest.NodeTarget? Node,
    string? AssetCode,
    string? State,
    string? OccurredAt,
    string? Reason)
{
    /// <param name="Level">One of <c>GridNodeLevels</c>.</param>
    /// <param name="Code">The node's natural code, unique within its level.</param>
    internal sealed record NodeTarget(string? Level, string? Code);
}
