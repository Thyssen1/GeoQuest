using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>Persists what the player has shown about each flag, between sessions.</summary>
public interface IHistoryStore
{
    /// <summary>Returns the stored history, or an empty one if none exists or it cannot be read.</summary>
    /// <param name="poolSize">How many flags mastery is measured against.</param>
    PlayerHistory Load(int poolSize);

    /// <summary>Records the history. Failures are swallowed — a failed write must never break play.</summary>
    void Save(PlayerHistory history);
}
