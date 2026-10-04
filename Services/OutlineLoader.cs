using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Platform;

namespace GeoQuest.Services;

/// <summary>
/// Loads country outlines from the file produced by tools/BorderBaker. Geometry is built
/// on first use and cached, because the same country reappears constantly as a distractor
/// and rebuilding a few hundred points per round would churn during play.
///
/// This is the border game's asset adapter, the counterpart to <see cref="FlagImageLoader"/>:
/// the one place that knows the resource layout and the coordinate convention.
/// </summary>
public sealed class OutlineLoader : ICountryArtwork
{
    private const string OutlinesUri = "avares://GeoQuest/Assets/Borders/borders.json";

    private readonly Dictionary<string, Geometry?> _cache = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, double[][]>? _rings;

    public object? For(string code) => Load(code);

    public Geometry? Load(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (_cache.TryGetValue(code, out var cached))
        {
            return cached;
        }

        Geometry? geometry = null;

        try
        {
            if (Rings().TryGetValue(code, out var rings))
            {
                geometry = Build(rings);
            }
        }
        catch (Exception)
        {
            // A missing or malformed outline must not take the game down mid-round; the
            // option simply renders empty and the null is cached so we stop retrying.
            geometry = null;
        }

        _cache[code] = geometry;
        return geometry;
    }

    private static Geometry Build(double[][] rings)
    {
        var geometry = new StreamGeometry();

        using var context = geometry.Open();

        foreach (var ring in rings)
        {
            if (ring.Length < 6)
            {
                continue;
            }

            context.BeginFigure(new Avalonia.Point(ring[0], ring[1]), isFilled: true);

            for (var i = 1; i < ring.Length / 2; i++)
            {
                context.LineTo(new Avalonia.Point(ring[i * 2], ring[(i * 2) + 1]));
            }

            context.EndFigure(isClosed: true);
        }

        return geometry;
    }

    /// <summary>Parsed once: the whole file is a few hundred KB and every round needs it.</summary>
    private Dictionary<string, double[][]> Rings()
    {
        if (_rings is not null)
        {
            return _rings;
        }

        _rings = new Dictionary<string, double[][]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var stream = AssetLoader.Open(new Uri(OutlinesUri));
            using var document = JsonDocument.Parse(stream);

            foreach (var entry in document.RootElement.GetProperty("outlines").EnumerateObject())
            {
                var rings = new List<double[]>();

                foreach (var ring in entry.Value.EnumerateArray())
                {
                    var points = new double[ring.GetArrayLength()];

                    for (var i = 0; i < points.Length; i++)
                    {
                        points[i] = ring[i].GetDouble();
                    }

                    rings.Add(points);
                }

                _rings[entry.Name] = [.. rings];
            }
        }
        catch (Exception)
        {
            // An unreadable outline file leaves every country without a shape rather than
            // stopping the app; the border mode is simply unplayable until it is fixed.
        }

        return _rings;
    }
}
