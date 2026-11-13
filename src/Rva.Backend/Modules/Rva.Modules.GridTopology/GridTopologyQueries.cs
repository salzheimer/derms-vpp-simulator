using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Features.GetAsset;
using Rva.Modules.GridTopology.Features.GetAssetsBySubstation;
using Rva.Modules.GridTopology.Features.GetConstraintPath;
using Rva.Modules.GridTopology.Features.GetGridState;
using Rva.Modules.GridTopology.Features.ProtocolMappings;
using Rva.Modules.GridTopology.Features.ResolveNode;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology;

/// <summary>Implements <see cref="IGridTopologyQueries"/>; each method delegates to its feature's handler.</summary>
internal sealed class GridTopologyQueries(
    ResolveTechnologyTypeHandler resolveTechnologyType,
    GetProtocolMappingsHandler getProtocolMappings,
    GetAssetHandler getAsset,
    GetAssetsBySubstationHandler getAssetsBySubstation,
    GetConstraintPathHandler getConstraintPath,
    ResolveNodeHandler resolveNode,
    GetGridStateHandler getGridState) : IGridTopologyQueries
{
    public Task<Result<TechnologyTypeCode>> ResolveTechnologyTypeAsync(
        ProtocolStandard standard, string resourceCategory, string assetTypeCode, DateTimeOffset? asOf,
        CancellationToken cancellationToken) =>
        resolveTechnologyType.HandleAsync(standard, resourceCategory, assetTypeCode, asOf, cancellationToken);

    public Task<Result<IReadOnlyList<ProtocolMappingSnapshot>>> GetProtocolMappingsAsync(
        ProtocolStandard standard, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        getProtocolMappings.HandleAsync(standard, asOf, cancellationToken);

    public Task<Result<AssetSnapshot>> GetAssetAsync(AssetId assetId, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        getAsset.HandleAsync(assetId, asOf, cancellationToken);

    public Task<Result<AssetPage>> GetAssetsBySubstationAsync(
        string substationCode, DateTimeOffset? asOf, string? cursor, int pageSize, CancellationToken cancellationToken) =>
        getAssetsBySubstation.HandleAsync(substationCode, asOf, cursor, pageSize, cancellationToken);

    public Task<Result<ConstraintPathSnapshot>> GetConstraintPathAsync(AssetId assetId, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        getConstraintPath.HandleAsync(assetId, asOf, cancellationToken);

    public Task<Result<GridNodeRef>> ResolveNodeAsync(string level, string code, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        resolveNode.HandleAsync(level, code, asOf, cancellationToken);

    public Task<Result<GridStateSnapshot>> GetGridStateAsync(AssetId assetId, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        getGridState.HandleAsync(assetId, asOf, cancellationToken);
}
