using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.Tests;

public class LearningQuestionGeneratorTests
{
    private static JsonCountryRepository LoadRepository()
    {
        using var stream = File.OpenRead(TestPaths.CountriesJson);
        return JsonCountryRepository.Load(stream);
    }

    /// <summary>Plays a session, answering every round the same way, and reports what was asked.</summary>
    private static (List<string> Asked, PlayerHistory History) Play(
        int rounds, bool correct, bool fast, int seed = 4242)
    {
        var repository = LoadRepository();
        var history = new PlayerHistory(poolSize: repository.QuestionPool.Count);
        var generator = new LearningQuestionGenerator(repository, history, new Random(seed));
        var asked = new List<string>();

        for (var i = 0; i < rounds; i++)
        {
            var answer = generator.Next(4).Answer.Code;

            asked.Add(answer);
            history.Record(answer, correct, fast);
        }

        return (asked, history);
    }

    [Fact]
    public void Returns_to_the_same_flags_rather_than_touring_the_world()
    {
        // A uniform draw over ~197 countries would show roughly 80 distinct flags in 100
        // rounds, which is far too sparse for anything to be learned. Concentrating on a
        // working set is the whole point of the mode.
        var (asked, _) = Play(rounds: 100, correct: true, fast: true);

        Assert.True(asked.Distinct().Count() < 60, $"asked {asked.Distinct().Count()} distinct flags");
    }

    [Fact]
    public void Answering_well_actually_masters_flags()
    {
        var (_, history) = Play(rounds: 120, correct: true, fast: true);

        Assert.True(history.Graduated >= 20, $"only {history.Graduated} mastered");
        Assert.True(history.MasteryPercent > 0);
    }

    [Fact]
    public void Answering_badly_masters_nothing_and_keeps_the_set_small()
    {
        var (asked, history) = Play(rounds: 100, correct: false, fast: false);

        Assert.Equal(0, history.Graduated);

        // Nothing graduates, so nothing new is introduced past the working set.
        Assert.True(asked.Distinct().Count() <= LearningRules.FocusLimit + 1);
    }

    [Fact]
    public void The_same_flag_does_not_come_back_immediately()
    {
        var (asked, _) = Play(rounds: 200, correct: true, fast: true);

        for (var i = 1; i < asked.Count; i++)
        {
            var window = asked.Skip(Math.Max(0, i - 5)).Take(Math.Min(5, i)).ToList();

            Assert.DoesNotContain(asked[i], window);
        }
    }

    [Fact]
    public void Mastered_flags_are_checked_again_now_and_then()
    {
        // A mastered box that is never revisited measures what someone once knew.
        var (asked, history) = Play(rounds: 400, correct: true, fast: true);

        var graduatedAsked = asked
            .Skip(200)
            .Count(code => history.BoxOf(code) >= LearningRules.GraduatedBox);

        Assert.True(graduatedAsked > 0, "no mastered flag was ever re-tested");
    }

    [Fact]
    public void Every_round_is_still_a_well_formed_question()
    {
        var repository = LoadRepository();
        var history = new PlayerHistory(poolSize: repository.QuestionPool.Count);
        var generator = new LearningQuestionGenerator(repository, history, new Random(7));

        for (var i = 0; i < 200; i++)
        {
            var question = generator.Next(4);

            Assert.Equal(4, question.Options.Count);
            Assert.Contains(question.Options, o => o.Code == question.Answer.Code);
            Assert.Equal(4, question.Options.Select(o => o.Code).Distinct().Count());

            history.Record(question.Answer.Code, correct: true, fast: true);
        }
    }
}
