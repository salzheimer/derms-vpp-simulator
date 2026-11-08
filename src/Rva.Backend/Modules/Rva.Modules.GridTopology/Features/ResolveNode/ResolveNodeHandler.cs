using Microsoft.EntityFrameworkCore;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Features.Reads;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.ResolveNode;

/// <summary>
/// <see cref="IGridTopologyQueries.ResolveNodeAsync"/>: a node's grid node id from its level and code, current or as
/// of a time. Contract only. Each level is its own table, so a code is looked up at the given level only.
/// </summary>
internal sealed class ResolveNodeHandler(GridTopologyDbContext db, TimeProvider time)
{
    public async Task<Result<GridNodeRef>> HandleAsync(string level, string code, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        if (!GridNodeLevels.IsKnown(level))
            return Error.Validation(GridTopologyErrors.NodeQueryInvalid,
                $"Unknown grid node level '{level}'; expected one of {string.Join(", ", GridNodeLevels.All)}.");
        if (string.IsNullOrWhiteSpace(code))
            return Error.Validation(GridTopologyErrors.NodeQueryInvalid, "A node code is required.");
        if (AsOf.CheckNotInFuture(asOf, time) is { } invalid)
            return invalid;

        var ids = level switch
        {
            GridNodeLevels.Substation => AsOfReads.Substations(db, asOf).Where(n => n.Code == code).Select(n => n.GridNodeId),
            GridNodeLevels.Feeder => AsOfReads.Feeders(db, asOf).Where(n => n.Code == code).Select(n => n.GridNodeId),
            GridNodeLevels.NeighborhoodTransformer => AsOfReads.Transformers(db, asOf).Where(n => n.Code == code).Select(n => n.GridNodeId),
            GridNodeLevels.Premise => AsOfReads.Premises(db, asOf).Where(n => n.Code == code).Select(n => n.GridNodeId),
            GridNodeLevels.Microgrid => AsOfReads.Microgrids(db, asOf).Where(n => n.Code == code).Select(n => n.GridNodeId),
            _ => throw new InvalidOperationException($"Level {level} is known but not handled."),
        };
        var found = await ids.Select(id => (GridNodeId?)id).SingleOrDefaultAsync(cancellationToken);

        return found is { } gridNodeId
            ? new GridNodeRef(gridNodeId, level, code)
            : Error.NotFound(GridTopologyErrors.NodeNotFound,
                asOf is { } at ? $"No {level} has code {code} as of {AsOf.Format(at)}." : $"No {level} has code {code}.");
    }
}
