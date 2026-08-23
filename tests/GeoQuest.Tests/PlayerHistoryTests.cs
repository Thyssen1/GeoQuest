using GeoQuest.Models;

namespace GeoQuest.Tests;

public class PlayerHistoryTests
{
    private static PlayerHistory NewHistory(int poolSize = 197) => new(poolSize: poolSize);

    [Fact]
    public void A_flag_never_asked_reads_as_unseen()
    {
        var history = NewHistory();

        Assert.Equal(LearningRules.Pool, history.BoxOf("dk"));
        Assert.Equal(0, history.For("dk").Seen);
    }

    [Fact]
    public void Recording_counts_the_answer()
    {
        var history = NewHistory();

        history.Record("dk", correct: true, fast: true);
        history.Record("dk", correct: false, fast: false);

        var flag = history.For("dk");

        Assert.Equal(2, flag.Seen);
        Assert.Equal(1, flag.Correct);
    }

    [Fact]
    public void A_miss_resets_the_streak()
    {
        var history = NewHistory();

        history.Record("dk", correct: true, fast: true);
        history.Record("dk", correct: true, fast: true);
        Assert.Equal(2, history.For("dk").Streak);

        history.Record("dk", correct: false, fast: false);
        Assert.Equal(0, history.For("dk").Streak);
    }

    [Fact]
    public void Three_quick_answers_master_a_flag()
    {
        var history = NewHistory();

        for (var i = 0; i < 3; i++)
        {
            history.Record("dk", correct: true, fast: true);
        }

        Assert.Equal(LearningRules.GraduatedBox, history.BoxOf("dk"));
        Assert.Equal(1, history.Graduated);
    }

    [Fact]
    public void Mastery_falls_when_a_mastered_flag_is_missed()
    {
        var history = NewHistory(poolSize: 100);

        for (var i = 0; i < 3; i++)
        {
            history.Record("dk", correct: true, fast: true);
        }

        Assert.Equal(1, history.MasteryPercent);

        history.Record("dk", correct: false, fast: false);

        Assert.Equal(0, history.Graduated);
        Assert.Equal(0, history.MasteryPercent);
    }

    [Fact]
    public void Recording_reports_when_the_mastered_count_moved()
    {
        var history = NewHistory();

        Assert.False(history.Record("dk", correct: true, fast: true));
        Assert.False(history.Record("dk", correct: true, fast: true));
        Assert.True(history.Record("dk", correct: true, fast: true));
        Assert.False(history.Record("dk", correct: true, fast: true));
        Assert.True(history.Record("dk", correct: false, fast: false));
    }

    [Fact]
    public void A_slow_correct_answer_never_masters_anything()
    {
        var history = NewHistory();

        for (var i = 0; i < 20; i++)
        {
            history.Record("dk", correct: true, fast: false);
        }

        Assert.Equal(0, history.Graduated);
    }

    [Fact]
    public void Flags_are_tracked_separately()
    {
        var history = NewHistory();

        history.Record("dk", correct: true, fast: true);
        history.Record("se", correct: false, fast: false);

        Assert.Equal(1, history.For("dk").Correct);
        Assert.Equal(0, history.For("se").Correct);
        Assert.Equal(2, history.Flags.Count);
    }

    [Fact]
    public void Codes_are_matched_regardless_of_case()
    {
        var history = NewHistory();

        history.Record("dk", correct: true, fast: true);

        Assert.Equal(1, history.For("DK").Seen);
    }
}
