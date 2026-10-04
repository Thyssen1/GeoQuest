using System.Globalization;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace GeoQuest.Tools.CityBaker;

/// <summary>
/// Dev-time tool. Bakes Natural Earth's populated places into the capital list the Find the
/// City mini-game asks about, so the app ships a few dozen KB of its own data rather than a
/// 48 MB shapefile and a parser.
///
///     dotnet run --project tools/CityBaker -- --input path\to\ne_10m_populated_places.shp
///
/// Coordinates stay as degrees. Country outlines are each scaled onto their own unit square
/// because a country is recognisable alone; a city is not, and only means anything against
/// the whole world, so the map it is dropped onto owns the projection.
/// </summary>
internal static class Program
{
    /// <summary>Degrees are stored to this many decimals, which is about ten metres.</summary>
    private const int Decimals = 4;

    /// <summary>
    /// Natural Earth's capital flag is absent or wrong for four countries, and the game
    /// cannot ask about a capital it has no coordinates for. Each entry is a correction to
    /// the data rather than an opinion about it.
    /// </summary>
    private static readonly Dictionary<string, Override> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kosovo's features carry ISO_A2 "-99", so Pristina never joins to a country. The
        // shapefile does flag it as a capital; only the code is missing.
        ["xk"] = new("Pristina", MatchAdm0: "Kosovo"),

        // South Sudan has been independent since 2011, but Juba is still flagged
        // ADM0CAP = 0. The city is present with the right coordinates; only the flag is wrong.
        ["ss"] = new("Juba", MatchAdm0: "South Sudan"),

        // Natural Earth flags no capital for Palestine. Ramallah is the seat of government,
        // and the choice that keeps the question off Jerusalem.
        ["ps"] = new("Ramallah", MatchAdm0: "Palestine"),

        // Nauru has no populated place in the dataset at all: Yaren is a district rather
        // than a city, so it is the one entry typed in from published coordinates.
        ["nr"] = new("Yaren", Latitude: -0.5477d, Longitude: 166.9209d),

        // Three countries flag more than one capital, and picking the largest gets all
        // three wrong. Each is named here so the choice is a decision rather than a
        // side effect of a population sort.
        //
        // South Africa splits its capitals three ways: Pretoria executive, Cape Town
        // legislative, Bloemfontein judicial. Pretoria is the seat of government.
        ["za"] = new("Pretoria", MatchAdm0: "South Africa"),

        // Yamoussoukro has been the official capital since 1983; Abidjan is larger and
        // holds most of the government, which is exactly why the population sort misfires.
        // Natural Earth files the country under its English name, "Ivory Coast".
        ["ci"] = new("Yamoussoukro", MatchAdm0: "Ivory Coast"),

        // Sucre is the constitutional capital, La Paz the seat of government. La Paz is
        // the one a player is being asked to find.
        ["bo"] = new("La Paz", MatchAdm0: "Bolivia"),

