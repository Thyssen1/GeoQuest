using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>Persists player-adjustable settings between sessions.</summary>
public interface ISettingsStore
{
    /// <summary>Returns stored settings, or defaults if none exist or the file is unreadable.</summary>
    GameSettings Load();

    /// <summary>Records settings. Failures are swallowed — a failed write must never break play.</summary>
    void Save(GameSettings settings);
}
