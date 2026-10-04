using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Stores the best score per mini-game and mode as a small JSON file under the user's
/// application data directory. <see cref="Environment.SpecialFolder.ApplicationData"/>
/// resolves to a writable per-user location on Windows, macOS and Linux alike, so this
/// carries no desktop-only assumption and will keep working when the project targets mobile.
/// </summary>
public sealed class FileScoreStore
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

    private static string Key(MiniGame game, GameMode mode) => $"{game}.{mode}";

    public int LoadBestScore(MiniGame game, GameMode mode) => Read().GetValueOrDefault(Key(game, mode));

    public void SaveBestScore(MiniGame game, GameMode mode, int score)
    {
        var scores = Read();
        scores[Key(game, mode)] = Math.Max(0, score);

        Write(scores);
    }

    public void ClearBestScores() => Write(new Dictionary<string, int>());

    /// <summary>
    /// Reads the stored scores, migrating older shapes as it goes. A file from before
    /// mini-games existed keys scores by mode alone, and one from before modes existed
    /// holds a single number; both were earned playing the flags.
    /// </summary>
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
                // A negative or absurd value is treated as "no score yet" rather than trusted.
                var value = Math.Max(0, score);

                if (TryParseKey(name, out var key))
                {
                    scores[key] = value;
                }
            }

            var normal = Key(MiniGame.Flags, GameMode.Normal);

            if (!scores.ContainsKey(normal) && record.BestScore is int legacy)
            {
                scores[normal] = Math.Max(0, legacy);
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

    /// <summary>Accepts "Flags.Normal" and the older bare "Normal", which meant the flags.</summary>
    private static bool TryParseKey(string name, out string key)
    {
        key = string.Empty;

        var dot = name.IndexOf('.');

        if (dot < 0)
        {
            if (!Enum.TryParse<GameMode>(name, out var only))
            {
                return false;
            }

            key = Key(MiniGame.Flags, only);
            return true;
        }

        if (!Enum.TryParse<MiniGame>(name[..dot], out var game) ||
            !Enum.TryParse<GameMode>(name[(dot + 1)..], out var mode))
        {
            return false;
        }

        key = Key(game, mode);
        return true;
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
        /// <summary>
        /// The single score written before modes existed. Read so an upgrade keeps it,
        /// never written again — the migration happens the first time a score is saved.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? BestScore { get; init; }

        [JsonPropertyName("bestScores")]
        public Dictionary<string, int>? BestScores { get; init; }
    }
}