        // Kazakhstan renamed Astana to Nur-Sultan in 2019 and back again in September 2022,
        // a few months after this release of the dataset. Same city, same coordinates.
        ["kz"] = new("Astana", MatchAdm0: "Kazakhstan", FindAs: "Nur-Sultan"),
    };

    private static int Main(string[] args)
    {
        string? input = null;
        string? output = null;

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
                default:
                    Console.Error.WriteLine($"Unrecognised argument: {args[i]}");
                    return 1;
            }
        }

        if (input is null)
        {
            Console.Error.WriteLine("--input is required: the path to ne_10m_populated_places.shp");
            return 1;
        }

        var assets = LocateAssets();
        if (assets is null)
        {
            Console.Error.WriteLine("Could not locate the Assets directory.");
            return 1;
        }

        output ??= Path.Combine(assets, "Cities", "cities.json");

        // The app's own country list is the authority on what belongs in the pool; the
        // shapefile is only asked where each capital sits.
        var pool = LoadPool(Path.Combine(assets, "countries.json"));
        Console.WriteLine($"Pool: {pool.Count} sovereign countries");

        var places = ReadPlaces(input);
        Console.WriteLine($"Shapefile: {places.Count} populated places");

        var capitals = new Dictionary<string, City>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var (code, name) in pool.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var capital = Resolve(code, places);

            if (capital is null)
            {
                missing.Add($"{code} ({name})");
                continue;
            }

            capitals[code] = capital;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, Serialise(capitals));

        Console.WriteLine($"Baked {capitals.Count} capitals -> {output}");
        Console.WriteLine($"{new FileInfo(output).Length / 1024} KB");

        if (missing.Count > 0)
        {
            Console.Error.WriteLine($"No capital for {missing.Count}: {string.Join(", ", missing)}");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// The capital of one country: the flagged place where the data has one, the override
    /// where it does not. Where a country flags several the largest wins, because Bolivia,
    /// South Africa and Cote d'Ivoire each split the seat of government across cities and
    /// the quiz has to settle on the one a player would name.
    /// </summary>
    private static City? Resolve(string code, List<Place> places)
    {
        if (Overrides.TryGetValue(code, out var fix))
        {
            if (fix.Latitude is { } latitude && fix.Longitude is { } longitude)
            {
                return new City(fix.Name, latitude, longitude, Population: 0);
            }

            var lookup = fix.FindAs ?? fix.Name;

            var corrected = places.FirstOrDefault(place =>
                string.Equals(place.Name, lookup, StringComparison.OrdinalIgnoreCase) &&
                (fix.MatchAdm0 is null || string.Equals(place.Adm0Name, fix.MatchAdm0, StringComparison.OrdinalIgnoreCase)));

            // The override names the city; the shapefile only supplies where it is.
            return corrected is null ? null : ToCity(corrected) with { Name = fix.Name };
        }

        var flagged = places
            .Where(place => place.IsCapital && string.Equals(place.Code, code, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(place => place.Population)
            .FirstOrDefault();

        return flagged is null ? null : ToCity(flagged);
    }

    private static City ToCity(Place place) =>
        new(place.Name, place.Latitude, place.Longitude, place.Population);

    private static List<Place> ReadPlaces(string shapefile)
    {
        var places = new List<Place>();

        using var reader = new ShapefileDataReader(shapefile, GeometryFactory.Default);

        var header = reader.DbaseHeader;
        var fields = Enumerable.Range(0, header.NumFields)
            .ToDictionary(i => header.Fields[i].Name, i => i + 1, StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            // NAME is the local-language name — Koebenhavn rather than Copenhagen — so the
            // English one is read first. Natural Earth fills NAME_EN for every capital.
            var name = Text(reader, fields, "NAME_EN") ?? Text(reader, fields, "NAME");

            if (name is null)
            {
                continue;
            }

            // LATITUDE and LONGITUDE are carried as attributes as well as geometry, and the
            // attributes are the ones Natural Earth corrects by hand.
            places.Add(new Place(
                Name: name,
                Code: Text(reader, fields, "ISO_A2") ?? string.Empty,
                Adm0Name: Text(reader, fields, "ADM0NAME") ?? string.Empty,
                Latitude: Number(reader, fields, "LATITUDE") ?? reader.Geometry.Coordinate.Y,
                Longitude: Number(reader, fields, "LONGITUDE") ?? reader.Geometry.Coordinate.X,
                Population: (long)(Number(reader, fields, "POP_MAX") ?? 0d),
                IsCapital: Number(reader, fields, "ADM0CAP") == 1d));
        }

        return places;
    }

    /// <summary>
    /// dBase pads short values with NUL, which Trim leaves in place and a console renders
    /// as blanks, so the text has to be cut at the first one. "-99" is how Natural Earth
    /// writes a value it does not have.
    /// </summary>
    private static string? Text(ShapefileDataReader reader, Dictionary<string, int> fields, string field)
    {
        if (!fields.TryGetValue(field, out var ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal)?.ToString()?.Split(char.MinValue)[0].Trim();

        return string.IsNullOrEmpty(value) || value == "-99" ? null : value;
    }

    private static double? Number(ShapefileDataReader reader, Dictionary<string, int> fields, string field)
    {
        if (!fields.TryGetValue(field, out var ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            double value => value,
            int value => value,
            long value => value,
            decimal value => (double)value,
            string text when double.TryParse(
                text.Split(char.MinValue)[0].Trim(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// Written by hand rather than through the serialiser, matching borders.json. One line
    /// per capital keeps a generated file readable in a diff, which is the only way a bad
    /// bake gets noticed before it ships.
    /// </summary>
    private static string Serialise(Dictionary<string, City> capitals)
    {
        var builder = new StringBuilder();
        builder.Append("{\"version\":1,\"capitals\":{");

        var first = true;

        foreach (var (code, city) in capitals.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;

            builder.Append('\n')
                .Append(JsonSerializer.Serialize(code))
                .Append(":{\"name\":").Append(JsonSerializer.Serialize(city.Name))
                .Append(",\"lat\":").Append(Round(city.Latitude))
                .Append(",\"lon\":").Append(Round(city.Longitude))
                .Append(",\"pop\":").Append(city.Population.ToString(CultureInfo.InvariantCulture))
                .Append('}');
        }

        builder.Append("\n}}");

        return builder.ToString();
    }

    private static string Round(double value) =>
        Math.Round(value, Decimals).ToString(CultureInfo.InvariantCulture);

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

    /// <summary>
    /// A correction to the shapefile: either a place to find in it, or coordinates to use
    /// instead. <see cref="Name"/> is always what gets written out; <see cref="FindAs"/> is
    /// what to look the place up under when the dataset files it under an older name.
    /// </summary>
    private sealed record Override(
        string Name,
        string? MatchAdm0 = null,
        double? Latitude = null,
        double? Longitude = null,
        string? FindAs = null);

    /// <summary>One row of the shapefile, reduced to the fields the game needs.</summary>
    private sealed record Place(
        string Name,
        string Code,
        string Adm0Name,
        double Latitude,
        double Longitude,
        long Population,
        bool IsCapital);

    /// <summary>One capital as it is written out.</summary>
    private sealed record City(string Name, double Latitude, double Longitude, long Population);
}
