namespace GeoQuest.Services;

/// <summary>The short effects a round can trigger.</summary>
public enum GameSound
{
    /// <summary>A right answer.</summary>
    Correct,

    /// <summary>A right answer that also won a life.</summary>
    BonusLife,

    /// <summary>A wrong answer, or a round that ran out of time.</summary>
    Wrong,
}

/// <summary>Plays the game's sound effects.</summary>
public interface ISoundPlayer
{
    /// <summary>
    /// Starts a sound and returns immediately. Implementations must never throw and never
    /// block the caller: a missing audio device is not a reason to interrupt a round.
    /// </summary>
    void Play(GameSound sound);
}
