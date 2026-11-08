using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Domain.Assets;
using Rva.Modules.GridTopology.Domain.ConstraintPaths;
using Rva.Modules.GridTopology.Domain.Hierarchy;
using Rva.Modules.GridTopology.Persistence;
using Rva.Modules.GridTopology.Persistence.Configurations;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Features.Reads;

/// <summary>
/// The read side every asset query shares (GetAsset, GetConstraintPath, GetAssetsBySubstation): loads a batch of
/// assets and every row their snapshots and paths need, all as of one time, in a fixed number of queries whatever the
/// batch size, then builds each asset's <see cref="AssetSnapshot"/> and <see cref="ConstraintPathSnapshot"/>.
/// <para>
/// An asset whose path cannot be completed fails the whole read with
/// <see cref="GridTopologyErrors.ConstraintPathIncomplete"/>, logged here at Error: it means the stored topology is
/// corrupt, and no read that bounds dispatch may skip it or return a shorter path (U5).
/// </para>
/// </summary>
internal sealed partial class AssetReader(GridTopologyDbContext db, LookupCatalog lookups, ILogger<AssetReader> logger)
{
    /// <summary>One asset as read: its snapshot and its constraint path.</summary>
    public sealed record Readout(AssetSnapshot Asset, ConstraintPathSnapshot Path);

    /// <summary>
    /// The assets <paramref name="select"/> picks from the asset table as of <paramref name="asOf"/>, in the order it
    /// gives them. <paramref name="select"/> only filters and orders; the rest is loaded here.
    /// </summary>
    public Task<Result<IReadOnlyList<Readout>>> ReadAsync(
        Func<IQueryable<Asset>, IQueryable<Asset>> select, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        AsOfReads.InOneVersionAsync(db, asOf, () => ReadInOneVersionAsync(select, asOf, cancellationToken), cancellationToken);

    private async Task<Result<IReadOnlyList<Readout>>> ReadInOneVersionAsync(
        Func<IQueryable<Asset>, IQueryable<Asset>> select, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        var rows = await select(AsOfReads.Assets(db, asOf))
            .Select(a => new { Asset = a, ValidFrom = EF.Property<NpgsqlRange<DateTime>>(a, MappingExtensions.SysPeriod).LowerBound })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return Result.Success<IReadOnlyList<Readout>>([]);

        var assets = rows.Select(r => r.Asset).ToList();
        var hierarchy = await LoadHierarchyAsync(assets, asOf, cancellationToken);
        var components = await LoadComponentsAsync(assets, asOf, cancellationToken);
        var manufacturers = await LoadControlManufacturersAsync(assets, asOf, cancellationToken);
        var codes = await lookups.CodesAsync(cancellationToken);

        var readouts = new List<Readout>(rows.Count);
        foreach (var row in rows)
        {
            var asset = row.Asset;
            var path = ConstraintPathBuilder.Build(asset, hierarchy);
            if (path.IsFailure)
            {
                LogIncompletePath(asset.Code, asset.Id.Value, asOf, path.Error.Message);
                return path.Error;
            }

            var assetComponents = components.TryGetValue(asset.Id, out var found) ? found : [];
            readouts.Add(Map(asset, path.Value, assetComponents, ControlManufacturer(asset, manufacturers),
                new DateTimeOffset(DateTime.SpecifyKind(row.ValidFrom, DateTimeKind.Utc)), codes));
        }
        return Result.Success<IReadOnlyList<Readout>>(readouts);
    }

    private async Task<HierarchyRows> LoadHierarchyAsync(List<Asset> assets, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        var rows = new HierarchyRows();

        var microgridIds = Ids(assets.Select(a => a.MicrogridId));
        if (microgridIds.Count > 0)
            foreach (var m in await AsOfReads.Microgrids(db, asOf).Where(m => microgridIds.Contains(m.Id)).ToListAsync(cancellationToken))
                rows.Microgrids[m.Id] = m;

        var premiseIds = Ids(assets.Select(a => a.PremiseId));
        if (premiseIds.Count > 0)
            foreach (var p in await AsOfReads.Premises(db, asOf).Where(p => premiseIds.Contains(p.Id)).ToListAsync(cancellationToken))
                rows.Premises[p.Id] = p;

        var transformerIds = Ids(assets.Select(a => a.TransformerId).Concat(rows.Premises.Values.Select(p => p.TransformerId)));
        if (transformerIds.Count > 0)
            foreach (var x in await AsOfReads.Transformers(db, asOf).Where(x => transformerIds.Contains(x.Id)).ToListAsync(cancellationToken))
                rows.Transformers[x.Id] = x;

        var feederIds = Ids(assets.Select(a => a.FeederId)
            .Concat(rows.Premises.Values.Select(p => p.FeederId))
            .Concat(rows.Transformers.Values.Select(x => (FeederId?)x.FeederId)));
        if (feederIds.Count > 0)
            foreach (var f in await AsOfReads.Feeders(db, asOf).Where(f => feederIds.Contains(f.Id)).ToListAsync(cancellationToken))
                rows.Feeders[f.Id] = f;

        var substationIds = Ids(assets.Select(a => a.SubstationId)
            .Concat(rows.Feeders.Values.Select(f => (SubstationId?)f.SubstationId)));
        if (substationIds.Count > 0)
            foreach (var s in await AsOfReads.Substations(db, asOf).Where(s => substationIds.Contains(s.Id)).ToListAsync(cancellationToken))
                rows.Substations[s.Id] = s;

        return rows;
    }

    private async Task<Dictionary<AssetId, List<AssetComponent>>> LoadComponentsAsync(
        List<Asset> assets, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        var assetIds = assets.Select(a => a.Id).ToList();
        var components = await AsOfReads.AssetComponents(db, asOf)
            .Where(c => assetIds.Contains(c.AssetId))
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);
        return components.GroupBy(c => c.AssetId).ToDictionary(g => g.Key, g => g.ToList());
    }

