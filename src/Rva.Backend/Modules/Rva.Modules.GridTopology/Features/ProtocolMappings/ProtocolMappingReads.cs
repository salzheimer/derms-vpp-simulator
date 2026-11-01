using Microsoft.EntityFrameworkCore;
using Rva.Modules.GridTopology.Contracts;
using Rva.Modules.GridTopology.Domain.Catalog;
using Rva.Modules.GridTopology.Persistence;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.ProtocolMappings;

/// <summary>
/// What the two protocol mapping reads share: argument validation, and the tables to read, current or as of a time
/// (through the <c>_as_of</c> functions). Plan p1-s01c generalizes the as-of reads for the topology tables.
/// </summary>
internal static class ProtocolMappingReads
{
    /// <summary>
    /// A Validation error when the standard is missing (<c>default(ProtocolStandard)</c>; its constructor already
    /// rejects blank values) or any code is blank; null when the query is valid.
    /// </summary>
    public static Error? Validate(ProtocolStandard standard, params string?[] codes)
    {
        if (standard.Value is null)
            return Error.Validation(GridTopologyErrors.ProtocolMappingQueryInvalid, "A protocol standard is required.");
        if (codes.Any(string.IsNullOrWhiteSpace))
            return Error.Validation(GridTopologyErrors.ProtocolMappingQueryInvalid, "The resource category and asset type code must not be blank.");
        return null;
    }

    public static IQueryable<ProtocolMapping> Mappings(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.ProtocolMappings.FromSql($"SELECT * FROM topology.protocol_mappings_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.ProtocolMappings.AsNoTracking();

    public static IQueryable<TechnologyType> TechnologyTypes(GridTopologyDbContext db, DateTimeOffset? asOf) =>
        asOf is { } at
            ? db.TechnologyTypes.FromSql($"SELECT * FROM topology.technology_types_as_of({at.ToUniversalTime()})").AsNoTracking()
            : db.TechnologyTypes.AsNoTracking();
}
