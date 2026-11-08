using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Features.Reads;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.GetAsset;

/// <summary>
/// <see cref="IGridTopologyQueries.GetAssetAsync"/> by id, and the <c>GET /api/topology/assets/{code}</c> endpoint by
/// code: one asset, current or as of a time, in any lifecycle state.
/// </summary>
internal sealed class GetAssetHandler(AssetReader reader, TimeProvider time)
{
    public Task<Result<AssetSnapshot>> HandleAsync(AssetId assetId, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        ReadOneAsync(assets => assets.Where(a => a.Id == assetId), $"No asset has id {assetId}", asOf, cancellationToken);

    public Task<Result<AssetSnapshot>> HandleAsync(string assetCode, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        ReadOneAsync(assets => assets.Where(a => a.Code == assetCode), $"No asset has code {assetCode}", asOf, cancellationToken);

    private async Task<Result<AssetSnapshot>> ReadOneAsync(
        Func<IQueryable<Domain.Assets.Asset>, IQueryable<Domain.Assets.Asset>> select, string notFound, DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        if (AsOf.CheckNotInFuture(asOf, time) is { } invalid)
            return invalid;

        var read = await reader.ReadAsync(select, asOf, cancellationToken);
        if (read.IsFailure)
            return read.Error;
        return read.Value.Count == 0
            ? Error.NotFound(GridTopologyErrors.AssetNotFound, asOf is { } at ? $"{notFound} as of {AsOf.Format(at)}." : $"{notFound}.")
            : read.Value[0].Asset;
    }
}
