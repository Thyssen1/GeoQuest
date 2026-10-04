using GeoQuest.Models;

namespace GeoQuest.Tests;

/// <summary>
/// What a pin is worth, and how the target shrinks as a run goes on. This is Find the
/// City's half of the difficulty curve: the other games add options, a pin round has none
/// to add, so the thing that changes is how close the player has to get.
/// </summary>
public class PinScoringTests
{
    private static readonly DifficultyProfile Normal =
        DifficultyProfile.For(MiniGame.Cities, GameMode.Normal);

    [Fact]
    public void A_pin_on_the_city_is_worth_full_marks()
    {
        Assert.Equal(1d, GameRules.PinAccuracy(0d, 500d));
    }

    /// <summary>
    /// Fifty kilometres is under a pixel on the drawn map, so everything inside it has to
    /// score the same. Otherwise the score turns on which pixel was clicked.
    /// </summary>
    [Fact]
    public void Anything_inside_a_pixel_of_the_city_scores_the_same()
    {
        Assert.Equal(1d, GameRules.PinAccuracy(GameRules.PerfectPinKm, 500d));
        Assert.Equal(1d, GameRules.PinAccuracy(GameRules.PerfectPinKm - 1d, 500d));
    }

    [Fact]
    public void A_pin_at_the_edge_of_what_the_mode_accepts_is_worth_nothing()
    {
        Assert.Equal(0d, GameRules.PinAccuracy(500d, 500d));
        Assert.Equal(0d, GameRules.PinAccuracy(9000d, 500d));
    }

    [Fact]
    public void Accuracy_falls_away_as_the_pin_gets_further_off()
    {
        var near = GameRules.PinAccuracy(150d, 800d);
        var middling = GameRules.PinAccuracy(400d, 800d);
        var far = GameRules.PinAccuracy(700d, 800d);

        Assert.True(near > middling, $"{near} should beat {middling}");
        Assert.True(middling > far, $"{middling} should beat {far}");
        Assert.InRange(near, 0d, 1d);
    }

    [Fact]
    public void A_missed_pin_scores_nothing()
    {
        var score = GameRules.ScoreForPin(
            distanceKm: 2000d,
            toleranceKm: 500d,
            remaining: TimeSpan.FromSeconds(10d),
            allowed: TimeSpan.FromSeconds(12d),
            streak: 1);

        Assert.Equal(0, score);
    }

    /// <summary>Knowing roughly where a city is has to beat not knowing at all.</summary>
    [Fact]
    public void A_pin_that_only_just_counts_still_scores()
    {
        var score = GameRules.ScoreForPin(
            distanceKm: 790d,
            toleranceKm: 800d,
            remaining: TimeSpan.FromSeconds(6d),
            allowed: TimeSpan.FromSeconds(12d),
            streak: 1);

        Assert.True(score > 0, "a pin inside the tolerance should be worth something");
    }

    [Fact]
    public void A_closer_pin_beats_a_further_one_given_the_same_clock()
    {
        var remaining = TimeSpan.FromSeconds(8d);
        var allowed = TimeSpan.FromSeconds(12d);

        var close = GameRules.ScoreForPin(60d, 800d, remaining, allowed, streak: 1);
        var loose = GameRules.ScoreForPin(600d, 800d, remaining, allowed, streak: 1);

        Assert.True(close > loose, $"{close} should beat {loose}");
    }

    [Fact]
    public void A_streak_still_multiplies_a_pin()
    {
        var alone = GameRules.ScoreForPin(60d, 800d, TimeSpan.FromSeconds(8d), TimeSpan.FromSeconds(12d), streak: 1);
        var onAStreak = GameRules.ScoreForPin(60d, 800d, TimeSpan.FromSeconds(8d), TimeSpan.FromSeconds(12d), streak: 6);

        Assert.True(onAStreak > alone, $"{onAStreak} should beat {alone}");
    }

    [Fact]
    public void Nonsense_input_is_refused_rather_than_scored()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameRules.PinAccuracy(-1d, 500d));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameRules.PinAccuracy(10d, 0d));
    }

    // ---- the target shrinking ----

    [Fact]
    public void The_target_starts_at_the_modes_opening_width()
    {
        Assert.Equal(Normal.OpeningToleranceKm, GameRules.ToleranceFor(0, Normal));
    }

    [Fact]
    public void The_target_shrinks_as_the_run_goes_on()
    {
        var opening = GameRules.ToleranceFor(0, Normal);
        var later = GameRules.ToleranceFor(7, Normal);
        var deep = GameRules.ToleranceFor(12, Normal);

        Assert.True(later < opening, $"{later} should be tighter than {opening}");
        Assert.True(deep < later, $"{deep} should be tighter than {later}");
    }

    [Fact]
    public void The_target_never_shrinks_past_the_modes_floor()
    {
        Assert.Equal(Normal.MinimumToleranceKm, GameRules.ToleranceFor(500, Normal));
    }

    /// <summary>Learning holds its terms steady, exactly as it holds the clock and the grid.</summary>
    [Fact]
    public void Learning_never_tightens_the_target()
    {
        var learning = DifficultyProfile.For(MiniGame.Cities, GameMode.Learning);

        Assert.Equal(learning.OpeningToleranceKm, GameRules.ToleranceFor(0, learning));
        Assert.Equal(learning.OpeningToleranceKm, GameRules.ToleranceFor(40, learning));
    }

    [Fact]
    public void Hard_demands_a_closer_pin_than_normal_from_the_first_round()
    {
        var hard = DifficultyProfile.For(MiniGame.Cities, GameMode.Hard);

        Assert.True(
            GameRules.ToleranceFor(0, hard) < GameRules.ToleranceFor(0, Normal),
            "hard mode should open with a smaller target than normal");
    }

    // ---- how the game and mode combine ----

    [Theory]
    [InlineData(GameMode.Normal)]
    [InlineData(GameMode.Learning)]
    [InlineData(GameMode.Hard)]
    public void Find_the_city_is_answered_with_a_pin(GameMode mode)
    {
        var profile = DifficultyProfile.For(MiniGame.Cities, mode);

        Assert.Equal(RoundInput.Pin, profile.Input);
        Assert.Equal(RoundSubject.Place, profile.Subject);
    }

    /// <summary>Recall reverses every game, and reversing a pin round means naming the pin.</summary>
    [Fact]
    public void Recall_turns_the_city_round_into_a_naming_one()
    {
        var profile = DifficultyProfile.For(MiniGame.Cities, GameMode.Recall);

        Assert.Equal(RoundInput.Name, profile.Input);
        Assert.Equal(RoundSubject.Place, profile.Subject);
    }

    [Theory]
    [InlineData(MiniGame.Flags)]
    [InlineData(MiniGame.Borders)]
    public void The_other_games_are_untouched_by_any_of_this(MiniGame game)
    {
        Assert.Equal(RoundInput.Grid, DifficultyProfile.For(game, GameMode.Normal).Input);
        Assert.Equal(RoundInput.Name, DifficultyProfile.For(game, GameMode.Recall).Input);
    }

    [Fact]
    public void Hard_still_gives_three_lives_and_no_way_back_in_a_city_run()
    {
        var hard = DifficultyProfile.For(MiniGame.Cities, GameMode.Hard);

        Assert.Equal(3, hard.StartingLives);
        Assert.Equal(0d, hard.BonusLifeChance);
    }
}
