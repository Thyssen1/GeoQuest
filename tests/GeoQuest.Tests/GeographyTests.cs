using GeoQuest.Models;

namespace GeoQuest.Tests;

/// <summary>
/// The maths Find the City is scored on. A pin drop has no wrong answer to compare
/// against, only a distance, so these are the rules that decide whether a round was right.
/// </summary>
public class GeographyTests
{
    // Distances checked against published great-circle figures, to within a percent.
    [Theory]
    [InlineData(55.6761, 12.5683, 59.3293, 18.0686, 522)]    // Copenhagen - Stockholm
    [InlineData(51.5074, -0.1278, 40.7128, -74.0060, 5570)]  // London - New York
    [InlineData(-33.8688, 151.2093, -36.8485, 174.7633, 2155)] // Sydney - Auckland
    public void Distance_matches_the_published_figure(
        double lat1, double lon1, double lat2, double lon2, double expectedKm)
    {
        var distance = new GeoPoint(lat1, lon1).DistanceTo(new GeoPoint(lat2, lon2));

        Assert.InRange(distance, expectedKm * 0.99d, expectedKm * 1.01d);
    }

    [Fact]
    public void A_place_is_no_distance_from_itself()
    {
        var copenhagen = new GeoPoint(55.6761d, 12.5683d);

        Assert.Equal(0d, copenhagen.DistanceTo(copenhagen), precision: 6);
    }

    [Fact]
    public void Distance_does_not_depend_on_which_way_round_it_is_measured()
    {
        var oslo = new GeoPoint(59.9139d, 10.7522d);
        var tokyo = new GeoPoint(35.6762d, 139.6503d);

        Assert.Equal(oslo.DistanceTo(tokyo), tokyo.DistanceTo(oslo), precision: 6);
    }

    /// <summary>
    /// The short way round, not the long one. Two points either side of the antimeridian
    /// are neighbours, and a naive longitude subtraction would call them half a world apart.
    /// </summary>
    [Fact]
    public void Distance_crosses_the_antimeridian_the_short_way()
    {
        var west = new GeoPoint(0d, 179d);
        var east = new GeoPoint(0d, -179d);

        Assert.InRange(west.DistanceTo(east), 200d, 250d);
    }

    [Fact]
    public void Half_the_world_apart_is_half_the_circumference()
    {
        var pole = new GeoPoint(90d, 0d);
        var antipode = new GeoPoint(-90d, 0d);

        Assert.InRange(pole.DistanceTo(antipode), 20_000d, 20_050d);
    }

    // ---- the projection ----

    [Theory]
    [InlineData(90d, -180d, 0d, 0d)]        // top left
    [InlineData(-90d, 180d, 1d, 0.5d)]      // bottom right
    [InlineData(0d, 0d, 0.5d, 0.25d)]       // the origin lands dead centre
    public void The_map_corners_are_where_the_baker_put_them(
        double latitude, double longitude, double expectedX, double expectedY)
    {
        var (x, y) = WorldMap.ToMap(new GeoPoint(latitude, longitude));

        Assert.Equal(expectedX, x, precision: 9);
        Assert.Equal(expectedY, y, precision: 9);
    }

    [Theory]
    [InlineData(55.6761d, 12.5683d)]
    [InlineData(-33.8688d, 151.2093d)]
    [InlineData(64.1466d, -21.9426d)]
    public void Projecting_a_place_and_back_returns_it(double latitude, double longitude)
    {
        var (x, y) = WorldMap.ToMap(new GeoPoint(latitude, longitude));
        var round = WorldMap.ToGlobe(x, y);

        Assert.Equal(latitude, round.Latitude, precision: 9);
        Assert.Equal(longitude, round.Longitude, precision: 9);
    }

    /// <summary>A click can land a pixel outside the map, which is the edge rather than an error.</summary>
    [Fact]
    public void A_point_off_the_map_clamps_to_its_edge()
    {
        Assert.Equal(new GeoPoint(90d, -180d), WorldMap.ToGlobe(-0.2d, -0.2d));
        Assert.Equal(new GeoPoint(-90d, 180d), WorldMap.ToGlobe(1.4d, 0.9d));
    }

    [Fact]
    public void The_map_is_twice_as_wide_as_it_is_tall()
    {
        Assert.Equal(2d, WorldMap.Width / WorldMap.Height, precision: 9);
    }
}
