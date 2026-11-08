using NetTopologySuite.Geometries;

namespace Rva.Modules.GridTopology.Http;

/// <summary>
/// A GeoJSON geometry (RFC 7946) in WGS 84 (SRID 4326, the SRID of every topology location). Coordinates are
/// <c>[longitude, latitude]</c>: a position for a <c>Point</c>, a list of positions for a <c>LineString</c>.
/// </summary>
internal sealed record GeoJsonGeometry(string Type, object Coordinates)
{
    public static GeoJsonGeometry? From(Point? point) =>
        point is null ? null : new("Point", Position(point.Coordinate));

    public static GeoJsonGeometry? From(LineString? line) =>
        line is null ? null : new("LineString", line.Coordinates.Select(Position).ToArray());

    private static double[] Position(Coordinate c) => [c.X, c.Y];
}
