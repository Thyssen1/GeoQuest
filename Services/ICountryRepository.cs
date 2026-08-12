using System.Collections.Generic;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>Provides the country set backing every mini-game.</summary>
public interface ICountryRepository
{
    /// <summary>Every entry in the dataset, including territories and subdivisions.</summary>
    IReadOnlyList<Country> All { get; }

    /// <summary>
    /// The subset eligible to appear as an answer or a distractor. Excludes territories,
    /// subdivisions and the EU flag, which would otherwise produce ambiguous rounds.
    /// </summary>
    IReadOnlyList<Country> QuestionPool { get; }
}
