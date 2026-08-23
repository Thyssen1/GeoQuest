namespace GeoQuest.Models;

/// <summary>
/// Everything a mode changes about how a run plays, gathered in one place so the modes
/// differ by values rather than by branches scattered through the rules. Hard mode's
/// "no way to earn a life" is a chance of zero, not a special case.
/// </summary>
public sealed record DifficultyProfile
{
    /// <summary>The mode this profile belongs to. Also the key its best score is stored under.</summary>
    public required GameMode Mode { get; init; }

    /// <summary>Lives a run starts with, or 0 for a mode that cannot be lost.</summary>
    public required int StartingLives { get; init; }

    /// <summary>Chance a correct answer wins a life back. Zero disables the reward entirely.</summary>
    public required double BonusLifeChance { get; init; }

    /// <summary>Flags shown in the opening round, before the ramp adds any.</summary>
    public required int OpeningOptions { get; init; }

    /// <summary>Where the ramp stops. Equal to <see cref="OpeningOptions"/> for a fixed grid.</summary>
    public required int MaxOptions { get; init; }

    /// <summary>Rounds in a session, or 0 for a run that lasts until the lives run out.</summary>
    public required int RoundLimit { get; init; }

    /// <summary>Whether the scoreboard shows how much of the pool is mastered. Hard mode
    /// keeps its board about surviving the run in front of it.</summary>
    public required bool ShowsMastery { get; init; }

    /// <summary>The classic run. Lives come from Options, so this is the settings-dependent one.</summary>
    public static readonly DifficultyProfile Normal = new()
    {
        Mode = GameMode.Normal,
        StartingLives = GameSettings.DefaultStartingLives,
        BonusLifeChance = GameRules.BonusLifeChance,
        OpeningOptions = GameRules.MinOptions,
        MaxOptions = GameRules.MaxOptions,
        RoundLimit = 0,
        ShowsMastery = true,
    };

    /// <summary>
    /// A steady four flags and a clock that never tightens, for a fixed session. Nothing
    /// is at stake: under time pressure people match on colour and never learn the flag.
    /// </summary>
    public static readonly DifficultyProfile Learning = new()
    {
        Mode = GameMode.Learning,
        StartingLives = 0,
        BonusLifeChance = 0d,
        OpeningOptions = 4,
        MaxOptions = 4,
        RoundLimit = 20,
        ShowsMastery = true,
    };

    /// <summary>
    /// Opens a flag wider than Normal and takes away the safety net. The clock and the
    /// ramp are left alone deliberately — difficulty here is meant to come from the
    /// questions, and stacking every lever at once produces a mode nobody can play.
    /// </summary>
    public static readonly DifficultyProfile Hard = new()
    {
        Mode = GameMode.Hard,
        StartingLives = 3,
        BonusLifeChance = 0d,
        OpeningOptions = 4,
        MaxOptions = GameRules.MaxOptions,
        RoundLimit = 0,
        ShowsMastery = false,
    };

    /// <summary>True when running out of lives is what ends the run.</summary>
    public bool HasLives => StartingLives > 0;

    /// <summary>True when the run ends after a set number of rounds instead.</summary>
    public bool IsBounded => RoundLimit > 0;

    /// <summary>
    /// The profile for a mode. Only Normal consults settings — the other two fix their own
    /// terms, which is what makes their scores comparable between runs.
    /// </summary>
    public static DifficultyProfile For(GameMode mode, GameSettings? settings = null) => mode switch
    {
        GameMode.Learning => Learning,
        GameMode.Hard => Hard,
        _ => Normal with { StartingLives = (settings ?? new GameSettings()).Sanitised().StartingLives },
    };
}
