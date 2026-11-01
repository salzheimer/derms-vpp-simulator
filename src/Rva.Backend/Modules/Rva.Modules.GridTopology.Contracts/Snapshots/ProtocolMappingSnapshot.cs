using Rva.SharedKernel.ReferenceCodes;

namespace Rva.Modules.GridTopology.Contracts.Snapshots;

/// <summary>
/// How an external protocol identifies one internal technology type: the codes a device message carries, translated at
/// the Anti-Corruption Layer.
/// </summary>
/// <param name="Technology">The internal technology type the external codes map to.</param>
/// <param name="Standard">The external protocol standard and version.</param>
/// <param name="ResourceCategory">The standard's resource category, ex. <c>Storage</c>.</param>
/// <param name="AssetTypeCode">The standard's asset or device type code, ex. <c>Battery</c>.</param>
/// <param name="ExtraAttributes">Additional external attributes as a JSON object; null = none.</param>
public sealed record ProtocolMappingSnapshot(
    TechnologyTypeCode Technology,
    ProtocolStandard Standard,
    string ResourceCategory,
    string AssetTypeCode,
    string? ExtraAttributes);
