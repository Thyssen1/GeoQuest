using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Stores learning history as JSON under the user's application data directory, in its own
/// file for the same reason settings are: a corrupt one must never cost the player
/// something else. Entries are nested under a game key because knowing a country's flag is
/// not knowing its outline, and later mini-games will need their own progress.
/// </summary>
public sealed class FileHistoryStore : IHistoryStore
{
    private const string FolderName = "GeoQuest";
    private const string FileName = "history.json";

    private readonly string _path;

    public FileHistoryStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            FolderName,
            FileName);
    }

    public PlayerHistory Load(int poolSize)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new PlayerHistory(poolSize: poolSize);
            }

            var record = JsonSerializer.Deserialize<HistoryRecord>(File.ReadAllText(_path));

            return new PlayerHistory(record?.Flags, poolSize);
        }
        catch (Exception)
        {
            // Corrupt or unreadable: start over rather than crash. Losing progress is bad,
            // but refusing to launch is worse, and the file is rewritten on the next run.
            return new PlayerHistory(poolSize: poolSize);
        }
    }

    public void Save(PlayerHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var record = new HistoryRecord
            {
                Flags = new Dictionary<string, FlagHistory>(history.Flags, StringComparer.OrdinalIgnoreCase),
            };

            File.WriteAllText(_path, JsonSerializer.Serialize(record));
        }
        catch (Exception)
        {
            // A read-only or full disk must not interrupt a run in progress.
        }
    }

    private sealed record HistoryRecord
    {
        [JsonPropertyName("flags")]
        public Dictionary<string, FlagHistory>? Flags { get; init; }
    }
}
