using System.Globalization;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Simplify;

namespace GeoQuest.Tools.BorderBaker;

/// <summary>
/// Dev-time tool. Bakes Natural Earth's admin-0 country polygons into the compact outline
/// file the Guess the Border mini-game draws, so the app ships a few hundred KB of its own
/// geometry rather than a shapefile and a parser.
///
///     dotnet run --project tools/BorderBaker -- --input path\to\ne_10m_admin_0_countries.shp
///
/// Each country is projected, simplified, and scaled onto its own unit square: Russia and
/// Monaco have to present at a comparable size or the round gives the answer away.
/// </summary>
internal static class Program
{
    /// <summary>Simplification tolerance, as a share of the country's own longer side.</summary>
    private const double SimplifyFraction = 0.0035d;

    /// <summary>Islands smaller than this share of the biggest landmass are dropped.</summary>
    private const double IslandFloor = 0.012d;

    /// <summary>
    /// How far the outline may spread beyond its main landmass, as a multiple of that
    /// landmass's own size. France owns French Guiana and Norway owns Svalbard; drawn to
    /// a shared square, those turn the country everyone would recognise into a speck.
    /// </summary>
    private const double SpreadLimit = 1.75d;

    /// <summary>
    /// How far, in degrees, a landmass may sit from the rest of the country and still be
    /// drawn. Corsica and Hokkaido are just offshore; Svalbard and Hawaii are not, and a
    /// quiz that draws them is asking about a different shape than the one people know.
    /// </summary>
    private const double GapLimit = 3d;

    /// <summary>Rings kept per country, largest first. Enough for Greece, not for every skerry.</summary>
    private const int MaxRings = 14;

    /// <summary>Coordinates are stored to this many decimals of a unit square.</summary>
    private const int Decimals = 4;

    /// <summary>
    /// Simplification for the world map, in world units — about a pixel on a map drawn a
    /// thousand across. The world map is seen whole and small, where the per-country
    /// outlines are seen one at a time and large, so it can afford to be much coarser.
    /// </summary>
    private const double WorldTolerance = 0.0009d;

    /// <summary>
    /// Islands smaller than this, in square degrees, are left off the world map. Roughly a
    /// hundred square kilometres at the equator: small enough to keep Malta, large enough
    /// to drop the specks that would cost more bytes than they add recognition.
    /// </summary>
    private const double WorldIslandFloor = 0.01d;

    /// <summary>
    /// Landmasses kept per country before the floor applies at all, largest first. Enough
    /// for an archipelago nation to show the atoll its capital is actually on.
    /// </summary>
    private const int WorldKeepPerCountry = 8;

