using System;
using System.Text.Json.Serialization;

namespace GeoQuest.Models;

/// <summary>
/// What the player has shown about one flag. Counts are kept rather than verdicts, so a
/// rule over them can be sharpened later without rewriting anyone's saved history.
/// </summary>
public sealed record FlagHistory
{
    /// <summary>Times this flag has been the answer.</summary>
    [JsonPropertyName("seen")]
    public int Seen { get; init; }

    /// <summary>Times it was answered correctly. Misses are the difference, never stored.</summary>
    [JsonPropertyName("correct")]
    public int Correct { get; init; }

    /// <summary>Leitner box: 0 unseen, 1-3 learning, 4 mastered.</summary>
    [JsonPropertyName("box")]
    public int Box { get; init; }

    /// <summary>Consecutive correct answers. Cannot be derived from the counts.</summary>
    [JsonPropertyName("streak")]
    public int Streak { get; init; }

    /// <summary>
    /// When it was last asked. Nothing reads it yet; it is stored because a timestamp is
    /// the one thing that cannot be reconstructed after the fact.
    /// </summary>
    [JsonPropertyName("lastSeen")]
    public DateTimeOffset? LastSeen { get; init; }
}
