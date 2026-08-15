namespace GeoQuest.Services;

/// <summary>Persists the player's best score between sessions.</summary>
public interface IScoreStore
{
    /// <summary>Returns the stored best score, or 0 if none has been recorded.</summary>
    int LoadBestScore();

    /// <summary>Records a new best score. Failures are swallowed — losing a high score must never break play.</summary>
    void SaveBestScore(int score);
}