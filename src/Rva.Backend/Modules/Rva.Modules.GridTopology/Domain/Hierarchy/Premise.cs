using NetTopologySuite.Geometries;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Domain.Hierarchy;

/// <summary>
/// A customer site behind one meter, served from a transformer (secondary) or directly from a feeder (primary):
/// exactly one of <see cref="TransformerId"/> and <see cref="FeederId"/> is set (<c>topology.premises</c>).
/// </summary>
internal sealed class Premise : GridNode
{
    private Premise() { }

    public PremiseId Id { get; private set; }
    public TransformerId? TransformerId { get; private set; }
    public FeederId? FeederId { get; private set; }
    public int CustomerSegmentId { get; private set; }

    /// <summary>Site import limit in kVA; null = unknown.</summary>
    public double? ServiceCapacityKva { get; private set; }

    /// <summary>Site export limit in kW; 0 = non-export, null = no limit recorded (D20).</summary>
    public double? ExportLimitKw { get; private set; }

    public bool CanIsland { get; private set; }
    public string? Address { get; private set; }
    public Point? Location { get; private set; }
}
