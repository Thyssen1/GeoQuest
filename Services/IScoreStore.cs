using GeoQuest.Models;

namespace GeoQuest.Services;

/// <summary>
/// Persists the player's best score in each mode. Scores are kept apart because the modes
/// do not play by the same terms — a Learning session and a Hard run are not comparable,
/// so one number across all three would mean nothing.
/// </summary>
public interface IScoreStore
{
    /// <summary>Returns the stored best score for a mode, or 0 if none has been recorded.</summary>
    int LoadBestScore(GameMode mode);

    /// <summary>Records a new best score. Failures are swallowed — losing a high score must never break play.</summary>
    void SaveBestScore(GameMode mode, int score);

    /// <summary>Erases every mode's best score.</summary>
    void ClearBestScores();
}
