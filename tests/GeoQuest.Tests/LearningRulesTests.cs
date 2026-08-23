using GeoQuest.Models;

namespace GeoQuest.Tests;

public class LearningRulesTests
{
    [Fact]
    public void A_flag_already_known_climbs_from_the_first_sighting()
    {
        // Most players arrive knowing dozens of flags. Making them spend a round being
        // introduced to one they can name on sight is busywork.
        Assert.Equal(2, LearningRules.NextBox(LearningRules.Pool, correct: true, fast: true));
    }

    [Fact]
    public void A_flag_missed_on_sight_joins_the_learning_set()
    {
        Assert.Equal(LearningRules.FirstBox, LearningRules.NextBox(LearningRules.Pool, correct: false, fast: false));
        Assert.Equal(LearningRules.FirstBox, LearningRules.NextBox(LearningRules.Pool, correct: true, fast: false));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public void A_quick_correct_answer_promotes(int box, int expected)
    {
        Assert.Equal(expected, LearningRules.NextBox(box, correct: true, fast: true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_slow_correct_answer_holds_ground(int box)
    {
        // At three or four options a slow correct answer is often a guess that landed.
        Assert.Equal(box, LearningRules.NextBox(box, correct: true, fast: false));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    public void A_miss_costs_a_box_but_never_leaves_the_set(int box, int expected)
    {
        Assert.Equal(expected, LearningRules.NextBox(box, correct: false, fast: true));
    }

    [Fact]
    public void A_mastered_flag_stays_mastered_while_it_is_answered()
    {
        Assert.Equal(
            LearningRules.GraduatedBox,
            LearningRules.NextBox(LearningRules.GraduatedBox, correct: true, fast: true));
    }

    [Fact]
    public void A_failed_retest_drops_two_boxes()
    {
        // One box would leave it a single good answer from graduating again, which is too
        // cheap for something just shown to be forgotten.
        Assert.Equal(2, LearningRules.NextBox(LearningRules.GraduatedBox, correct: false, fast: false));
    }

    [Fact]
    public void Boxes_never_leave_their_range()
    {
        for (var box = -5; box <= 10; box++)
        {
            foreach (var correct in new[] { true, false })
            {
                var next = LearningRules.NextBox(box, correct, fast: true);
                Assert.InRange(next, LearningRules.FirstBox, LearningRules.GraduatedBox);
            }
        }
    }

    [Fact]
    public void Half_the_clock_is_the_line_between_knowing_and_working_it_out()
    {
        var allowed = TimeSpan.FromSeconds(12);

        Assert.True(LearningRules.IsFast(TimeSpan.FromSeconds(6), allowed));
        Assert.True(LearningRules.IsFast(TimeSpan.FromSeconds(11), allowed));
        Assert.False(LearningRules.IsFast(TimeSpan.FromSeconds(5.9), allowed));
        Assert.False(LearningRules.IsFast(TimeSpan.Zero, allowed));
    }

    [Fact]
    public void A_zero_length_round_is_never_fast()
    {
        Assert.False(LearningRules.IsFast(TimeSpan.Zero, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0, 197, 0)]
    [InlineData(197, 197, 100)]
    [InlineData(99, 197, 50)]
    public void Mastery_is_a_share_of_the_pool(int graduated, int total, int expected)
    {
        Assert.Equal(expected, LearningRules.MasteryPercent(graduated, total));
    }

    [Fact]
    public void Mastery_of_an_empty_pool_is_zero_rather_than_a_divide_by_zero()
    {
        Assert.Equal(0, LearningRules.MasteryPercent(5, 0));
    }
}
