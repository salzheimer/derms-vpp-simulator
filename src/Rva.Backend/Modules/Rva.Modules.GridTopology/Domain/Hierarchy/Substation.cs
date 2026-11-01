using NetTopologySuite.Geometries;
using Rva.SharedKernel.Identifiers;

namespace Rva.Modules.GridTopology.Domain.Hierarchy;

/// <summary>A distribution substation: the top of the hierarchy (<c>topology.substations</c>).</summary>
internal sealed class Substation : GridNode
{
    private Substation() { }

    internal Substation(string code, double ratedCapacityKva, Point? location = null)
    {
        Code = code;
        RatedCapacityKva = ratedCapacityKva;
        Location = location;
    }

    public SubstationId Id { get; private set; }
    public double RatedCapacityKva { get; private set; }
    public Point? Location { get; private set; }
}
