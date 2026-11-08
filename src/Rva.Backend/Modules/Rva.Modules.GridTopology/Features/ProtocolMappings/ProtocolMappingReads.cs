using Rva.Modules.GridTopology.Contracts;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Features.ProtocolMappings;

/// <summary>Argument validation the two protocol mapping reads share. Their tables are read through <c>AsOfReads</c>.</summary>
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
}
