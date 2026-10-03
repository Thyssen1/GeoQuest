namespace GeoQuest.Models;

/// <summary>
/// How the player answers a round. The round lifecycle, clock, scoring and lives are the
/// same either way; only the question and the input differ.
/// </summary>
public enum RoundInput
{
    /// <summary>A grid of flags, one of which belongs to the country named in the prompt.</summary>
    Grid,

    /// <summary>One flag, and the player names the country it belongs to.</summary>
    Name,
}
