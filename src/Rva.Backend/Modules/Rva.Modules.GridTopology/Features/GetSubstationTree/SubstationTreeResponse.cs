using Rva.Modules.GridTopology.Http;

namespace Rva.Modules.GridTopology.Features.GetSubstationTree;

/// <summary>
/// <c>GET /api/topology/substations/{code}/tree</c>: one substation's subtree, nested by the normal configuration.
/// Assets appear under the node they connect to. Every location is GeoJSON in WGS 84, or null when not recorded.
/// </summary>
/// <param name="AsOf">The topology version read; null = current.</param>
internal sealed record SubstationTreeResponse(DateTimeOffset? AsOf, SubstationTreeNode Substation);

internal sealed record SubstationTreeNode(
    long GridNodeId,
    string Code,
    double RatedCapacityKva,
    GeoJsonGeometry? Location,
    IReadOnlyList<FeederTreeNode> Feeders,
    IReadOnlyList<MicrogridTreeNode> Microgrids,
    IReadOnlyList<TreeAsset> Assets);

/// <param name="RatedCapacityKva">Null = not rated (Q1): unknown, never unconstrained.</param>
/// <param name="Premises">Primary-service premises, served directly from the feeder.</param>
internal sealed record FeederTreeNode(
    long GridNodeId,
    string Code,
    double NominalVoltageKv,
    double? RatedCapacityKva,
    GeoJsonGeometry? Location,
    IReadOnlyList<TransformerTreeNode> Transformers,
    IReadOnlyList<PremiseTreeNode> Premises,
    IReadOnlyList<TreeAsset> Assets);

internal sealed record TransformerTreeNode(
    long GridNodeId,
    string Code,
    double RatedCapacityKva,
    double SecondaryVoltageV,
    GeoJsonGeometry? Location,
    IReadOnlyList<PremiseTreeNode> Premises,
    IReadOnlyList<TreeAsset> Assets);

internal sealed record PremiseTreeNode(
    long GridNodeId,
    string Code,
    string CustomerSegment,
    double? ServiceCapacityKva,
    double? ExportLimitKw,
    bool CanIsland,
    GeoJsonGeometry? Location,
    IReadOnlyList<TreeAsset> Assets);

/// <param name="FeederCode">The feeder its PCC is on; null for a microgrid at the substation.</param>
internal sealed record MicrogridTreeNode(
    long GridNodeId,
    string Code,
    string? FeederCode,
    bool CanIsland,
    double? ImportLimitKw,
    double? ExportLimitKw,
    int MemberCount);

internal sealed record TreeAsset(
    long Id,
    string Code,
    string InterconnectionType,
    double RatedPMinKw,
    double RatedPMaxKw,
    double RatedKva,
    double? ExportLimitKw,
    string LifecycleState,
    string ParticipationState,
    string? MicrogridCode);