    private static int Main(string[] args)
    {
        string? input = null;
        string? output = null;
        string? world = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input" when i + 1 < args.Length:
                    input = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--world" when i + 1 < args.Length:
                    world = args[++i];
                    break;
                case "--world":
                    world = string.Empty;
                    break;
                default:
                    Console.Error.WriteLine($"Unrecognised argument: {args[i]}");
                    return 1;
            }
        }

        if (input is null)
        {
            Console.Error.WriteLine("--input is required: the path to ne_10m_admin_0_countries.shp");
            return 1;
        }

        var assets = LocateAssets();
        if (assets is null)
        {
            Console.Error.WriteLine("Could not locate the Assets directory.");
            return 1;
        }

        output ??= Path.Combine(assets, "Borders", "borders.json");

        // The app's own country list is the authority on what belongs in the pool; the
        // shapefile is only asked for the shape of each one.
        var pool = LoadPool(Path.Combine(assets, "countries.json"));
        Console.WriteLine($"Pool: {pool.Count} sovereign countries");

        var shapes = ReadShapes(input);
        Console.WriteLine($"Shapefile: {shapes.Count} usable features");

        var baked = new Dictionary<string, double[][]>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var (code, name) in pool.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!shapes.TryGetValue(code, out var geometry))
            {
                missing.Add($"{code} ({name})");
                continue;
            }

            var rings = Bake(geometry);

            if (rings.Length == 0)
            {
                missing.Add($"{code} ({name}) — no usable ring");
                continue;
            }

            baked[code] = rings;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, Serialise(baked));

        var points = baked.Values.Sum(rings => rings.Sum(ring => ring.Length / 2));
        Console.WriteLine($"Baked {baked.Count} outlines, {points} points -> {output}");
        Console.WriteLine($"{new FileInfo(output).Length / 1024} KB");

        if (world is not null)
        {
            if (world.Length == 0)
            {
                world = Path.Combine(assets, "Maps", "world.json");
            }

            // Every feature, not just the pool: a map with Antarctica and Greenland missing
            // because they are not quiz answers would read as a broken map rather than a
            // deliberate one.
            var landmasses = ReadEveryShape(input).SelectMany(BakeWorld).ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(world)!);
            File.WriteAllText(world, SerialiseWorld(landmasses));

            var worldPoints = landmasses.Sum(ring => ring.Length / 2);
            Console.WriteLine($"Baked world map, {landmasses.Length} rings, {worldPoints} points -> {world}");
            Console.WriteLine($"{new FileInfo(world).Length / 1024} KB");
        }

        if (missing.Count > 0)
        {
            Console.Error.WriteLine($"No shape for {missing.Count}: {string.Join(", ", missing)}");
            return 1;
        }

        return 0;
    }

    /// <summary>Reads the shapefile into geometry keyed by lowercase ISO alpha-2.</summary>
    private static Dictionary<string, Geometry> ReadShapes(string shapefile)
    {
        var shapes = new Dictionary<string, Geometry>(StringComparer.OrdinalIgnoreCase);

        using var reader = new ShapefileDataReader(shapefile, GeometryFactory.Default);

        var header = reader.DbaseHeader;
        var fields = Enumerable.Range(0, header.NumFields)
            .ToDictionary(i => header.Fields[i].Name, i => i + 1, StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            var code = Code(reader, fields);

            if (code is null)
            {
                continue;
            }

            // Natural Earth carries a few territories under the same code as their parent;
            // the larger geometry is the one the player would recognise.
            if (!shapes.TryGetValue(code, out var existing) || reader.Geometry.Area > existing.Area)
            {
                shapes[code] = reader.Geometry;
            }
        }

        return shapes;
    }

    /// <summary>
    /// Every geometry in the shapefile, coded or not. <see cref="ReadShapes"/> keys by ISO
    /// code and so drops Antarctica, Kosovo and Somaliland, which is right for a quiz pool
    /// and wrong for a map, where they would leave holes in the land.
    /// </summary>
    private static List<Geometry> ReadEveryShape(string shapefile)
    {
        var shapes = new List<Geometry>();

        using var reader = new ShapefileDataReader(shapefile, GeometryFactory.Default);

        while (reader.Read())
        {
            shapes.Add(reader.Geometry);
        }

        return shapes;
    }

    /// <summary>
    /// The ISO alpha-2 code, if the feature has a real one. Natural Earth writes "-99"
    /// where it has no code to give, and keeps a corrected value in ISO_A2_EH for the
    /// handful it gets wrong, so that is tried before giving up on the feature.
    /// </summary>
    private static string? Code(ShapefileDataReader reader, Dictionary<string, int> fields)
    {
        foreach (var field in new[] { "ISO_A2_EH", "ISO_A2", "WB_A2" })
        {
            if (!fields.TryGetValue(field, out var ordinal))
            {
                continue;
            }

            // dBase pads short values with NUL, which Trim leaves in place and a console
            // renders as blanks — so the text has to be cut at the first one.
            var value = reader.GetValue(ordinal)?.ToString()?.Split(char.MinValue)[0].Trim();

            if (!string.IsNullOrEmpty(value) && value != "-99" && value.Length == 2)
            {
                return value.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// One country as rings of x,y pairs on its own unit square, biggest landmass first.
    /// </summary>
    private static double[][] Bake(Geometry geometry)
    {
        var polygons = Flatten(geometry)
            .OrderByDescending(polygon => polygon.Area)
            .ToList();

        if (polygons.Count == 0)
        {
            return [];
        }

        var biggest = polygons[0].Area;
        var candidates = polygons.Where(polygon => polygon.Area >= biggest * IslandFloor).ToList();

        // Grow out from the main landmass, taking islands in size order and refusing any
        // that would stretch the whole outline past the limit.
        var kept = new List<Polygon> { candidates[0] };
        var envelope = candidates[0].EnvelopeInternal.Copy();
        var room = Math.Max(envelope.Width, envelope.Height) * SpreadLimit;

        foreach (var polygon in candidates.Skip(1))
        {
            if (kept.Min(near => near.Distance(polygon)) > GapLimit)
            {
                continue;
            }

            var grown = envelope.Copy();
            grown.ExpandToInclude(polygon.EnvelopeInternal);

            if (Math.Max(grown.Width, grown.Height) > room)
            {
                continue;
            }

            envelope = grown;
            kept.Add(polygon);

            if (kept.Count == MaxRings)
            {
                break;
            }
        }

        // Russia and Fiji straddle the antimeridian, where a naive longitude smears the
        // country across the whole world. Shifting the western lobe past 180 reunites it.
        var wraps = kept.SelectMany(p => p.ExteriorRing.Coordinates).Max(c => c.X) -
                    kept.SelectMany(p => p.ExteriorRing.Coordinates).Min(c => c.X) > 180d;

        var projected = kept
            .Select(polygon => polygon.ExteriorRing.Coordinates.Select(c => Project(c, wraps)).ToArray())
            .ToList();

        // Flat earths look wrong at high latitude, so longitude is squeezed by the
        // country's own latitude: Norway keeps its proportions instead of fanning out.
        var centreLatitude = projected.SelectMany(ring => ring).Average(point => point.Y);
        var squeeze = Math.Cos(centreLatitude * Math.PI / 180d);

        var shaped = projected
            .Select(ring => ring.Select(point => (X: point.X * squeeze, Y: -point.Y)).ToArray())
            .ToList();

        var minX = shaped.SelectMany(r => r).Min(p => p.X);
        var maxX = shaped.SelectMany(r => r).Max(p => p.X);
        var minY = shaped.SelectMany(r => r).Min(p => p.Y);
        var maxY = shaped.SelectMany(r => r).Max(p => p.Y);

        var extent = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-9d);
        var offsetX = (extent - (maxX - minX)) / 2d;
        var offsetY = (extent - (maxY - minY)) / 2d;

        var factory = GeometryFactory.Default;
        var tolerance = extent * SimplifyFraction;
        var rings = new List<double[]>();

        foreach (var ring in shaped)
        {
            var unit = ring
                .Select(p => new Coordinate((p.X - minX + offsetX) / extent, (p.Y - minY + offsetY) / extent))
                .ToArray();

            var simplified = DouglasPeuckerSimplifier
                .Simplify(factory.CreateLineString(unit), tolerance / extent)
                .Coordinates;

            // A ring needs three distinct corners to enclose anything.
            if (simplified.Length < 4)
            {
                continue;
            }

            var flat = new double[simplified.Length * 2];

            for (var i = 0; i < simplified.Length; i++)
            {
                flat[i * 2] = Math.Round(simplified[i].X, Decimals);
                flat[(i * 2) + 1] = Math.Round(simplified[i].Y, Decimals);
            }

            rings.Add(flat);
        }

        return [.. rings];
    }

    private static (double X, double Y) Project(Coordinate coordinate, bool wraps) =>
        (wraps && coordinate.X < 0 ? coordinate.X + 360d : coordinate.X, coordinate.Y);

    /// <summary>
    /// Every country on one equirectangular map, for Find the City to drop pins onto.
    ///
    /// This is the opposite of <see cref="Bake"/> in every choice it makes. Outlines are
    /// scaled onto their own squares, squeezed by latitude and stripped of outlying islands,
    /// because each is shown alone and has to be recognisable. The world map is shown whole,
    /// so every country has to stay where it actually is: one shared projection, no squeeze,
    /// no wrap, and islands kept unless they are too small to see.
    ///
    /// Both axes are divided by 360, which keeps the map's 2:1 proportions in the numbers
    /// themselves — x lands in 0..1, y in 0..0.5 — so nothing downstream has to know the
    /// aspect ratio to draw it undistorted. Inverting it is two multiplications, which
    /// matters because every click the player makes has to be turned back into a position.
    /// </summary>
    private static double[][] BakeWorld(Geometry geometry)
    {
        var factory = GeometryFactory.Default;
        var rings = new List<double[]>();
        var ordered = Flatten(geometry).OrderByDescending(polygon => polygon.Area).ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            var polygon = ordered[index];

            // A country's biggest landmasses are always drawn, however small they are.
            // Tonga and Tuvalu are below any sensible island floor, and a map that leaves
            // them out is one the game can ask unanswerable questions about. Several are
            // kept rather than one because Kiribati's largest island is Kiritimati while
            // its capital sits on Tarawa, three thousand kilometres away.
            if (index >= WorldKeepPerCountry && polygon.Area < WorldIslandFloor)
            {
                continue;
            }

            var unit = polygon.ExteriorRing.Coordinates
                .Select(c => new Coordinate((c.X + 180d) / 360d, (90d - c.Y) / 360d))
                .ToArray();

            // Simplifying a small island at the tolerance a continent wants erases it, so
            // the tolerance is also capped against the island's own size.
            var width = unit.Max(c => c.X) - unit.Min(c => c.X);
            var height = unit.Max(c => c.Y) - unit.Min(c => c.Y);
            var tolerance = Math.Min(WorldTolerance, Math.Max(width, height) / 8d);

            var simplified = DouglasPeuckerSimplifier
                .Simplify(factory.CreateLineString(unit), tolerance)
                .Coordinates;

            if (simplified.Length < 4)
            {
                continue;
            }

            var flat = new double[simplified.Length * 2];

            for (var i = 0; i < simplified.Length; i++)
            {
                flat[i * 2] = Math.Round(simplified[i].X, Decimals);
                flat[(i * 2) + 1] = Math.Round(simplified[i].Y, Decimals);
            }

            rings.Add(flat);
        }

        return [.. rings];
    }

    private static IEnumerable<Polygon> Flatten(Geometry geometry) => geometry switch
    {
        Polygon polygon => [polygon],
        MultiPolygon multi => multi.Geometries.OfType<Polygon>(),
        _ => [],
    };

    /// <summary>
    /// Written by hand rather than through the serialiser: coordinates go out as flat
    /// number arrays, which is roughly half the bytes of a list of pairs.
    /// </summary>
    /// <summary>
    /// The world map: a flat list of rings rather than a map keyed by country, because
    /// nothing asks it which country a ring belongs to. Drawn filled it is the land; drawn
    /// stroked as well, the same rings are the borders.
    /// </summary>
    private static string SerialiseWorld(double[][] rings)
    {
        var builder = new StringBuilder();
        builder.Append("{\"version\":1,\"rings\":[");

        for (var i = 0; i < rings.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append('\n').Append('[');

            for (var j = 0; j < rings[i].Length; j++)
            {
                if (j > 0)
                {
                    builder.Append(',');
                }

                builder.Append(rings[i][j].ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(']');
        }

        builder.Append("\n]}");

        return builder.ToString();
    }

    private static string Serialise(Dictionary<string, double[][]> baked)
    {
        var builder = new StringBuilder();
        builder.Append("{\"version\":1,\"outlines\":{");

        var first = true;

        foreach (var (code, rings) in baked.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;

            builder.Append(JsonSerializer.Serialize(code)).Append(":[");

            for (var r = 0; r < rings.Length; r++)
            {
                if (r > 0)
                {
                    builder.Append(',');
                }

                builder.Append('[');

                for (var i = 0; i < rings[r].Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(rings[r][i].ToString("0.####", CultureInfo.InvariantCulture));
                }

                builder.Append(']');
            }

            builder.Append(']');
        }

        builder.Append("}}");

        return builder.ToString();
    }

    /// <summary>The sovereign pool, read from the app's own country list.</summary>
    private static Dictionary<string, string> LoadPool(string countriesJson)
    {
        using var stream = File.OpenRead(countriesJson);
        using var document = JsonDocument.Parse(stream);

        var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.GetProperty("kind").GetString() is not "sovereign")
            {
                continue;
            }

            pool[entry.GetProperty("code").GetString()!] = entry.GetProperty("name").GetString()!;
        }

        return pool;
    }

    private static string? LocateAssets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Assets");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
