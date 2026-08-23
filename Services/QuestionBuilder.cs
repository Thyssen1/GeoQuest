using System;
using System.Collections.Generic;
using System.Linq;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Turns a chosen answer into a round. Shared by the generators so they differ only in
/// which flag they ask about, which is the only thing a mode should change.
/// </summary>
internal static class QuestionBuilder
{
    public static FlagQuestion Build(Country answer, IReadOnlyList<Country> pool, int optionCount, Random random)
    {
        var options = new List<Country>(optionCount) { answer };

        // Reservoir of everything the answer could be confused with this round.
        var candidates = pool.Where(c => c.Code != answer.Code).ToArray();

        // Partial Fisher-Yates: we only need the first (optionCount - 1) of the shuffle.
        var needed = Math.Min(optionCount - 1, candidates.Length);
        for (var i = 0; i < needed; i++)
        {
            var j = random.Next(i, candidates.Length);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            options.Add(candidates[i]);
        }

        var ordered = options.ToArray();
        random.Shuffle(ordered);

        return new FlagQuestion { Answer = answer, Options = ordered };
    }
}
