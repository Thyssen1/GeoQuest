using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Platform;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Find the City's asset adapter, the counterpart to <see cref="FlagImageLoader"/> and
/// <see cref="OutlineLoader"/>: the one place that knows where the city data and the world
/// map live and what coordinate convention they are written in.
///
/// It loads two files that have to agree with each other — the capitals and the map they
/// are found on — which is why they are read by one type rather than two.
/// </summary>
public sealed class CityLoader
{
    private const string CapitalsUri = "avares://GeoQuest/Assets/Cities/cities.json";
    private const string WorldUri = "avares://GeoQuest/Assets/Maps/world.json";

    private IReadOnlyList<Capital>? _capitals;
    private Geometry? _world;
    private bool _worldLoaded;

    /// <summary>
    /// Every capital the game can ask about, in ISO code order. Parsed once: the file is
    /// twelve kilobytes and every round needs all of it to pick a question.
    /// </summary>
    public IReadOnlyList<Capital> Capitals()
    {
        if (_capitals is not null)
        {
            return _capitals;
        }

        var capitals = new List<Capital>();

        try
        {
            using var stream = AssetLoader.Open(new Uri(CapitalsUri));
            using var document = JsonDocument.Parse(stream);

            foreach (var entry in document.RootElement.GetProperty("capitals").EnumerateObject())
            {
                capitals.Add(new Capital(
                    Code: entry.Name,
                    Name: entry.Value.GetProperty("name").GetString() ?? entry.Name,
                    Location: new GeoPoint(
                        entry.Value.GetProperty("lat").GetDouble(),
                        entry.Value.GetProperty("lon").GetDouble())));
            }
        }
        catch (Exception)
        {
            // An unreadable city file leaves the mode with nothing to ask rather than
            // stopping the app, matching how a missing outline is handled.
            capitals.Clear();
        }

        _capitals = capitals;
        return _capitals;
    }

    /// <summary>
    /// The world as one geometry, in the units world.json is written in: x across 0..1,
    /// y down 0..0.5. Drawn filled it is the land; stroked as well, the same rings are the
    /// borders, which is how the map can be shown with or without them.
    /// </summary>
    public Geometry? World()
    {
        if (_worldLoaded)
        {
            return _world;
        }

        _worldLoaded = true;

        try
        {
            using var stream = AssetLoader.Open(new Uri(WorldUri));
            using var document = JsonDocument.Parse(stream);

            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                foreach (var ring in document.RootElement.GetProperty("rings").EnumerateArray())
                {
                    var length = ring.GetArrayLength();

                    // Three points, six numbers: fewer encloses no area.
                    if (length < 6)
                    {
                        continue;
                    }

                    context.BeginFigure(new Avalonia.Point(ring[0].GetDouble(), ring[1].GetDouble()), isFilled: true);

                    for (var i = 1; i < length / 2; i++)
                    {
                        context.LineTo(new Avalonia.Point(ring[i * 2].GetDouble(), ring[(i * 2) + 1].GetDouble()));
                    }

                    context.EndFigure(isClosed: true);
                }
            }

            _world = geometry;
        }
        catch (Exception)
        {
            _world = null;
        }

        return _world;
    }
}
