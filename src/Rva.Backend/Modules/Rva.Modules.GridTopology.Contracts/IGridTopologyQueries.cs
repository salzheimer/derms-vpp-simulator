using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.SharedKernel.ReferenceCodes;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Contracts;

/// <summary>
/// Synchronous, in-process reads of grid topology for other modules. Every method returns <c>Task&lt;Result&lt;T&gt;&gt;</c>,
/// takes an optional <c>asOf</c> and a <c>CancellationToken</c>, and is served from rva_core, never from a cache.
/// The methods are added by the read features (plan p1-s01b to p1-s01g).
/// </summary>
public interface IGridTopologyQueries
{
    /// <summary>
    /// The internal technology type an external protocol's codes map to, ex. (<c>OpenADR_3.0</c>, <c>Storage</c>,
    /// <c>Battery</c>) → <c>bess</c>. Codes match exactly (ordinal). An unmapped combination is NotFound
    /// (<see cref="GridTopologyErrors.ProtocolMappingNotFound"/>), never a default technology; a missing standard or a
    /// blank category or type code is a Validation error (<see cref="GridTopologyErrors.ProtocolMappingQueryInvalid"/>).
    /// </summary>
    /// <param name="asOf">Resolve with the mappings as they were at this time; null = current.</param>
    Task<Result<TechnologyTypeCode>> ResolveTechnologyTypeAsync(
        ProtocolStandard standard, string resourceCategory, string assetTypeCode, DateTimeOffset? asOf,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every mapping for one protocol standard, ordered by resource category and asset type code. A standard with no
    /// mappings returns an empty list; a missing standard is a Validation error
    /// (<see cref="GridTopologyErrors.ProtocolMappingQueryInvalid"/>).
    /// </summary>
    /// <param name="asOf">The mappings as they were at this time; null = current.</param>
    Task<Result<IReadOnlyList<ProtocolMappingSnapshot>>> GetProtocolMappingsAsync(
        ProtocolStandard standard, DateTimeOffset? asOf, CancellationToken cancellationToken);
}
