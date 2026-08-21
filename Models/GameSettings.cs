using System.Text.Json.Serialization;

namespace GeoQuest.Models;

/// <summary>
/// Player-adjustable settings, persisted between sessions. Kept separate from the
/// score file so a corrupt settings file can never cost someone their best score.
/// </summary>
public sealed record GameSettings
{
    /// <summary>Lives a run starts with. Constrained to <see cref="AllowedLives"/>.</summary>
    [JsonPropertyName("startingLives")]
    public int StartingLives { get; init; } = DefaultStartingLives;

    public const int DefaultStartingLives = 3;

    /// <summary>The choices offered in Options, from a gentle run to a single mistake.</summary>
    public static readonly int[] AllowedLives = [1, 3, 5];

    /// <summary>Clamps anything unexpected from disk back to a playable value.</summary>
    public GameSettings Sanitised() =>
        System.Array.IndexOf(AllowedLives, StartingLives) >= 0
            ? this
            : this with { StartingLives = DefaultStartingLives };
}
