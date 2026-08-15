using System;
using System.IO;
using System.Text.Json;

namespace GeoQuest.Services;

/// <summary>
/// Stores the best score as a small JSON file under the user's application data
/// directory. <see cref="Environment.SpecialFolder.ApplicationData"/> resolves to a
/// writable per-user location on Windows, macOS and Linux alike, so this carries no
/// desktop-only assumption and will keep working when the project targets mobile.
/// </summary>
public sealed class FileScoreStore : IScoreStore
{
    private const string FolderName = "GeoQuest";
    private const string FileName = "player.json";

    private readonly string _path;

    public FileScoreStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            FolderName,
            FileName);
    }

    public int LoadBestScore()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return 0;
            }

            var record = JsonSerializer.Deserialize<PlayerRecord>(File.ReadAllText(_path));

            // A negative or absent value is treated as "no score yet" rather than trusted.
            return Math.Max(0, record?.BestScore ?? 0);
        }
        catch (Exception)
        {
            // Corrupt or unreadable file: start the player from zero rather than crash.
            return 0;
        }
    }

    public void SaveBestScore(int score)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(new PlayerRecord { BestScore = score }));
        }
        catch (Exception)
        {
            // A read-only or full disk must not interrupt a run in progress.
        }
    }

    private sealed record PlayerRecord
    {
        public int BestScore { get; init; }
    }
}
