using Microsoft.EntityFrameworkCore;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.ProtocolMappings;

/// <summary>
/// <see cref="IGridTopologyQueries.ResolveTechnologyTypeAsync"/>: the technology type an external protocol's codes map
/// to. Contract only, no endpoint. An unmapped combination is NotFound, never a default technology.
/// </summary>
internal sealed class ResolveTechnologyTypeHandler(GridTopologyDbContext db)
{
    public async Task<Result<TechnologyTypeCode>> HandleAsync(
        ProtocolStandard standard, string resourceCategory, string assetTypeCode, DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        if (ProtocolMappingReads.Validate(standard, resourceCategory, assetTypeCode) is { } invalid)
            return invalid;

        var code = await (
                from m in AsOfReads.ProtocolMappings(db, asOf)
                join t in AsOfReads.TechnologyTypes(db, asOf) on m.TechnologyTypeId equals t.Id
                where m.ProtocolStandard == standard.Value && m.ResourceCategory == resourceCategory && m.AssetTypeCode == assetTypeCode
                select t.Code)
            .SingleOrDefaultAsync(cancellationToken);

        return code is null
            ? Error.NotFound(GridTopologyErrors.ProtocolMappingNotFound,
                $"No protocol mapping for ({standard}, {resourceCategory}, {assetTypeCode}).")
            : new TechnologyTypeCode(code);
    }
}
