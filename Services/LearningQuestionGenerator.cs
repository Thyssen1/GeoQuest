using System;
using System.Collections.Generic;
using System.Linq;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Draws rounds from the flags the player is currently learning rather than from the whole
/// world. A uniform draw over 197 countries would ask about the same flag once every couple
/// of hundred rounds, which is far too sparse for anything to be learned or graduated; this
/// keeps a working set of <see cref="LearningRules.FocusLimit"/> flags and returns to them.
/// </summary>
public sealed class LearningQuestionGenerator : IQuestionGenerator
{
    /// <summary>How often a mastered flag is checked again, so the count stays honest.</summary>
    private const double GraduatedRetestChance = 0.08d;

    /// <summary>Shares of the remaining rounds given to the three learning boxes.</summary>
    private const double FirstBoxShare = 0.55d;
    private const double SecondBoxShare = 0.78d;

    /// <summary>Answers held back from repeating. Small, because the focus set is small.</summary>
    private const int RecentWindow = 5;

    private readonly IReadOnlyList<Country> _pool;
    private readonly PlayerHistory _history;
    private readonly Random _random;
    private readonly Queue<string> _recent = new();

    public LearningQuestionGenerator(ICountryRepository repository, PlayerHistory history, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(history);

        _pool = repository.QuestionPool;
        _history = history;
        _random = random ?? Random.Shared;

        if (_pool.Count < GameRules.MaxOptions)
        {
            throw new ArgumentException(
                $"Question pool needs at least {GameRules.MaxOptions} countries but had {_pool.Count}.",
                nameof(repository));
        }
    }

    public FlagQuestion Next(int optionCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(optionCount, GameRules.MinOptions);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(optionCount, _pool.Count);

        var answer = PickAnswer();

        Remember(answer);

        return QuestionBuilder.Build(answer, _pool, optionCount, _random);
    }

    /// <summary>
    /// The flags being worked on right now: everything already in a learning box, oldest and
    /// least-known first, topped up from the unseen pool until the window is full. New flags
    /// arrive only as older ones graduate, which is what stops a session overwhelming anyone.
    /// </summary>
    private List<Country> BuildFocus()
    {
        var focus = _pool
            .Where(country => IsLearning(_history.BoxOf(country.Code)))
            .OrderBy(country => _history.BoxOf(country.Code))
            .ThenBy(country => _history.For(country.Code).LastSeen ?? DateTimeOffset.MinValue)
            .Take(LearningRules.FocusLimit)
            .ToList();

        if (focus.Count >= LearningRules.FocusLimit)
        {
            return focus;
        }

        var unseen = _pool
            .Where(country => _history.BoxOf(country.Code) == LearningRules.Pool)
            .ToList();

        while (focus.Count < LearningRules.FocusLimit && unseen.Count > 0)
        {
            var index = _random.Next(unseen.Count);

            focus.Add(unseen[index]);
            unseen.RemoveAt(index);
        }

        return focus;
    }

    private Country PickAnswer()
    {
        var graduated = _pool.Where(country => _history.BoxOf(country.Code) >= LearningRules.GraduatedBox).ToList();

        // A mastered box that is never checked measures what someone once knew, not what
        // they know, so a small share of rounds goes back to it.
        if (graduated.Count > 0 && _random.NextDouble() < GraduatedRetestChance)
        {
            var retest = Choose(graduated);

            if (retest is not null)
            {
                return retest;
            }
        }

        var focus = BuildFocus();
        var roll = _random.NextDouble();
        var target = roll < FirstBoxShare ? 1 : roll < SecondBoxShare ? 2 : 3;

        // The weights are a preference, not a promise: an empty box falls to a neighbour
        // rather than costing a round.
        for (var box = target; box >= LearningRules.FirstBox; box--)
        {
            if (Choose(InFocusBox(focus, box)) is Country lower)
            {
                return lower;
            }
        }

        for (var box = target + 1; box <= 3; box++)
        {
            if (Choose(InFocusBox(focus, box)) is Country higher)
            {
                return higher;
            }
        }

        // Everything eligible was asked in the last few rounds, so the window is dropped
        // rather than the round: repeating a flag beats having nothing to ask.
        return Choose(focus) ?? focus.FirstOrDefault() ?? _pool[_random.Next(_pool.Count)];
    }

    private IEnumerable<Country> InFocusBox(IEnumerable<Country> focus, int box) =>
        focus.Where(country => EffectiveBox(country) == box);

    /// <summary>
    /// An unseen flag pulled into the window counts as the first box: it is the new pile,
    /// and it has to be reachable by the weighting that favours what is least known.
    /// </summary>
    private int EffectiveBox(Country country) =>
        Math.Max(LearningRules.FirstBox, _history.BoxOf(country.Code));

    private static bool IsLearning(int box) => box >= LearningRules.FirstBox && box < LearningRules.GraduatedBox;

    private Country? Choose(IEnumerable<Country> candidates)
    {
        var eligible = candidates.Where(country => !_recent.Contains(country.Code)).ToArray();

        return eligible.Length == 0 ? null : eligible[_random.Next(eligible.Length)];
    }

    private void Remember(Country answer)
    {
        _recent.Enqueue(answer.Code);

        if (_recent.Count > RecentWindow)
        {
            _recent.Dequeue();
        }
    }
}
