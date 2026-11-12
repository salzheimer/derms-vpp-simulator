using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.GetGridState;

/// <summary>
/// <see cref="IGridTopologyQueries.GetGridStateAsync"/>: one asset's effective grid state and registry eligibility.
/// Contract only, no endpoint. A database failure propagates as an exception, never a result (U5).
/// </summary>
internal sealed class GetGridStateHandler
{
    public Task<Result<GridStateSnapshot>> HandleAsync(AssetId assetId, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
