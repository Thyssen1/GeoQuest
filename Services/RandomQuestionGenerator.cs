using System;
using System.Collections.Generic;
using System.Linq;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Draws rounds uniformly at random from the question pool, with two fairness rules:
/// options within a round are always distinct, and a country that was just the answer
/// will not be the answer again until a window of other countries has been used.
/// </summary>
public sealed class RandomQuestionGenerator : IQuestionGenerator
{
    /// <summary>How much of the pool to hold back from reuse as an answer, as a fraction.</summary>
    private const double RecentWindowFraction = 0.25d;

    private readonly IReadOnlyList<Country> _pool;
    private readonly Random _random;
    private readonly Queue<string> _recentAnswers = new();
    private readonly HashSet<string> _recentAnswerCodes = new(StringComparer.Ordinal);
    private readonly int _recentWindow;

    public RandomQuestionGenerator(ICountryRepository repository, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _pool = repository.QuestionPool;
        _random = random ?? Random.Shared;

        if (_pool.Count < GameRules.MaxOptions)
        {
            throw new ArgumentException(
                $"Question pool needs at least {GameRules.MaxOptions} countries but had {_pool.Count}.",
                nameof(repository));
        }

        // Leave enough headroom that excluding recent answers can never starve the draw.
        _recentWindow = Math.Max(0, Math.Min(
            (int)(_pool.Count * RecentWindowFraction),
            _pool.Count - GameRules.MaxOptions - 1));
    }

    public FlagQuestion Next(int optionCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(optionCount, GameRules.MinOptions);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(optionCount, _pool.Count);

        var answer = PickAnswer();
        var options = new List<Country>(optionCount) { answer };

        // Reservoir of everything the answer could be confused with this round.
        var candidates = _pool.Where(c => c.Code != answer.Code).ToArray();

        // Partial Fisher-Yates: we only need the first (optionCount - 1) of the shuffle.
        var needed = optionCount - 1;
        for (var i = 0; i < needed; i++)
        {
            var j = _random.Next(i, candidates.Length);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            options.Add(candidates[i]);
        }

        var ordered = options.ToArray();
        _random.Shuffle(ordered);

        Remember(answer);

        return new FlagQuestion { Answer = answer, Options = ordered };
    }

    private Country PickAnswer()
    {
        // The window is sized so at least one country is always outside it.
        Country candidate;
        do
        {
            candidate = _pool[_random.Next(_pool.Count)];
        }
        while (_recentAnswerCodes.Contains(candidate.Code));

        return candidate;
    }

    private void Remember(Country answer)
    {
        if (_recentWindow == 0)
        {
            return;
        }

        _recentAnswers.Enqueue(answer.Code);
        _recentAnswerCodes.Add(answer.Code);

        if (_recentAnswers.Count > _recentWindow)
        {
            _recentAnswerCodes.Remove(_recentAnswers.Dequeue());
        }
    }
}
