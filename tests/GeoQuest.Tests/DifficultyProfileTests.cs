using GeoQuest.Models;

namespace GeoQuest.Tests;

public class DifficultyProfileTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Normal_takes_its_lives_from_settings(int lives)
    {
        var profile = DifficultyProfile.For(GameMode.Normal, new GameSettings { StartingLives = lives });

        Assert.Equal(lives, profile.StartingLives);
    }

    [Fact]
    public void Normal_without_settings_still_starts_a_playable_run()
    {
        Assert.Equal(GameSettings.DefaultStartingLives, DifficultyProfile.For(GameMode.Normal).StartingLives);
    }

    [Fact]
    public void Learning_cannot_be_lost_and_ends_on_a_count()
    {
        var profile = DifficultyProfile.For(GameMode.Learning);

        Assert.False(profile.HasLives);
        Assert.True(profile.IsBounded);
        Assert.Equal(20, profile.RoundLimit);
    }

    [Fact]
    public void Learning_holds_one_grid_width()
    {
        var profile = DifficultyProfile.For(GameMode.Learning);

        Assert.Equal(profile.OpeningOptions, profile.MaxOptions);
    }

    [Fact]
    public void Hard_fixes_three_lives_whatever_the_setting_says()
    {
        var generous = new GameSettings { StartingLives = 5 };

        Assert.Equal(3, DifficultyProfile.For(GameMode.Hard, generous).StartingLives);
    }

    [Fact]
    public void Hard_runs_end_on_lives_rather_than_on_a_count()
    {
        var profile = DifficultyProfile.For(GameMode.Hard);

        Assert.True(profile.HasLives);
        Assert.False(profile.IsBounded);
    }

    [Fact]
    public void Only_normal_hands_out_extra_lives()
    {
        Assert.True(DifficultyProfile.For(GameMode.Normal).BonusLifeChance > 0d);
        Assert.Equal(0d, DifficultyProfile.For(GameMode.Learning).BonusLifeChance);
        Assert.Equal(0d, DifficultyProfile.For(GameMode.Hard).BonusLifeChance);
    }

    [Fact]
    public void Hard_opens_wider_than_normal()
    {
        Assert.True(
            DifficultyProfile.For(GameMode.Hard).OpeningOptions >
            DifficultyProfile.For(GameMode.Normal).OpeningOptions);
    }

    [Fact]
    public void Every_mode_resolves_to_its_own_profile()
    {
        Assert.All(Enum.GetValues<GameMode>(), mode =>
            Assert.Equal(mode, DifficultyProfile.For(mode).Mode));
    }

    [Fact]
    public void Every_profile_describes_a_run_that_can_actually_end()
    {
        // A mode with neither lives nor a round limit would never stop.
        Assert.All(Enum.GetValues<GameMode>(), mode =>
        {
            var profile = DifficultyProfile.For(mode);
            Assert.True(profile.HasLives || profile.IsBounded, $"{mode} cannot end");
        });
    }
}
