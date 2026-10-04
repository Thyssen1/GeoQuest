using System.Text;
using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.Tests;

public class QuestionGeneratorTests
{
    private static JsonCountryRepository LoadRepository()
    {
        using var stream = File.OpenRead(TestPaths.CountriesJson);
        return JsonCountryRepository.Load(stream);
    }

    /// <summary>Seeded so a failure is reproducible rather than a one-off flake.</summary>
    private static RandomQuestionGenerator NewGenerator(int seed = 12345)
    {
        return new RandomQuestionGenerator(LoadRepository(), new Random(seed));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Produces_the_requested_number_of_options(int optionCount)
    {
        var generator = NewGenerator();

        for (var i = 0; i < 500; i++)
        {
            Assert.Equal(optionCount, generator.Next(optionCount).Options.Count);
        }
    }

    [Fact]
    public void The_answer_is_always_among_the_options()
    {
        var generator = NewGenerator();

        for (var i = 0; i < 2000; i++)
        {
            var question = generator.Next(3 + i % 4);
            Assert.Contains(question.Options, o => o.Code == question.Answer.Code);
        }
    }

    [Fact]
    public void A_round_never_shows_the_same_flag_twice()
    {
        var generator = NewGenerator();

        for (var i = 0; i < 2000; i++)
        {
            var options = generator.Next(3 + i % 4).Options;
            Assert.Equal(options.Count, options.Select(o => o.Code).Distinct().Count());
        }
    }

    [Fact]
    public void Distractors_come_only_from_the_sovereign_pool()
    {
        var generator = NewGenerator();

        for (var i = 0; i < 2000; i++)
        {
            Assert.All(generator.Next(6).Options, o => Assert.Equal(CountryKind.Sovereign, o.Kind));
        }
    }

    [Fact]
    public void The_same_answer_never_repeats_back_to_back()
    {
        var generator = NewGenerator();
        string? previous = null;

        for (var i = 0; i < 5000; i++)
        {
            var answer = generator.Next(4).Answer.Code;
            Assert.NotEqual(previous, answer);
            previous = answer;
        }
    }

    [Fact]
    public void Every_country_in_the_pool_can_be_drawn_as_an_answer()
    {
        var generator = NewGenerator();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 20000; i++)
        {
            seen.Add(generator.Next(4).Answer.Code);
        }

        Assert.Equal(LoadRepository().QuestionPool.Count, seen.Count);
    }

    [Fact]
    public void Answers_are_distributed_evenly()
    {
        var generator = NewGenerator();
        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < 20000; i++)
        {
            var code = generator.Next(4).Answer.Code;
            histogram[code] = histogram.GetValueOrDefault(code) + 1;
        }

        var min = histogram.Values.Min();
        var max = histogram.Values.Max();

        // Loose bound: catches a badly skewed draw without failing on ordinary variance.
        Assert.True(max < min * 2, $"answer distribution too skewed: min {min}, max {max}");
    }

    [Fact]
    public void Rejects_fewer_options_than_the_game_allows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewGenerator().Next(GameRules.MinOptions - 1));
    }

    [Fact]
    public void Rejects_more_options_than_the_pool_can_supply()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewGenerator().Next(100_000));
    }

    [Fact]
    public void Rejects_a_pool_too_small_to_fill_a_round()
    {
        var tiny = LoadFrom("""
            [{"code":"dk","name":"Denmark","region":"Europe","kind":"sovereign"},
             {"code":"se","name":"Sweden","region":"Europe","kind":"sovereign"}]
            """);

        Assert.Throws<ArgumentException>(() => new RandomQuestionGenerator(tiny));
    }

    private static JsonCountryRepository LoadFrom(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return JsonCountryRepository.Load(stream);
    }
}
