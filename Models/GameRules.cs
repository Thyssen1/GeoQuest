using System;

namespace GeoQuest.Models;

/// <summary>
/// The difficulty curve, kept as pure functions of the player's running correct-answer
/// count so the same input always yields the same round shape. This is what makes the
/// ramp unit-testable without standing up a game session.
/// </summary>
public static class GameRules
{
    /// <summary>Option count for the opening round.</summary>
    public const int MinOptions = 3;

    /// <summary>The grid stops growing here, per the Milestone 1 spec (3 to 4, 5, 6).</summary>
    public const int MaxOptions = 6;
    public const int MaxLives = 5;
    public const double BonusLifeChance = 0.12d;

    /// <summary>Correct answers needed to unlock each successive grid size.</summary>
    private static readonly int[] Thresholds = [0, 3, 7, 12];
    
    private const int BasePoints = 100;


    /// <summary>
    /// The same ramp measured from wherever a mode opens: each threshold adds a flag until
    /// the profile's ceiling. A profile that opens and ends at the same width never grows.
    /// </summary>
    public static int OptionCountFor(int correctAnswers, DifficultyProfile profile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(correctAnswers);
        ArgumentNullException.ThrowIfNull(profile);

        var count = profile.OpeningOptions;

        foreach (var threshold in Thresholds)
        {
            if (correctAnswers >= threshold)
            {
                count = profile.OpeningOptions + Array.IndexOf(Thresholds, threshold);
            }
        }

        return Math.Min(count, profile.MaxOptions);
    }
    
    public static TimeSpan RoundDurationFor(int correctAnswers, DifficultyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var extraOptions = OptionCountFor(correctAnswers, profile) - profile.OpeningOptions;
        var seconds = Math.Max(profile.MinimumSeconds, profile.OpeningSeconds - extraOptions);

        return TimeSpan.FromSeconds(seconds);
    }
    
    public static bool AwardsBonusLife(int lives, double roll, double chance) =>
        lives < MaxLives && roll < chance;
    
    public static int ScoreFor(TimeSpan remaining, TimeSpan allowed, int streak)
    {
        if (allowed <= TimeSpan.Zero)
        {
            return BasePoints;
        }

        var fractionLeft = Math.Clamp(remaining.TotalSeconds / allowed.TotalSeconds, 0d, 1d);
        var speedBonus = BasePoints * 0.5d * fractionLeft;

        // Capped so a long streak stays an incentive rather than a runaway multiplier.
        var streakMultiplier = 1d + Math.Min(streak - 1, 10) * 0.1d;

        return (int)Math.Round((BasePoints + speedBonus) * streakMultiplier);
    }
}
