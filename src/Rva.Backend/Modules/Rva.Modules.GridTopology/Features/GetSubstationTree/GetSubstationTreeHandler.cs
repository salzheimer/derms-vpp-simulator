using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Domain.Assets;
using Rva.Modules.GridTopology.Features.Reads;
using Rva.Modules.GridTopology.Http;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.GetSubstationTree;

/// <summary>
/// One substation's subtree for the endpoint: feeders, transformers, premises, microgrids, and assets, current or as of
/// a time. Every level is read as of the same time and nested by id, top down. The response is bounded: a subtree with
/// more nodes and assets than <see cref="GridTopologyOptions.MaxSubstationTreeSize"/> is refused before its assets are
/// loaded (the bulk export for the simulator is the topology snapshot, plan p1-s01g).
/// </summary>
internal sealed class GetSubstationTreeHandler(GridTopologyDbContext db, LookupCatalog lookups, TimeProvider time, IOptions<GridTopologyOptions> options)
{
    public async Task<Result<SubstationTreeResponse>> HandleAsync(string substationCode, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        if (AsOf.CheckNotInFuture(asOf, time) is { } invalid)
            return invalid;

        var substation = await AsOfReads.Substations(db, asOf).SingleOrDefaultAsync(s => s.Code == substationCode, cancellationToken);
        if (substation is null)
            return Error.NotFound(GridTopologyErrors.SubstationNotFound,
                asOf is { } at ? $"No substation has code {substationCode} as of {AsOf.Format(at)}." : $"No substation has code {substationCode}.");

        var substationId = substation.Id;
        var feeders = await AsOfReads.Feeders(db, asOf).Where(f => f.SubstationId == substationId).ToListAsync(cancellationToken);
        var feederIds = feeders.Select(f => f.Id).ToList();
        var transformers = await AsOfReads.Transformers(db, asOf).Where(x => feederIds.Contains(x.FeederId)).ToListAsync(cancellationToken);
        var transformerIds = transformers.Select(x => x.Id).ToList();
        var premises = await AsOfReads.Premises(db, asOf)
            .Where(p => (p.TransformerId != null && transformerIds.Contains(p.TransformerId.Value))
                     || (p.FeederId != null && feederIds.Contains(p.FeederId.Value)))
            .ToListAsync(cancellationToken);
        var premiseIds = premises.Select(p => p.Id).ToList();
        var microgrids = await AsOfReads.Microgrids(db, asOf)
            .Where(m => m.SubstationId == substationId || (m.FeederId != null && feederIds.Contains(m.FeederId.Value)))
            .ToListAsync(cancellationToken);

        var assetQuery = AsOfReads.Assets(db, asOf).Where(a =>
            a.SubstationId == substationId
            || (a.FeederId != null && feederIds.Contains(a.FeederId.Value))
            || (a.TransformerId != null && transformerIds.Contains(a.TransformerId.Value))
            || (a.PremiseId != null && premiseIds.Contains(a.PremiseId.Value)));

        var limit = options.Value.MaxSubstationTreeSize;
        var nodeCount = 1 + feeders.Count + transformers.Count + premises.Count + microgrids.Count;
        var size = nodeCount + await assetQuery.CountAsync(cancellationToken);
        if (size > limit)
            return Error.Validation(GridTopologyErrors.TreeTooLarge,
                $"Substation {substationCode} has {size} nodes and assets, more than the {limit} one tree may hold. Request a topology snapshot instead.");

        var assets = await assetQuery.OrderBy(a => a.Code).ToListAsync(cancellationToken);
        var codes = await lookups.CodesAsync(cancellationToken);
        var microgridCodes = microgrids.ToDictionary(m => m.Id, m => m.Code);
        var feederCodes = feeders.ToDictionary(f => f.Id, f => f.Code);

        TreeAsset Asset(Asset a) => new(
            a.Id.Value, a.Code, codes.Of(LookupTable.InterconnectionTypes, a.InterconnectionTypeId),
            a.RatedPMin.Value, a.RatedPMax.Value, a.RatedKva.Value, a.ExportLimit?.Value,
            codes.Of(LookupTable.AssetLifecycleStates, a.LifecycleStateId),
            codes.Of(LookupTable.ParticipationStates, a.ParticipationStateId),
            a.MicrogridId is { } m && microgridCodes.TryGetValue(m, out var mc) ? mc : null);

        List<TreeAsset> AssetsWhere(Func<Asset, bool> connectedHere) => assets.Where(connectedHere).Select(Asset).ToList();

        List<PremiseTreeNode> PremisesWhere(Func<Domain.Hierarchy.Premise, bool> servedHere) => premises
            .Where(servedHere)
            .OrderBy(p => p.Code, StringComparer.Ordinal)
            .Select(p => new PremiseTreeNode(
                p.GridNodeId.Value, p.Code, codes.Of(LookupTable.CustomerSegments, p.CustomerSegmentId),
                p.ServiceCapacityKva, p.ExportLimitKw, p.CanIsland, GeoJsonGeometry.From(p.Location),
                AssetsWhere(a => a.PremiseId == p.Id)))
            .ToList();

        var tree = new SubstationTreeNode(
            substation.GridNodeId.Value,
            substation.Code,
            substation.RatedCapacityKva,
            GeoJsonGeometry.From(substation.Location),
            feeders.OrderBy(f => f.Code, StringComparer.Ordinal).Select(f => new FeederTreeNode(
                f.GridNodeId.Value, f.Code, f.NominalVoltageKv, f.RatedCapacityKva, GeoJsonGeometry.From(f.LineGeometry),
                transformers.Where(x => x.FeederId == f.Id).OrderBy(x => x.Code, StringComparer.Ordinal).Select(x => new TransformerTreeNode(
                    x.GridNodeId.Value, x.Code, x.RatedCapacityKva, x.SecondaryVoltageV, GeoJsonGeometry.From(x.Location),
                    PremisesWhere(p => p.TransformerId == x.Id),
                    AssetsWhere(a => a.TransformerId == x.Id))).ToList(),
                PremisesWhere(p => p.FeederId == f.Id),
                AssetsWhere(a => a.FeederId == f.Id))).ToList(),
            microgrids.OrderBy(m => m.Code, StringComparer.Ordinal).Select(m => new MicrogridTreeNode(
                m.GridNodeId.Value, m.Code, m.FeederId is { } fid && feederCodes.TryGetValue(fid, out var fc) ? fc : null,
                m.CanIsland, m.ImportLimitKw, m.ExportLimitKw, assets.Count(a => a.MicrogridId == m.Id))).ToList(),
            AssetsWhere(a => a.SubstationId == substationId));

        return new SubstationTreeResponse(asOf, tree);
    }
}
