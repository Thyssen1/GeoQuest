using System;
using Avalonia.Platform;

namespace GeoQuest.Services;

/// <summary>
/// Composition helper that loads the country set from embedded assets once per process.
/// Shared by the running app and the XAML previewer, so both see the real dataset.
/// </summary>
public static class AssetCountryData
{
    private const string CountriesUri = "avares://GeoQuest/Assets/countries.json";

    private static ICountryRepository? _instance;

    public static ICountryRepository Instance => _instance ??= Load();

    private static ICountryRepository Load()
    {
        using var stream = AssetLoader.Open(new Uri(CountriesUri));
        return JsonCountryRepository.Load(stream);
    }
}
