using NetTopologySuite.Geometries;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Domain.Hierarchy;

/// <summary>A service transformer stepping feeder voltage down to its premises (<c>topology.neighborhood_transformers</c>).</summary>
internal sealed class NeighborhoodTransformer : GridNode
{
    private NeighborhoodTransformer() { }

    public TransformerId Id { get; private set; }
    public FeederId FeederId { get; private set; }
    public double RatedCapacityKva { get; private set; }
    public double SecondaryVoltageV { get; private set; }
    public Point? Location { get; private set; }
}
