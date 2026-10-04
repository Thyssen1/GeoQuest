using System;

namespace GeoQuest.Models;

/// <summary>A place on the globe, in degrees.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    /// <summary>
    /// Great-circle distance in kilometres. Pin drops are scored on this rather than on
    /// distance across the drawn map, so a near miss in Siberia and a near miss in
    /// Indonesia are judged the same way despite the projection stretching one of them.
    /// </summary>
    public double DistanceTo(GeoPoint other)
    {
        const double EarthRadiusKm = 6371d;

        var lat1 = Radians(Latitude);
        var lat2 = Radians(other.Latitude);
        var deltaLat = Radians(other.Latitude - Latitude);
        var deltaLon = Radians(other.Longitude - Longitude);

        var a = (Math.Sin(deltaLat / 2d) * Math.Sin(deltaLat / 2d)) +
                (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2d) * Math.Sin(deltaLon / 2d));

        return 2d * EarthRadiusKm * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180d;
}

/// <summary>
/// A capital city: the thing Find the City asks about. Keyed by the same ISO code as every
/// other game, so a player's progress through the world is recorded the same way whether
/// the round drew a flag, an outline or a map.
/// </summary>
public sealed record Capital(string Code, string Name, GeoPoint Location);

/// <summary>
/// The equirectangular projection the world map is drawn in, and its inverse.
///
/// This has to agree exactly with what BorderBaker wrote into world.json: both axes are
/// divided by 360, which puts x in 0..1 and y in 0..0.5 and keeps the map's 2:1 proportions
/// in the numbers, so nothing downstream needs to know the aspect ratio to draw it.
///
/// Equirectangular rather than Mercator for two reasons. Every click has to be turned back
/// into a position, and inverting this is two multiplications where Mercator needs a
/// logarithm's inverse. And Mercator would draw Greenland the size of Africa, which is a
/// strange thing for a geography game to teach.
/// </summary>
public static class WorldMap
{
    /// <summary>Width of the map in the units world.json is written in.</summary>
    public const double Width = 1d;

    /// <summary>Height of the map in the same units: half the width, for 360 by 180 degrees.</summary>
    public const double Height = 0.5d;

    private const double Span = 360d;

    /// <summary>Where a place sits on the map.</summary>
    public static (double X, double Y) ToMap(GeoPoint point) =>
        ((point.Longitude + 180d) / Span, (90d - point.Latitude) / Span);

    /// <summary>
    /// Where a point on the map is on the globe. Out-of-range input is clamped rather than
    /// rejected: a click lands in pixels, and the edge pixel of a map is still the edge of
    /// the world rather than an error.
    /// </summary>
    public static GeoPoint ToGlobe(double x, double y) => new(
        Latitude: Math.Clamp(90d - (y * Span), -90d, 90d),
        Longitude: Math.Clamp((x * Span) - 180d, -180d, 180d));
}
