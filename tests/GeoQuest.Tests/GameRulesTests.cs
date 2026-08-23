using GeoQuest.Models;

namespace GeoQuest.Tests;

public class GameRulesTests
{
    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(6, 4)]
    [InlineData(7, 5)]
    [InlineData(11, 5)]
    [InlineData(12, 6)]
    [InlineData(500, 6)]
    public void OptionCount_follows_the_documented_ramp(int correct, int expected)
    {
        Assert.Equal(expected, GameRules.OptionCountFor(correct));
    }

    [Fact]
    public void OptionCount_never_decreases_as_the_player_improves()
    {
        for (var i = 1; i <= 300; i++)
        {
            Assert.True(
                GameRules.OptionCountFor(i) >= GameRules.OptionCountFor(i - 1),
                $"option count dropped between {i - 1} and {i} correct answers");
        }
    }

    [Fact]
    public void OptionCount_stays_within_its_declared_bounds()
    {
        for (var i = 0; i <= 300; i++)
        {
            var count = GameRules.OptionCountFor(i);
            Assert.InRange(count, GameRules.MinOptions, GameRules.MaxOptions);
        }
    }

    [Fact]
    public void OptionCount_rejects_a_negative_answer_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameRules.OptionCountFor(-1));
    }

    [Fact]
    public void Round_starts_at_twelve_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(12), GameRules.RoundDurationFor(0));
    }

    [Fact]
    public void Round_duration_never_drops_below_the_floor()
    {
        for (var i = 0; i <= 300; i++)
        {
            Assert.True(GameRules.RoundDurationFor(i) >= TimeSpan.FromSeconds(7));
        }
    }

    [Fact]
    public void Round_duration_never_increases_as_the_player_improves()
    {
        for (var i = 1; i <= 300; i++)
        {
            Assert.True(GameRules.RoundDurationFor(i) <= GameRules.RoundDurationFor(i - 1));
        }
    }

    [Fact]
    public void Answering_faster_scores_more()
    {
        var allowed = TimeSpan.FromSeconds(12);

        var fast = GameRules.ScoreFor(allowed, allowed, 1);
        var slow = GameRules.ScoreFor(TimeSpan.Zero, allowed, 1);

        Assert.True(fast > slow, $"fast {fast} should beat slow {slow}");
    }

    [Fact]
    public void A_longer_streak_scores_more()
    {
        var allowed = TimeSpan.FromSeconds(12);

        var streaked = GameRules.ScoreFor(TimeSpan.Zero, allowed, 5);
        var single = GameRules.ScoreFor(TimeSpan.Zero, allowed, 1);

        Assert.True(streaked > single);
    }

    [Fact]
    public void Streak_multiplier_is_capped()
    {
        var allowed = TimeSpan.FromSeconds(12);

        // Beyond the cap the multiplier must stop growing, or a long run runs away.
        var atCap = GameRules.ScoreFor(TimeSpan.Zero, allowed, 11);
        var wellPastCap = GameRules.ScoreFor(TimeSpan.Zero, allowed, 500);

        Assert.Equal(atCap, wellPastCap);
    }

    [Fact]
    public void A_zero_length_round_still_scores_without_dividing_by_zero()
    {
        Assert.True(GameRules.ScoreFor(TimeSpan.Zero, TimeSpan.Zero, 1) > 0);
    }

    [Fact]
    public void Remaining_time_beyond_the_allowance_does_not_inflate_the_bonus()
    {
        var allowed = TimeSpan.FromSeconds(12);

        var exact = GameRules.ScoreFor(allowed, allowed, 1);
        var impossible = GameRules.ScoreFor(TimeSpan.FromSeconds(600), allowed, 1);

        Assert.Equal(exact, impossible);
    }

    [Fact]
    public void A_lucky_roll_wins_a_life()
    {
        Assert.True(GameRules.AwardsBonusLife(lives: 1, roll: 0d));
    }

    [Fact]
    public void An_ordinary_roll_wins_nothing()
    {
        // The boundary belongs to "no life": the chance is the share of rolls below it.
        Assert.False(GameRules.AwardsBonusLife(lives: 1, roll: GameRules.BonusLifeChance));
        Assert.False(GameRules.AwardsBonusLife(lives: 1, roll: 0.99d));
    }

    [Fact]
    public void Any_life_count_below_the_cap_can_still_win_one()
    {
        for (var lives = 0; lives < GameRules.MaxLives; lives++)
        {
            Assert.True(GameRules.AwardsBonusLife(lives, roll: 0d), $"no life offered on {lives}");
        }
    }

    [Fact]
    public void Lives_never_grow_past_the_cap()
    {
        Assert.False(GameRules.AwardsBonusLife(GameRules.MaxLives, roll: 0d));

        // Defensive: a hand-edited save could in principle start a run above the cap.
        Assert.False(GameRules.AwardsBonusLife(GameRules.MaxLives + 3, roll: 0d));
    }

    [Fact]
    public void A_bonus_life_stays_an_occasional_reward()
    {
        Assert.InRange(GameRules.BonusLifeChance, 0d, 0.5d);
    }

    [Fact]
    public void The_most_generous_start_is_the_life_cap()
    {
        // Otherwise the kindest starting-lives setting would begin a run already able
        // to hold more than the rules allow, or unable to ever win one.
        Assert.Equal(GameRules.MaxLives, GameSettings.AllowedLives.Max());
    }

    [Fact]
    public void The_normal_profile_reproduces_the_default_ramp_exactly()
    {
        // Guards the refactor: adding profiles must not have moved Normal by a single round.
        for (var i = 0; i <= 300; i++)
        {
            Assert.Equal(GameRules.OptionCountFor(i), GameRules.OptionCountFor(i, DifficultyProfile.Normal));
            Assert.Equal(GameRules.RoundDurationFor(i), GameRules.RoundDurationFor(i, DifficultyProfile.Normal));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(50)]
    public void Learning_holds_its_grid_and_its_clock(int correct)
    {
        Assert.Equal(4, GameRules.OptionCountFor(correct, DifficultyProfile.Learning));
        Assert.Equal(TimeSpan.FromSeconds(12), GameRules.RoundDurationFor(correct, DifficultyProfile.Learning));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(3, 5)]
    [InlineData(7, 6)]
    [InlineData(50, 6)]
    public void Hard_opens_at_four_and_stops_at_six(int correct, int expected)
    {
        Assert.Equal(expected, GameRules.OptionCountFor(correct, DifficultyProfile.Hard));
    }

    [Fact]
    public void Hard_still_starts_on_a_full_clock()
    {
        Assert.Equal(TimeSpan.FromSeconds(12), GameRules.RoundDurationFor(0, DifficultyProfile.Hard));
    }

    [Fact]
    public void A_mode_with_no_chance_of_a_life_never_awards_one()
    {
        // Hard mode's "no way to earn a life" is these odds, not a branch somewhere.
        Assert.False(GameRules.AwardsBonusLife(lives: 1, roll: 0d, chance: 0d));
    }
}
