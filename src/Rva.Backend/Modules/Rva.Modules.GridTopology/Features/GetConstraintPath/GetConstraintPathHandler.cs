using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Features.Reads;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.GetConstraintPath;

/// <summary>
/// <see cref="IGridTopologyQueries.GetConstraintPathAsync"/>: the nodes and limits that constrain one asset. Contract
/// only, no endpoint. Read from rva_core on every call; a database failure propagates as an exception, never a result.
/// </summary>
internal sealed class GetConstraintPathHandler(GridTopologyDbContext db, AssetReader reader, TimeProvider time)
{
    public async Task<Result<ConstraintPathSnapshot>> HandleAsync(AssetId assetId, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        if (await AsOf.CheckNotInFutureAsync(asOf, time, db, cancellationToken) is { } invalid)
            return invalid;

        var read = await reader.ReadAsync(assets => assets.Where(a => a.Id == assetId), asOf, cancellationToken);
        if (read.IsFailure)
            return read.Error;
        return read.Value.Count == 0
            ? Error.NotFound(GridTopologyErrors.AssetNotFound,
                asOf is { } at ? $"No asset has id {assetId} as of {AsOf.Format(at)}." : $"No asset has id {assetId}.")
            : read.Value[0].Path;
    }
}
