namespace Rva.Modules.GridTopology.Domain.Catalog;

/// <summary>
/// Maps an internal technology type to the codes an external protocol uses for it (<c>topology.protocol_mappings</c>).
/// </summary>
internal sealed class ProtocolMapping
{
    private ProtocolMapping() { }

    public long Id { get; private set; }
    public int TechnologyTypeId { get; private set; }
    public string ProtocolStandard { get; private set; } = "";
    public string ResourceCategory { get; private set; } = "";
    public string AssetTypeCode { get; private set; } = "";

    /// <summary>Additional external attributes as JSON text; null = none.</summary>
    public string? ExtraAttributes { get; private set; }
}
