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
}
