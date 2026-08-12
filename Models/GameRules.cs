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

    /// <summary>Correct answers needed to unlock each successive grid size.</summary>
    private static readonly int[] Thresholds = [0, 3, 7, 12];

    /// <summary>Seconds allowed for the opening round.</summary>
    private const double MaxRoundSeconds = 12d;

    /// <summary>The round timer never drops below this, however deep the run goes.</summary>
    private const double MinRoundSeconds = 7d;

    /// <summary>Base points for a correct answer, before time and streak bonuses.</summary>
    private const int BasePoints = 100;

    /// <summary>
    /// How many flags to show given how many the player has answered correctly so far.
    /// Grows 3 -> 4 -> 5 -> 6 at 0, 3, 7 and 12 correct answers, then holds at 6.
    /// </summary>
    public static int OptionCountFor(int correctAnswers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(correctAnswers);

        var count = MinOptions;

        foreach (var threshold in Thresholds)
        {
            if (correctAnswers >= threshold)
            {
                count = MinOptions + Array.IndexOf(Thresholds, threshold);
            }
        }

        return Math.Min(count, MaxOptions);
    }

    /// <summary>
    /// Time allowed for a round. Shrinks by a second per grid size so later rounds
    /// squeeze on both axes — more options to scan, less time to scan them.
    /// </summary>
    public static TimeSpan RoundDurationFor(int correctAnswers)
    {
        var extraOptions = OptionCountFor(correctAnswers) - MinOptions;
        var seconds = Math.Max(MinRoundSeconds, MaxRoundSeconds - extraOptions);

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Score for a correct answer. Rewards answering quickly and rewards streaks, so a
    /// long clean run is worth substantially more than the same count of scattered hits.
    /// </summary>
    /// <param name="remaining">Time left on the clock when the player answered.</param>
    /// <param name="allowed">Total time the round allowed.</param>
    /// <param name="streak">Consecutive correct answers, including this one.</param>
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
