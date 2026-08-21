using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GeoQuestTests", Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public void Defaults_when_nothing_is_stored()
    {
        var settings = new FileSettingsStore(PathFor("absent.json")).Load();

        Assert.Equal(GameSettings.DefaultStartingLives, settings.StartingLives);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Round_trips_each_allowed_value(int lives)
    {
        var path = PathFor("settings.json");

        new FileSettingsStore(path).Save(new GameSettings { StartingLives = lives });

        Assert.Equal(lives, new FileSettingsStore(path).Load().StartingLives);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults()
    {
        var path = PathFor("corrupt.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "definitely not json");

        Assert.Equal(GameSettings.DefaultStartingLives, new FileSettingsStore(path).Load().StartingLives);
    }

    [Fact]
    public void A_hand_edited_value_outside_the_allowed_set_is_clamped()
    {
        var path = PathFor("silly.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """{"startingLives":9999}""");

        Assert.Equal(GameSettings.DefaultStartingLives, new FileSettingsStore(path).Load().StartingLives);
    }

    [Fact]
    public void Saving_an_out_of_range_value_stores_the_default_instead()
    {
        var path = PathFor("clamped.json");

        new FileSettingsStore(path).Save(new GameSettings { StartingLives = 0 });

        Assert.Equal(GameSettings.DefaultStartingLives, new FileSettingsStore(path).Load().StartingLives);
    }

    [Fact]
    public void Saving_to_an_unwritable_path_does_not_throw()
    {
        var store = new FileSettingsStore(Path.Combine(_directory, "\0invalid", "settings.json"));

        Assert.Null(Record.Exception(() => store.Save(new GameSettings())));
    }

    [Fact]
    public void Sanitise_keeps_every_allowed_value_and_rejects_the_rest()
    {
        Assert.All(GameSettings.AllowedLives, lives =>
            Assert.Equal(lives, new GameSettings { StartingLives = lives }.Sanitised().StartingLives));

        Assert.Equal(
            GameSettings.DefaultStartingLives,
            new GameSettings { StartingLives = -4 }.Sanitised().StartingLives);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup of a temp directory.
        }
    }
}
