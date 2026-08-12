using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>Builds successive rounds of "Guess the Flag".</summary>
public interface IQuestionGenerator
{
    /// <summary>
    /// Produces the next round with exactly <paramref name="optionCount"/> options,
    /// one of which is the answer. Options within a round are always distinct.
    /// </summary>
    FlagQuestion Next(int optionCount);
}
