using System.Collections.Generic;

namespace GeoQuest.Models;

/// <summary>
/// A single "Guess the Flag" round: show <see cref="Answer"/>'s name, and let the
/// player pick from <see cref="Options"/>, which always contains the answer exactly once.
/// </summary>
public sealed record FlagQuestion
{
    public required Country Answer { get; init; }

    /// <summary>The full option set in presentation order, already shuffled.</summary>
    public required IReadOnlyList<Country> Options { get; init; }
}
