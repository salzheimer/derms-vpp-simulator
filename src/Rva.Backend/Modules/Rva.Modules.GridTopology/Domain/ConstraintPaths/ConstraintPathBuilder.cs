using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Domain.Assets;
using Rva.Modules.GridTopology.Domain.Hierarchy;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Domain.ConstraintPaths;

/// <summary>The hierarchy rows a set of assets' paths are resolved from, by id, all read as of the same time.</summary>
internal sealed class HierarchyRows
{
    public Dictionary<PremiseId, Premise> Premises { get; } = [];
    public Dictionary<TransformerId, NeighborhoodTransformer> Transformers { get; } = [];
    public Dictionary<FeederId, Feeder> Feeders { get; } = [];
    public Dictionary<SubstationId, Substation> Substations { get; } = [];
    public Dictionary<MicrogridId, Microgrid> Microgrids { get; } = [];
}

/// <summary>
/// Resolves an asset's constraint path from its connection point upward, following the normal configuration
/// (hierarchy foreign keys) as Phase 1 defines it. Pure: it reads only the rows it is given. A parent that is not among
/// them is an incomplete path, returned as <see cref="GridTopologyErrors.ConstraintPathIncomplete"/> rather than a path
/// missing a level, so a safety-relevant read never under-reports what constrains an asset (U5).
/// </summary>
internal static class ConstraintPathBuilder
{
    public static Result<ConstraintPath> Build(Asset asset, HierarchyRows rows)
    {
        var nodes = new List<PathNode>(4);
        Premise? premise = null;
        var transformerId = asset.TransformerId;
        var feederId = asset.FeederId;
        var substationId = asset.SubstationId;

        if (asset.PremiseId is { } premiseId)
        {
            if (!rows.Premises.TryGetValue(premiseId, out premise))
                return Incomplete(asset, GridNodeLevels.Premise, premiseId.Value);
            nodes.Add(new PathNode(premise, GridNodeLevels.Premise, premise.ServiceCapacityKva));
            transformerId = premise.TransformerId;
            feederId = premise.FeederId;
        }

        if (transformerId is { } xId)
        {
            if (!rows.Transformers.TryGetValue(xId, out var transformer))
                return Incomplete(asset, GridNodeLevels.NeighborhoodTransformer, xId.Value);
            nodes.Add(new PathNode(transformer, GridNodeLevels.NeighborhoodTransformer, transformer.RatedCapacityKva));
            feederId = transformer.FeederId;
        }

        if (feederId is { } fId)
        {
            if (!rows.Feeders.TryGetValue(fId, out var feeder))
                return Incomplete(asset, GridNodeLevels.Feeder, fId.Value);
            nodes.Add(new PathNode(feeder, GridNodeLevels.Feeder, feeder.RatedCapacityKva));
            substationId = feeder.SubstationId;
        }

        if (substationId is not { } sId)
            return Incomplete(asset, GridNodeLevels.Substation, null);
        if (!rows.Substations.TryGetValue(sId, out var substation))
            return Incomplete(asset, GridNodeLevels.Substation, sId.Value);
        nodes.Add(new PathNode(substation, GridNodeLevels.Substation, substation.RatedCapacityKva));

        Microgrid? microgrid = null;
        if (asset.MicrogridId is { } mId && !rows.Microgrids.TryGetValue(mId, out microgrid))
            return Incomplete(asset, GridNodeLevels.Microgrid, mId.Value);

        return new ConstraintPath(nodes, substation, premise, microgrid);
    }

    // The message names levels and ids only: it can reach a log and a problem response, never SQL or table names.
    private static Error Incomplete(Asset asset, string level, long? id) =>
        Error.Rule(GridTopologyErrors.ConstraintPathIncomplete, id is null
            ? $"Asset {asset.Code} has no complete constraint path: no {level} above it."
            : $"Asset {asset.Code} has no complete constraint path: its {level} (id {id}) is missing at the requested time.");
}
