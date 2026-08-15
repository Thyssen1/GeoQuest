using System.Text;
using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.Tests;

public class CountryDataTests
{
    private static ICountryRepository Load()
    {
        using var stream = File.OpenRead(TestPaths.CountriesJson);
        return JsonCountryRepository.Load(stream);
    }

    [Fact]
    public void Loads_every_entry()
    {
        Assert.Equal(255, Load().All.Count);
    }

    [Fact]
    public void Question_pool_is_sovereign_states_only()
    {
        var pool = Load().QuestionPool;

        Assert.Equal(197, pool.Count);
        Assert.All(pool, c => Assert.Equal(CountryKind.Sovereign, c.Kind));
    }

    [Fact]
    public void Question_pool_excludes_subdivisions_and_the_eu()
    {
        var codes = Load().QuestionPool.Select(c => c.Code).ToHashSet();

        // Offering Scotland alongside the United Kingdom would not be a fair round.
        Assert.DoesNotContain("gb-eng", codes);
        Assert.DoesNotContain("gb-sct", codes);
        Assert.DoesNotContain("gb-wls", codes);
        Assert.DoesNotContain("gb-nir", codes);
        Assert.DoesNotContain("eu", codes);
        Assert.Contains("gb", codes);
    }

    [Fact]
    public void Every_entry_has_a_code_and_a_name()
    {
        Assert.All(Load().All, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Code));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.Region));
        });
    }

    [Fact]
    public void Codes_are_lowercase_and_unique()
    {
        var all = Load().All;

        Assert.All(all, c => Assert.Equal(c.Code.ToLowerInvariant(), c.Code));
        Assert.Equal(all.Count, all.Select(c => c.Code).Distinct().Count());
    }

    [Fact]
    public void Non_ascii_names_survive_the_load()
    {
        var all = Load().All;

        Assert.Equal("Côte d'Ivoire", all.Single(c => c.Code == "ci").Name);
        Assert.Equal("Åland Islands", all.Single(c => c.Code == "ax").Name);
        Assert.Equal("Denmark", all.Single(c => c.Code == "dk").Name);
    }

    [Fact]
    public void Empty_json_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => LoadFrom("[]"));
    }

    [Fact]
    public void Malformed_json_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => LoadFrom("{ not json"));
    }

    [Fact]
    public void Duplicate_codes_are_rejected()
    {
        // A duplicate would let the same country appear twice in one question.
        const string json = """
            [{"code":"dk","name":"Denmark","region":"Europe","kind":"sovereign"},
             {"code":"dk","name":"Duplicate","region":"Europe","kind":"sovereign"}]
            """;

        Assert.Throws<InvalidDataException>(() => LoadFrom(json));
    }

    /// <summary>
    /// The 255-entries-to-255-images invariant the README documents. Without this,
    /// a flag added without metadata (or vice versa) only surfaces as a blank tile
    /// mid-game.
    /// </summary>
    [Fact]
    public void Every_country_has_a_flag_image_and_every_image_has_a_country()
    {
        var metadata = Load().All.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);

        var images = Directory
            .GetFiles(TestPaths.FlagsDirectory, "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

        var missingImages = metadata.Except(images).OrderBy(c => c).ToArray();
        var orphanImages = images.Except(metadata).OrderBy(c => c).ToArray();

        Assert.True(missingImages.Length == 0, "countries with no flag image: " + string.Join(", ", missingImages));
        Assert.True(orphanImages.Length == 0, "flag images with no country entry: " + string.Join(", ", orphanImages));
    }

    private static ICountryRepository LoadFrom(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return JsonCountryRepository.Load(stream);
    }
}
