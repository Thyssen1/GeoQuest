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
}
