using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Domain.GridState;
using Rva.Modules.GridTopology.Features.Reads;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.GetGridState;

/// <summary>
/// <see cref="IGridTopologyQueries.GetGridStateAsync"/>: one asset's effective grid state and registry eligibility.
/// Contract only, no endpoint. A database failure propagates as an exception, never a result (U5).
/// </summary>
/// <remarks>
/// The asset, its path, and the grid state events are all read as of one time (one REPEATABLE READ transaction for a
/// current read), so the state is never derived from one version of the path and another version's events. A path that
/// cannot be completed fails the read with <see cref="GridTopologyErrors.ConstraintPathIncomplete"/>: the state of an
/// asset whose path is unknown is unknown, never "energized".
/// </remarks>
internal sealed class GetGridStateHandler(GridTopologyDbContext db, AssetReader reader, TimeProvider time)
{
    // Lifecycle and participation codes (topology.asset_lifecycle_states, topology.participation_states).
    private const string Commissioned = "commissioned";
    private const string Active = "active";

    public async Task<Result<GridStateSnapshot>> HandleAsync(AssetId assetId, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        if (await AsOf.CheckNotInFutureAsync(asOf, time, db, cancellationToken) is { } invalid)
            return invalid;

        return await AsOfReads.InOneVersionAsync(db, asOf, () => ReadAsync(assetId, asOf, cancellationToken), cancellationToken);
    }

    private async Task<Result<GridStateSnapshot>> ReadAsync(AssetId assetId, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        var read = await reader.ReadAsync(assets => assets.Where(a => a.Id == assetId), asOf, cancellationToken);
        if (read.IsFailure)
            return read.Error;
        if (read.Value.Count == 0)
            return Error.NotFound(GridTopologyErrors.AssetNotFound,
                asOf is { } at ? $"No asset has id {assetId} as of {AsOf.Format(at)}." : $"No asset has id {assetId}.");

        var (asset, path) = (read.Value[0].Asset, read.Value[0].Path);
        var nodes = path.Nodes.Select(n => n.Node).ToList();
        if (path.Microgrid is { } microgrid)
            nodes.Add(microgrid.Microgrid);

        var latest = await GridStateReads.LatestAsync(db, nodes.Select(n => n.GridNodeId).ToList(), assetId, asOf, cancellationToken);
        var effective = EffectiveGridState.Derive(
            nodes.Select(n => new PathNodeState(n, latest.Nodes.GetValueOrDefault(n.GridNodeId))).ToList(),
            latest.Asset);

        return new GridStateSnapshot(
            asset.Id,
            asset.Code,
            effective.State,
            effective.CauseNode,
            effective.CausedByAsset ? asset.Id : null,
            IsRegistryEligible(asset, effective.State));
    }

    /// <summary>Commissioned, actively participating, and energized from the grid or an island (GT-18).</summary>
    private static bool IsRegistryEligible(AssetSnapshot asset, string state) =>
        asset.LifecycleState == Commissioned
        && asset.ParticipationState == Active
        && state is GridStates.ConnectedEnergized or GridStates.Islanded;
}
