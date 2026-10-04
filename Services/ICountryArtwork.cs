namespace GeoQuest.Services;

/// <summary>
/// Whatever a round draws to stand for a country: its flag in the flag games, its outline
/// in the border game. The game does not care which, so the mode picks the source and the
/// view renders whatever comes back.
/// </summary>
public interface ICountryArtwork
{
    /// <summary>Artwork for a country code, or null if the asset is missing.</summary>
    object? For(string code);
}
