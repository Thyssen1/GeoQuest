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
    
    /// <summary>
    /// How far a pin may land from the city and still count, at this point in the run. A
    /// pin round has no options to add, so it gets harder the only way it can: the target
    /// shrinks on the same thresholds that widen the grid in the other games, which keeps
    /// one difficulty curve across all three rather than two that have to be tuned apart.
    /// </summary>
    public static double ToleranceFor(int correctAnswers, DifficultyProfile profile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(correctAnswers);
        ArgumentNullException.ThrowIfNull(profile);

        var steps = Thresholds.Length - 1;

        if (steps <= 0 || profile.OpeningToleranceKm <= profile.MinimumToleranceKm)
        {
            return profile.MinimumToleranceKm;
        }

        var reached = 0;

        foreach (var threshold in Thresholds)
        {
            if (correctAnswers >= threshold)
            {
                reached = Array.IndexOf(Thresholds, threshold);
            }
        }

        var span = profile.OpeningToleranceKm - profile.MinimumToleranceKm;

        return profile.OpeningToleranceKm - (span * reached / steps);
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

    /// <summary>A pin this close to the city is treated as dead on, and scores in full.</summary>
    public const double PerfectPinKm = 50d;

    /// <summary>The least a pin inside the tolerance can be worth, as a share of full marks.</summary>
    private const double PinFloor = 0.25d;

    /// <summary>
    /// How good a pin was, from 1 for a direct hit down to 0 at the edge of what the mode
    /// accepts. The flat band inside <see cref="PerfectPinKm"/> exists because the map is
    /// about a thousand pixels wide, where fifty kilometres is less than a pixel: without
    /// it the score would turn on which pixel a click landed in rather than on knowledge.
    /// </summary>
    public static double PinAccuracy(double distanceKm, double toleranceKm)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(distanceKm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toleranceKm);

        if (distanceKm <= PerfectPinKm)
        {
            return 1d;
        }

        if (distanceKm >= toleranceKm)
        {
            return 0d;
        }

        return 1d - ((distanceKm - PerfectPinKm) / (toleranceKm - PerfectPinKm));
    }

    /// <summary>
    /// What a pin is worth: the same speed and streak scoring the other games use, scaled
    /// by how close it landed. A pin that only just counts still earns a quarter, because
    /// knowing roughly where a city is deserves more than nothing.
    /// </summary>
    public static int ScoreForPin(
        double distanceKm,
        double toleranceKm,
        TimeSpan remaining,
        TimeSpan allowed,
        int streak)
    {
        var accuracy = PinAccuracy(distanceKm, toleranceKm);

        if (accuracy <= 0d)
        {
            return 0;
        }

        var scaled = PinFloor + ((1d - PinFloor) * accuracy);

        return (int)Math.Round(ScoreFor(remaining, allowed, streak) * scaled);
    }
    
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
