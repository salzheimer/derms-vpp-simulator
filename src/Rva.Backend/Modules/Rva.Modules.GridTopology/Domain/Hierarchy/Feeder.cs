using NetTopologySuite.Geometries;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Domain.Hierarchy;

/// <summary>A distribution circuit leaving a substation at primary voltage (<c>topology.feeders</c>).</summary>
internal sealed class Feeder : GridNode
{
    private Feeder() { }

    public FeederId Id { get; private set; }
    public SubstationId SubstationId { get; private set; }
    public double NominalVoltageKv { get; private set; }

    /// <summary>Thermal capacity in kVA; null = not modeled, never "unconstrained" (plan p1-s01, Q1).</summary>
    public double? RatedCapacityKva { get; private set; }

    public LineString? LineGeometry { get; private set; }
}
