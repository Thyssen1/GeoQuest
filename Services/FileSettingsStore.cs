using System;
using System.IO;
using System.Text.Json;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Stores settings as JSON under the user's application data directory, alongside
/// the score file. See <see cref="FileScoreStore"/> for why that location is used.
/// </summary>
public sealed class FileSettingsStore : ISettingsStore
{
    private const string FolderName = "GeoQuest";
    private const string FileName = "settings.json";

    private readonly string _path;

    public FileSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            FolderName,
            FileName);
    }

    public GameSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new GameSettings();
            }

            var settings = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(_path));

            // Values from disk are never trusted; a hand-edited file must not break the game.
            return (settings ?? new GameSettings()).Sanitised();
        }
        catch (Exception)
        {
            return new GameSettings();
        }
    }

    public void Save(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(settings.Sanitised()));
        }
        catch (Exception)
        {
            // A read-only or full disk must not interrupt play.
        }
    }
}
