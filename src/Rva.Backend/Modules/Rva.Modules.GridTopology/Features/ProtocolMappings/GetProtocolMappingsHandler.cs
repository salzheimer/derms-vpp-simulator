using Microsoft.EntityFrameworkCore;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.ProtocolMappings;

/// <summary>
/// <see cref="IGridTopologyQueries.GetProtocolMappingsAsync"/>: every mapping for one protocol standard. Contract only,
/// no endpoint.
/// </summary>
internal sealed class GetProtocolMappingsHandler(GridTopologyDbContext db)
{
    public async Task<Result<IReadOnlyList<ProtocolMappingSnapshot>>> HandleAsync(
        ProtocolStandard standard, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        if (ProtocolMappingReads.Validate(standard) is { } invalid)
            return invalid;

        var rows = await (
                from m in AsOfReads.ProtocolMappings(db, asOf)
                join t in AsOfReads.TechnologyTypes(db, asOf) on m.TechnologyTypeId equals t.Id
                where m.ProtocolStandard == standard.Value
                orderby m.ResourceCategory, m.AssetTypeCode
                select new { Technology = t.Code, m.ResourceCategory, m.AssetTypeCode, m.ExtraAttributes })
            .ToListAsync(cancellationToken);

        // Explicit: implicit conversions to Result<T> do not apply when T is an interface.
        return Result.Success<IReadOnlyList<ProtocolMappingSnapshot>>(rows
            .Select(r => new ProtocolMappingSnapshot(
                new TechnologyTypeCode(r.Technology), standard, r.ResourceCategory, r.AssetTypeCode, r.ExtraAttributes))
            .ToList());
    }
}
