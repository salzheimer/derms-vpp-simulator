namespace Rva.Modules.GridTopology.Contracts;

/// <summary>
/// Error codes the GridTopology module returns in <c>Error.Code</c> and in the <c>code</c> member of its problem+json
/// responses. Stable: callers match on them.
/// </summary>
public static class GridTopologyErrors
{
    /// <summary>Conflict: the row changed since it was read (optimistic concurrency); re-read and retry.</summary>
    public const string ConcurrentChange = "GridTopology.ConcurrentChange";

    /// <summary>Conflict: the natural code is already used, including by a decommissioned asset.</summary>
    public const string DuplicateCode = "GridTopology.DuplicateCode";

    /// <summary>Validation: a field-reported time is later than the time it was recorded.</summary>
    public const string OccurredInFuture = "GridTopology.OccurredInFuture";

    /// <summary>Validation: a change needs a reason of 1 to 255 characters for the history tables.</summary>
    public const string ChangeReasonInvalid = "GridTopology.ChangeReasonInvalid";

    /// <summary>NotFound: no protocol mapping has this standard, resource category, and asset type code.</summary>
    public const string ProtocolMappingNotFound = "GridTopology.ProtocolMappingNotFound";

    /// <summary>Validation: a protocol mapping query needs a standard and non-blank resource category and asset type code.</summary>
    public const string ProtocolMappingQueryInvalid = "GridTopology.ProtocolMappingQueryInvalid";

    /// <summary>NotFound: no asset has this id or code, or it did not exist yet at the requested time.</summary>
    public const string AssetNotFound = "GridTopology.AssetNotFound";

    /// <summary>NotFound: no substation has this code, or it did not exist yet at the requested time.</summary>
    public const string SubstationNotFound = "GridTopology.SubstationNotFound";

    /// <summary>NotFound: no node at this level has this code, or it did not exist yet at the requested time.</summary>
    public const string NodeNotFound = "GridTopology.NodeNotFound";

    /// <summary>Validation: the level is not one of <see cref="GridNodeLevels"/>, or the code is blank.</summary>
    public const string NodeQueryInvalid = "GridTopology.NodeQueryInvalid";

    /// <summary>Validation: the requested <c>asOf</c> time is later than now; the topology's future is unknown.</summary>
    public const string AsOfInFuture = "GridTopology.AsOfInFuture";

    /// <summary>Validation: <c>asOf</c> is not an ISO 8601 date and time with a UTC offset (ex. <c>2026-11-07T08:00:00Z</c>).</summary>
    public const string AsOfInvalid = "GridTopology.AsOfInvalid";

    /// <summary>Validation: the page cursor is malformed, or belongs to another listing.</summary>
    public const string PageCursorInvalid = "GridTopology.PageCursorInvalid";

    /// <summary>Validation: the page size is outside the allowed range.</summary>
    public const string PageSizeInvalid = "GridTopology.PageSizeInvalid";

    /// <summary>Validation: the substation tree has more nodes and assets than one response may hold.</summary>
    public const string TreeTooLarge = "GridTopology.TreeTooLarge";

    /// <summary>
    /// Rule, but never the caller's fault: the stored topology has no complete path from an asset to its substation at
    /// the requested time (a parent row is missing from the main or history table). Safety-relevant reads fail with
    /// this rather than return a path missing a level (U5). It is logged at Error, and an endpoint answers 500.
    /// </summary>
    public const string ConstraintPathIncomplete = "GridTopology.ConstraintPathIncomplete";

    /// <summary>Validation: a grid state event names both a node and an asset as its target, or neither.</summary>
    public const string GridStateTargetInvalid = "GridTopology.GridStateTargetInvalid";

    /// <summary>Rule: no node at the given level has the given code, or no asset has the given code.</summary>
    public const string GridStateTargetNotFound = "GridTopology.GridStateTargetNotFound";

    /// <summary>Validation: the grid state code is not one of <see cref="GridStates"/>.</summary>
    public const string GridStateUnknown = "GridTopology.GridStateUnknown";

    /// <summary>Rule: <c>islanded</c> on a node that cannot island (a transformer, a feeder, a premise without <c>can_island</c>).</summary>
    public const string NodeCannotIsland = "GridTopology.NodeCannotIsland";

    /// <summary>Rule: an asset's own event may only be <c>disconnected</c> or <c>connected_energized</c> (D10).</summary>
    public const string AssetGridStateNotAllowed = "GridTopology.AssetGridStateNotAllowed";

    /// <summary>
    /// Validation: a grid state event's <c>occurredAt</c> is missing, or is not an ISO 8601 date and time with a UTC
    /// offset (ex. <c>2026-11-07T08:00:00Z</c>).
    /// </summary>
    public const string OccurredAtInvalid = "GridTopology.OccurredAtInvalid";
}
