namespace Rva.Modules.GridTopology.Persistence;

/// <summary>
/// Who made a change, what kind of actor they are, and why. Applied to each write transaction as the rva.* settings the
/// versioning and event triggers record (.ai/rules/csharp-monolith.md section 9).
/// </summary>
/// <param name="Actor">A UUID, as text; the database rejects anything else.</param>
internal sealed record ChangeContext(string Actor, string ActorType, string Reason)
{
    /// <summary>The module itself, until requests carry an authenticated user or service (D17, risk R1).</summary>
    public static ChangeContext System(string reason) => new(SystemActor.Id.ToString(), SystemActor.ActorType, reason);
}

/// <summary>The fixed actor for every Phase 1 change (D17).</summary>
internal static class SystemActor
{
    /// <summary>A constant UUIDv7 that identifies the GridTopology module as a system actor.</summary>
    public static readonly Guid Id = Guid.Parse("019a3c00-0000-7000-8000-000000000001");

    public const string ActorType = "system";
}

/// <summary>The change context of the unit of work running in this scope, if any. Scoped.</summary>
internal sealed class ChangeContextAccessor
{
    public ChangeContext? Current { get; set; }
}
