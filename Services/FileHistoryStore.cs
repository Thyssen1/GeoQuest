using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Stores learning progress as JSON under the user's application data directory, in its
/// own file for the same reason settings are: a corrupt one must never cost the player
/// something else. Each mini-game has its own section, because knowing a country's flag
/// says nothing about whether you would recognise its outline.
/// </summary>
public sealed class FileHistoryStore
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

    private static string Section(MiniGame game) => game.ToString().ToLowerInvariant();

    public PlayerHistory Load(MiniGame game, int poolSize)
    {
        var sections = Read();

        return sections.TryGetValue(Section(game), out var flags)
            ? new PlayerHistory(flags, poolSize)
            : new PlayerHistory(poolSize: poolSize);
    }

    public void Save(MiniGame game, PlayerHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        try
        {
            // Read first: the other games' progress lives in the same file and must survive.
            var sections = Read();
            sections[Section(game)] = new Dictionary<string, FlagHistory>(history.Flags, StringComparer.OrdinalIgnoreCase);

            var directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(sections));
        }
        catch (Exception)
        {
            // A read-only or full disk must not interrupt a run in progress.
        }
    }

    private Dictionary<string, Dictionary<string, FlagHistory>> Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new Dictionary<string, Dictionary<string, FlagHistory>>(StringComparer.OrdinalIgnoreCase);
            }

            var sections = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, FlagHistory>>>(
                File.ReadAllText(_path));

            return sections is null
                ? new Dictionary<string, Dictionary<string, FlagHistory>>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Dictionary<string, FlagHistory>>(sections, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Corrupt or unreadable: start over rather than crash. Losing progress is bad,
            // but refusing to launch is worse, and the file is rewritten on the next run.
            return new Dictionary<string, Dictionary<string, FlagHistory>>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
