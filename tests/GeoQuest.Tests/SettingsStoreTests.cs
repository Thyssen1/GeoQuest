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

    [Fact]
    public void Sound_is_on_until_it_is_turned_off()
    {
        Assert.True(new FileSettingsStore(PathFor("absent.json")).Load().SoundEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Round_trips_the_sound_choice(bool enabled)
    {
        var path = PathFor("sound.json");

        new FileSettingsStore(path).Save(new GameSettings { SoundEnabled = enabled });

        Assert.Equal(enabled, new FileSettingsStore(path).Load().SoundEnabled);
    }

    [Fact]
    public void Storing_one_setting_leaves_the_others_alone()
    {
        var path = PathFor("both.json");
        var store = new FileSettingsStore(path);

        store.Save(new GameSettings { StartingLives = 5, SoundEnabled = false });

        var stored = store.Load();
        store.Save(stored with { StartingLives = 1 });

        var reloaded = store.Load();

        Assert.Equal(1, reloaded.StartingLives);
        Assert.False(reloaded.SoundEnabled);
    }

    [Fact]
    public void A_settings_file_written_before_sound_existed_still_loads()
    {
        var path = PathFor("older.json");
        Directory.CreateDirectory(_directory);

        // Exactly what the previous release wrote. The upgrade must keep the player's
        // starting lives and default the setting it has never heard of.
        File.WriteAllText(path, """{"startingLives":5}""");

        var settings = new FileSettingsStore(path).Load();

        Assert.Equal(5, settings.StartingLives);
        Assert.True(settings.SoundEnabled);
    }

    [Fact]
    public void Normal_is_the_mode_until_another_is_chosen()
    {
        Assert.Equal(GameMode.Normal, new FileSettingsStore(PathFor("absent.json")).Load().Mode);
    }

    [Theory]
    [InlineData(GameMode.Normal)]
    [InlineData(GameMode.Learning)]
    [InlineData(GameMode.Hard)]
    public void Round_trips_the_last_mode_played(GameMode mode)
    {
        var path = PathFor("mode.json");

        new FileSettingsStore(path).Save(new GameSettings { Mode = mode });

        Assert.Equal(mode, new FileSettingsStore(path).Load().Mode);
    }

    [Fact]
    public void The_mode_is_stored_by_name_so_the_file_stays_readable()
    {
        var path = PathFor("named.json");

        new FileSettingsStore(path).Save(new GameSettings { Mode = GameMode.Hard });

        Assert.Contains("Hard", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_mode_this_build_does_not_have_falls_back_to_normal()
    {
        var path = PathFor("unknown.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """{"startingLives":3,"mode":"Nightmare"}""");

        Assert.Equal(GameMode.Normal, new FileSettingsStore(path).Load().Mode);
    }

    [Fact]
    public void A_mode_number_outside_the_enum_is_clamped()
    {
        Assert.Equal(GameMode.Normal, new GameSettings { Mode = (GameMode)99 }.Sanitised().Mode);
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