    private async Task<Dictionary<int, string>> LoadControlManufacturersAsync(
        List<Asset> assets, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        var profileIds = assets.Where(a => a.ControlHardwareProfileId is not null).Select(a => a.ControlHardwareProfileId!.Value).Distinct().ToList();
        if (profileIds.Count == 0)
            return [];
        return await AsOfReads.HardwareProfiles(db, asOf)
            .Where(p => profileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Manufacturer, cancellationToken);
    }

    // The foreign key makes a missing profile impossible for current rows; as of a time it means a history row is gone.
    private static string? ControlManufacturer(Asset asset, Dictionary<int, string> manufacturers) =>
        asset.ControlHardwareProfileId is not { } profileId ? null
        : manufacturers.TryGetValue(profileId, out var manufacturer) ? manufacturer
        : throw new InvalidOperationException($"Control hardware profile {profileId} of asset {asset.Code} is missing at the requested time.");

    private static Readout Map(
        Asset asset, ConstraintPath path, List<AssetComponent> components, string? controlManufacturer, DateTimeOffset validFrom, LookupCodes codes)
    {
        var site = path.Premise is { } premise
            ? new SiteSnapshot(
                Ref(premise, GridNodeLevels.Premise),
                codes.Of(LookupTable.CustomerSegments, premise.CustomerSegmentId),
                Kva(premise.ServiceCapacityKva),
                Kw(premise.ExportLimitKw),
                premise.CanIsland)
            : null;
        var microgrid = path.Microgrid is { } m
            ? new MicrogridSnapshot(Ref(m, GridNodeLevels.Microgrid), Kw(m.ImportLimitKw), Kw(m.ExportLimitKw), m.CanIsland)
            : null;

        var snapshot = new AssetSnapshot(
            asset.Id,
            asset.Code,
            Ref(path.Connection.Node, path.Connection.Level),
            path.Substation.Id,
            path.Substation.Code,
            new InterconnectionTypeCode(codes.Of(LookupTable.InterconnectionTypes, asset.InterconnectionTypeId)),
            asset.RatedPMin,
            asset.RatedPMax,
            asset.RatedKva,
            asset.ExportLimit,
            site,
            microgrid,
            AssetMakeup.Derive(components.Select(c => codes.Of(LookupTable.TechnologyTypes, c.TechnologyTypeId))),
            Kwh(AssetMakeup.EnergyCapacityKwh(components)),
            controlManufacturer,
            codes.Of(LookupTable.AssetLifecycleStates, asset.LifecycleStateId),
            codes.Of(LookupTable.ParticipationStates, asset.ParticipationStateId),
            validFrom);

        var constraintPath = new ConstraintPathSnapshot(
            asset.Id,
            asset.Code,
            path.Nodes.Select(n => new ConstraintNodeSnapshot(Ref(n.Node, n.Level), Kva(n.RatedCapacityKva))).ToList(),
            site,
            microgrid,
            new Kilowatts(EffectiveLimits.Export(asset.RatedPMax.Value, asset.ExportLimit?.Value, path.Premise?.ExportLimitKw, path.Microgrid?.ExportLimitKw)),
            new Kilowatts(EffectiveLimits.Import(asset.RatedPMin.Value, path.Premise?.ServiceCapacityKva, path.Microgrid?.ImportLimitKw)));

        return new Readout(snapshot, constraintPath);
    }

    private static GridNodeRef Ref(GridNode node, string level) => new(node.GridNodeId, level, node.Code);
    private static Kilowatts? Kw(double? value) => value is { } v ? new Kilowatts(v) : null;
    private static KilovoltAmperes? Kva(double? value) => value is { } v ? new KilovoltAmperes(v) : null;
    private static KilowattHours? Kwh(double? value) => value is { } v ? new KilowattHours(v) : null;

    private static List<T> Ids<T>(IEnumerable<T?> ids) where T : struct =>
        ids.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Stored topology is corrupt: no complete constraint path for asset {AssetCode} ({AssetId}) as of {AsOf}: {Detail}")]
    private partial void LogIncompletePath(string assetCode, long assetId, DateTimeOffset? asOf, string detail);
}
