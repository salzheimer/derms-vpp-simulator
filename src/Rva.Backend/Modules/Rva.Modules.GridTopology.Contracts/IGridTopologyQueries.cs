using Rva.Modules.GridTopology.Contracts.Snapshots;
using Rva.SharedKernel.Identifiers;
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

    /// <summary>
    /// One asset, current or as it was at <paramref name="asOf"/>, in any lifecycle state (a decommissioned asset stays
    /// readable). NotFound (<see cref="GridTopologyErrors.AssetNotFound"/>) when it does not exist, or did not exist
    /// yet then.
    /// </summary>
    /// <param name="asOf">Read the topology as it was at this time; null = current. A future time is a Validation
    /// error (<see cref="GridTopologyErrors.AsOfInFuture"/>).</param>
    Task<Result<AssetSnapshot>> GetAssetAsync(AssetId assetId, DateTimeOffset? asOf, CancellationToken cancellationToken);

    /// <summary>
    /// Every asset under one substation, connected at any level (premise, transformer, feeder, or the substation
    /// itself) and in any lifecycle state, a page at a time in asset id order. Pass the previous page's
    /// <see cref="AssetPage.NextCursor"/> to continue; every page reads the first page's
    /// <see cref="AssetPage.AsOf"/>, so the listing is stable and complete while the topology changes. NotFound
    /// (<see cref="GridTopologyErrors.SubstationNotFound"/>) for an unknown substation; Validation for a malformed or
    /// foreign cursor (<see cref="GridTopologyErrors.PageCursorInvalid"/>), a page size outside 1 to
    /// <see cref="MaxAssetPageSize"/> (<see cref="GridTopologyErrors.PageSizeInvalid"/>), or a cursor given with a
    /// different <paramref name="asOf"/>.
    /// </summary>
    /// <param name="asOf">Read the topology as it was at this time; null = now (the database time of the first page).</param>
    Task<Result<AssetPage>> GetAssetsBySubstationAsync(
        string substationCode, DateTimeOffset? asOf, string? cursor, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// The nodes and limits that constrain an asset (<see cref="ConstraintPathSnapshot"/>), current or as of a time.
    /// Read fresh from rva_core on every call: never cache it, since a reparenting changes it (GT-09). NotFound
    /// (<see cref="GridTopologyErrors.AssetNotFound"/>) for an unknown asset. A path the stored topology cannot complete
    /// is <see cref="GridTopologyErrors.ConstraintPathIncomplete"/>, never a shorter path; a database failure is an
    /// exception, never a result (U5).
    /// </summary>
    /// <param name="asOf">Read the topology as it was at this time; null = current.</param>
    Task<Result<ConstraintPathSnapshot>> GetConstraintPathAsync(AssetId assetId, DateTimeOffset? asOf, CancellationToken cancellationToken);

    /// <summary>
    /// The grid node with this level and code, current or as of a time, ex. to check a simulation scope root. Codes are
    /// unique within a level only, so a code that exists only at another level is NotFound
    /// (<see cref="GridTopologyErrors.NodeNotFound"/>). An unknown level or a blank code is a Validation error
    /// (<see cref="GridTopologyErrors.NodeQueryInvalid"/>).
    /// </summary>
    /// <param name="level">One of <see cref="GridNodeLevels"/>.</param>
    /// <param name="asOf">Resolve the node as it was at this time; null = current.</param>
    Task<Result<GridNodeRef>> ResolveNodeAsync(string level, string code, DateTimeOffset? asOf, CancellationToken cancellationToken);

    /// <summary>The largest page <see cref="GetAssetsBySubstationAsync"/> returns.</summary>
    const int MaxAssetPageSize = 1000;
}
