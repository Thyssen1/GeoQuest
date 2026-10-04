using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GeoQuest.Services;

/// <summary>Resolves a country code to its flag bitmap.</summary>
public interface IFlagImageLoader
{
    Bitmap? Load(string code);
}

/// <summary>
/// Loads flags from the embedded <c>avares://</c> resources produced by tools/FlagConverter.
/// Bitmaps are cached because the same flag reappears constantly as a distractor and
/// decoding it per round would churn during play.
///
/// This is the asset adapter: it is the one place that knows the resource layout, which
/// is what keeps <see cref="JsonCountryRepository"/> and the game logic free of Avalonia.
/// </summary>
public sealed class FlagImageLoader : IFlagImageLoader, ICountryArtwork
{
    private const string FlagUriFormat = "avares://GeoQuest/Assets/Flags/{0}.png";

    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public object? For(string code) => Load(code);

    public Bitmap? Load(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (_cache.TryGetValue(code, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;

        try
        {
            var uri = new Uri(string.Format(FlagUriFormat, code));

            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                bitmap = new Bitmap(stream);
            }
        }
        catch (Exception)
        {
            // A missing or corrupt flag must not take the game down mid-round; the
            // option simply renders empty and the null is cached so we stop retrying.
            bitmap = null;
        }

        _cache[code] = bitmap;
        return bitmap;
    }
}
