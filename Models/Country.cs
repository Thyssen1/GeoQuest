using System.Text.Json.Serialization;

namespace GeoQuest.Models;

/// <summary>
/// What an entry in the flag set actually represents. Only <see cref="Sovereign"/>
/// entries are used as questions or distractors by default — offering Scotland
/// alongside the United Kingdom, or the EU flag, does not make for a fair round.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CountryKind>))]
public enum CountryKind
{
    /// <summary>A sovereign state.</summary>
    Sovereign,

    /// <summary>A dependency, overseas territory or similar (Greenland, Guam, Réunion).</summary>
    Territory,

    /// <summary>A subdivision of a sovereign state (England, Scotland, Wales, Northern Ireland).</summary>
    Subdivision,

    /// <summary>Anything else with a flag in the set, such as the European Union.</summary>
    Other,
}

/// <summary>
/// One entry from Assets/countries.json. The ISO 3166-1 alpha-2 <see cref="Code"/> is
/// the join key across every mini-game: it addresses the flag image today, and will
/// address country outlines and city lists in later milestones.
/// </summary>
public sealed record Country
{
    /// <summary>Lowercase ISO 3166-1 alpha-2 code, e.g. <c>dk</c>. Matches the flag filename.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>English display name shown as the question prompt, e.g. <c>Denmark</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Continent-level grouping. Used to bias distractor choice and, later, to filter by region.</summary>
    [JsonPropertyName("region")]
    public required string Region { get; init; }

    [JsonPropertyName("kind")]
    public required CountryKind Kind { get; init; }
}
