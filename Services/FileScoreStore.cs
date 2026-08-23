using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Stores the best score per mode as a small JSON file under the user's application data
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

    public int LoadBestScore(GameMode mode) => Read().GetValueOrDefault(mode.ToString());

    public void SaveBestScore(GameMode mode, int score)
    {
        var scores = Read();
        scores[mode.ToString()] = Math.Max(0, score);

        Write(scores);
    }

    public void ClearBestScores() => Write(new Dictionary<string, int>());
    
    private Dictionary<string, int> Read()
    {
        var scores = new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            if (!File.Exists(_path))
            {
                return scores;
            }

            var record = JsonSerializer.Deserialize<PlayerRecord>(File.ReadAllText(_path));

            if (record is null)
            {
                return scores;
            }

            foreach (var (name, score) in record.BestScores ?? new Dictionary<string, int>())
            {
                if (Enum.TryParse<GameMode>(name, out var mode))
                {
                    scores[mode.ToString()] = Math.Max(0, score);
                }
            }

            if (!scores.ContainsKey(nameof(GameMode.Normal)) && record.BestScore is int legacy)
            {
                scores[nameof(GameMode.Normal)] = Math.Max(0, legacy);
            }

            return scores;
        }
        catch (Exception)
        {
            // Corrupt or unreadable file: start the player from zero rather than crash.
            scores.Clear();
            return scores;
        }
    }

    private void Write(Dictionary<string, int> scores)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(new PlayerRecord { BestScores = scores }));
        }
        catch (Exception)
        {
            // A read-only or full disk must not interrupt a run in progress.
        }
    }

    private sealed record PlayerRecord
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? BestScore { get; init; }

        [JsonPropertyName("bestScores")]
        public Dictionary<string, int>? BestScores { get; init; }
    }
}
