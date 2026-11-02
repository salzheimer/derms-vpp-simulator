using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Features.ProtocolMappings;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology;

/// <summary>Implements <see cref="IGridTopologyQueries"/>; each method delegates to its feature's handler.</summary>
internal sealed class GridTopologyQueries(
    ResolveTechnologyTypeHandler resolveTechnologyType,
    GetProtocolMappingsHandler getProtocolMappings) : IGridTopologyQueries
{
    public Task<Result<TechnologyTypeCode>> ResolveTechnologyTypeAsync(
        ProtocolStandard standard, string resourceCategory, string assetTypeCode, DateTimeOffset? asOf,
        CancellationToken cancellationToken) =>
        resolveTechnologyType.HandleAsync(standard, resourceCategory, assetTypeCode, asOf, cancellationToken);

    public Task<Result<IReadOnlyList<ProtocolMappingSnapshot>>> GetProtocolMappingsAsync(
        ProtocolStandard standard, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        getProtocolMappings.HandleAsync(standard, asOf, cancellationToken);
}
