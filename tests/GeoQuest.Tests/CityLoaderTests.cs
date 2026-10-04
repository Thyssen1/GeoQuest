using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.Tests;

/// <summary>
/// That the baked city data and world map are actually reachable from inside the app. The
/// rules can all be right while the resource path is wrong, and nothing else would catch
/// that short of running the game.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public class CityLoaderTests
{
    private readonly HeadlessSession _avalonia;

    public CityLoaderTests(HeadlessSession avalonia) => _avalonia = avalonia;

    [Fact]
    public void Every_country_in_the_pool_has_a_capital_to_find() => _avalonia.Run(() =>
    {
        var capitals = new CityLoader().Capitals();

        Assert.Equal(197, capitals.Count);
        Assert.All(capitals, capital =>
        {
            Assert.False(string.IsNullOrWhiteSpace(capital.Name));
            Assert.Equal(2, capital.Code.Length);
        });
    });

    [Fact]
    public void Capitals_are_keyed_the_same_way_as_every_other_game() => _avalonia.Run(() =>
    {
        var capitals = new CityLoader().Capitals();

        using var stream = File.OpenRead(TestPaths.CountriesJson);
        var pool = JsonCountryRepository.Load(stream).QuestionPool;

        var codes = capitals.Select(capital => capital.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(pool, country => Assert.Contains(country.Code, codes));
    });

    [Fact]
    public void Every_capital_sits_somewhere_real() => _avalonia.Run(() =>
    {
        Assert.All(new CityLoader().Capitals(), capital =>
        {
            Assert.InRange(capital.Location.Latitude, -90d, 90d);
            Assert.InRange(capital.Location.Longitude, -180d, 180d);

            // Null Island is in the Gulf of Guinea, and is what an unparsed coordinate
            // looks like. No capital is within a hundred kilometres of it.
            Assert.True(
                capital.Location.DistanceTo(new GeoPoint(0d, 0d)) > 100d,
                $"{capital.Name} has coordinates that look unset");
        });
    });

    /// <summary>A few checked by hand, including the ones the baker had to correct.</summary>
    [Theory]
    [InlineData("dk", "Copenhagen")]
    [InlineData("za", "Pretoria")]
    [InlineData("ci", "Yamoussoukro")]
    [InlineData("kz", "Astana")]
    [InlineData("nr", "Yaren")]
    [InlineData("ss", "Juba")]
    public void The_corrected_capitals_survived_the_bake(string code, string expected) => _avalonia.Run(() =>
    {
        var capital = new CityLoader().Capitals()
            .Single(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expected, capital.Name);
    });

    [Fact]
    public void The_world_map_loads_and_fills_the_space_it_claims() => _avalonia.Run(() =>
    {
        var world = new CityLoader().World();

        Assert.NotNull(world);

        var bounds = world.Bounds;

        // The baker writes x across 0..1 and y down 0..0.5. If either is wrong the map
        // draws at the wrong scale and every pin is scored against the wrong place.
        Assert.InRange(bounds.Width, 0.97d, 1.001d);
        Assert.InRange(bounds.Height, 0.47d, 0.501d);
    });

    [Fact]
    public void The_data_is_parsed_once_and_reused() => _avalonia.Run(() =>
    {
        var loader = new CityLoader();

        Assert.Same(loader.Capitals(), loader.Capitals());
        Assert.Same(loader.World(), loader.World());
    });
}
