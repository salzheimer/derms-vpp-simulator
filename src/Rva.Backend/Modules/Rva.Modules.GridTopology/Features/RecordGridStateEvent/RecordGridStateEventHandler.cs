using Microsoft.EntityFrameworkCore;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Domain.GridState;
using Rva.Modules.GridTopology.Features.Reads;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.RecordGridStateEvent;

/// <summary>
/// Records one grid state event on a node or an asset (GT-17), with the reason as its change context (D11). Asset
/// events may only be <c>disconnected</c> or <c>connected_energized</c> (D10); <c>islanded</c> only on a node that can
/// island (GT-17).
/// </summary>
/// <remarks>
/// The request is checked in this order, and nothing reaches the database until the request itself is valid:
/// <list type="number">
/// <item>Exactly one target, a node with a known level and a code, or an asset code: Validation
/// <see cref="GridTopologyErrors.GridStateTargetInvalid"/>.</item>
/// <item>A known state: Validation <see cref="GridTopologyErrors.GridStateUnknown"/>.</item>
/// <item><c>occurredAt</c> given with a UTC offset (<see cref="GridTopologyErrors.OccurredAtInvalid"/>) and not after
/// the app clock (<see cref="GridTopologyErrors.OccurredInFuture"/>). The database checks it again against its own
/// clock, which stamps <c>recorded_at</c>; that violation is the same Validation error (Q2).</item>
/// <item>A reason (D11), 1 to <see cref="UnitOfWork.MaxChangeReasonLength"/> characters:
/// <see cref="GridTopologyErrors.ChangeReasonInvalid"/>.</item>
/// <item>An asset's own state is <c>disconnected</c> or <c>connected_energized</c> (D10): Rule
/// <see cref="GridTopologyErrors.AssetGridStateNotAllowed"/>.</item>
/// <item>The target exists now: Rule <see cref="GridTopologyErrors.GridStateTargetNotFound"/>.</item>
/// <item><c>islanded</c> only on a microgrid or premise that can island: Rule
/// <see cref="GridTopologyErrors.NodeCannotIsland"/>.</item>
/// </list>
/// </remarks>
internal sealed class RecordGridStateEventHandler(
    GridTopologyDbContext db, UnitOfWork unitOfWork, LookupCatalog lookups, TimeProvider time)
{
    /// <summary>Every grid state code (<c>topology.grid_states</c>).</summary>
    private static readonly string[] KnownStates =
        [GridStates.ConnectedEnergized, GridStates.ConnectedDeenergized, GridStates.Islanded, GridStates.Disconnected];

    public async Task<Result> HandleAsync(RecordGridStateEventRequest request, CancellationToken cancellationToken)
    {
        if (ValidateTarget(request) is { } invalidTarget)
            return invalidTarget;

        var state = request.State;
        if (state is null || !KnownStates.Contains(state, StringComparer.Ordinal))
            return Error.Validation(GridTopologyErrors.GridStateUnknown,
                $"state must be one of {string.Join(", ", KnownStates)}.");

        if (!AsOf.TryParseInstant(request.OccurredAt, out var occurredAt))
            return Error.Validation(GridTopologyErrors.OccurredAtInvalid,
                "occurredAt is required: an ISO 8601 date and time with a UTC offset, ex. 2026-11-07T08:00:00Z or 2026-11-07T00:00:00-08:00.");
        if (AsOf.IsAfter(occurredAt, time.GetUtcNow()))
            return Error.Validation(GridTopologyErrors.OccurredInFuture, $"occurredAt {AsOf.Format(occurredAt)} is in the future.");

        var reason = request.Reason;
        if (string.IsNullOrWhiteSpace(reason))
            return Error.Validation(GridTopologyErrors.ChangeReasonInvalid,
                $"A grid state event needs a reason of 1 to {UnitOfWork.MaxChangeReasonLength} characters.");

        if (request.AssetCode is not null && state is not (GridStates.Disconnected or GridStates.ConnectedEnergized))
            return Error.Rule(GridTopologyErrors.AssetGridStateNotAllowed,
                $"An asset's own grid state event may only be {GridStates.Disconnected} or {GridStates.ConnectedEnergized}; " +
                "an asset is de-energized or islanded through the nodes above it.");

        // The reason (UnitOfWork checks its length) is also the transaction's change reason.
        return await unitOfWork.ExecuteAsync(reason, async ct =>
        {
            var gridStateId = await lookups.IdOfAsync(LookupTable.GridStates, state, ct);

            if (request.AssetCode is { } assetCode)
            {
                var assetId = await db.Assets.AsNoTracking()
                    .Where(a => a.Code == assetCode).Select(a => (AssetId?)a.Id).SingleOrDefaultAsync(ct);
                if (assetId is not { } id)
                    return Error.Rule(GridTopologyErrors.GridStateTargetNotFound, $"No asset has code {assetCode}.");

                db.GridStateEvents.Add(GridStateEvent.OnAsset(id, gridStateId, occurredAt, reason));
                return Result.Success();
            }

            var (level, code) = (request.Node!.Level!, request.Node.Code!);
            var node = await FindNodeAsync(level, code, ct);
            if (node is null)
                return Error.Rule(GridTopologyErrors.GridStateTargetNotFound, $"No {level} has code {code}.");
            if (state == GridStates.Islanded && !node.CanIsland)
                return Error.Rule(GridTopologyErrors.NodeCannotIsland,
                    $"The {level} {code} cannot island; only a microgrid or a premise that can island may be islanded.");

            db.GridStateEvents.Add(GridStateEvent.OnNode(node.GridNodeId, gridStateId, occurredAt, reason));
            return Result.Success();
        }, cancellationToken);
    }

    private static Error? ValidateTarget(RecordGridStateEventRequest request)
    {
        if ((request.Node is null) == (request.AssetCode is null))
            return Error.Validation(GridTopologyErrors.GridStateTargetInvalid,
                "A grid state event needs exactly one target: node (level and code) or assetCode.");
        if (request.Node is { } node)
        {
            if (!GridNodeLevels.IsKnown(node.Level))
                return Error.Validation(GridTopologyErrors.GridStateTargetInvalid,
                    $"node.level must be one of {string.Join(", ", GridNodeLevels.All)}.");
            if (string.IsNullOrWhiteSpace(node.Code))
                return Error.Validation(GridTopologyErrors.GridStateTargetInvalid, "node.code is required.");
        }
        else if (string.IsNullOrWhiteSpace(request.AssetCode))
            return Error.Validation(GridTopologyErrors.GridStateTargetInvalid, "assetCode must not be blank.");
        return null;
    }

    /// <summary>A node that exists now at this level with this code, and whether it can island.</summary>
    private sealed record NodeTarget(GridNodeId GridNodeId, bool CanIsland);

    // Substations, feeders, and transformers never island; a microgrid or premise only if it is built to (GT-17).
    private Task<NodeTarget?> FindNodeAsync(string level, string code, CancellationToken cancellationToken) => level switch
    {
        GridNodeLevels.Substation => db.Substations.AsNoTracking().Where(n => n.Code == code)
            .Select(n => new NodeTarget(n.GridNodeId, false)).SingleOrDefaultAsync(cancellationToken),
        GridNodeLevels.Feeder => db.Feeders.AsNoTracking().Where(n => n.Code == code)
            .Select(n => new NodeTarget(n.GridNodeId, false)).SingleOrDefaultAsync(cancellationToken),
        GridNodeLevels.NeighborhoodTransformer => db.NeighborhoodTransformers.AsNoTracking().Where(n => n.Code == code)
            .Select(n => new NodeTarget(n.GridNodeId, false)).SingleOrDefaultAsync(cancellationToken),
        GridNodeLevels.Premise => db.Premises.AsNoTracking().Where(n => n.Code == code)
            .Select(n => new NodeTarget(n.GridNodeId, n.CanIsland)).SingleOrDefaultAsync(cancellationToken),
        GridNodeLevels.Microgrid => db.Microgrids.AsNoTracking().Where(n => n.Code == code)
            .Select(n => new NodeTarget(n.GridNodeId, n.CanIsland)).SingleOrDefaultAsync(cancellationToken),
        _ => throw new InvalidOperationException($"Level {level} is known but not handled."),
    };
}
