using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Domain.Hierarchy;

/// <summary>
/// A utility-side microgrid whose point of common coupling is on a feeder or at a substation: exactly one of
/// <see cref="FeederId"/> and <see cref="SubstationId"/> is set (<c>topology.microgrids</c>).
/// </summary>
internal sealed class Microgrid : GridNode
{
    private Microgrid() { }

    public MicrogridId Id { get; private set; }
    public FeederId? FeederId { get; private set; }
    public SubstationId? SubstationId { get; private set; }
    public bool CanIsland { get; private set; }

    /// <summary>Import limit at the PCC in kW; null = no limit recorded, which is not 0 (D20).</summary>
    public double? ImportLimitKw { get; private set; }

    /// <summary>Export limit at the PCC in kW; 0 = non-export, null = no limit recorded (D20).</summary>
    public double? ExportLimitKw { get; private set; }
}
