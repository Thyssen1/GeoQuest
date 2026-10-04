using System.Text.Json.Serialization;

namespace GeoQuest.Models;

/// <summary>
/// Player-adjustable settings, persisted between sessions. Kept separate from the
/// score file so a corrupt settings file can never cost someone their best score.
/// </summary>
public sealed record GameSettings
{
    [JsonPropertyName("startingLives")]
    public int StartingLives { get; init; } = DefaultStartingLives;
    
    [JsonPropertyName("soundEnabled")]
    public bool SoundEnabled { get; init; } = true;
    
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(JsonStringEnumConverter<GameMode>))]
    public GameMode Mode { get; init; } = GameMode.Normal;

    /// <summary>The mini-game of the last run, offered again first for the same reason.</summary>
    [JsonPropertyName("game")]
    [JsonConverter(typeof(JsonStringEnumConverter<MiniGame>))]
    public MiniGame Game { get; init; } = MiniGame.Flags;

    public const int DefaultStartingLives = 3;
    public static readonly int[] AllowedLives = [1, 3, 5];

    /// <summary>Clamps anything unexpected from disk back to a playable value.</summary>
    public GameSettings Sanitised()
    {
        var sanitised = this;

        if (System.Array.IndexOf(AllowedLives, StartingLives) < 0)
        {
            sanitised = sanitised with { StartingLives = DefaultStartingLives };
        }

        // A number outside the enum survives deserialisation; a mode or game that does
        // not exist would leave Play with nothing to preselect.
        if (!System.Enum.IsDefined(Mode))
        {
            sanitised = sanitised with { Mode = GameMode.Normal };
        }

        if (!System.Enum.IsDefined(Game))
        {
            sanitised = sanitised with { Game = MiniGame.Flags };
        }

        return sanitised;
    }
}
