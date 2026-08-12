using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Reads the country set from the JSON in Assets. Deliberately takes a <see cref="Stream"/>
/// rather than reaching for Avalonia's asset loader, so the data layer carries no UI
/// dependency and can be exercised from a plain unit test.
/// </summary>
public sealed class JsonCountryRepository : ICountryRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private JsonCountryRepository(IReadOnlyList<Country> all)
    {
        All = all;
        QuestionPool = all.Where(c => c.Kind == CountryKind.Sovereign).ToArray();
    }

    public IReadOnlyList<Country> All { get; }

    public IReadOnlyList<Country> QuestionPool { get; }

    /// <exception cref="InvalidDataException">The JSON is malformed, empty, or has duplicate codes.</exception>
    public static JsonCountryRepository Load(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);

        List<Country>? countries;

        try
        {
            countries = JsonSerializer.Deserialize<List<Country>>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("countries.json could not be parsed.", ex);
        }

        if (countries is null || countries.Count == 0)
        {
            throw new InvalidDataException("countries.json contained no entries.");
        }

        // A duplicate code would let the same country appear twice in one question,
        // which reads to the player as a broken round. Fail loudly at load instead.
        var duplicate = countries
            .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidDataException($"countries.json contains duplicate code '{duplicate.Key}'.");
        }

        return new JsonCountryRepository(countries);
    }
}
