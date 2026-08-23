using System;

namespace GeoQuest.Models;

/// <summary>
/// The Leitner schedule, kept as pure functions of a flag's state and one answer so the
/// whole progression can be tested without playing a round.
/// </summary>
public static class LearningRules
{
    /// <summary>Flags that have never been asked.</summary>
    public const int Pool = 0;

    /// <summary>Where a flag lands the first time it is asked.</summary>
    public const int FirstBox = 1;

    /// <summary>Learned. Out of rotation apart from the occasional re-test.</summary>
    public const int GraduatedBox = 4;

    /// <summary>How many flags Learning mode works on at a time, so a session stays finite.</summary>
    public const int FocusLimit = 20;

    /// <summary>
    /// A missed re-test drops this far rather than one box. Box 3 is a single good answer
    /// from graduating again, which is too cheap for something just shown to be forgotten.
    /// </summary>
    private const int RelapseBox = 2;

    /// <summary>
    /// Share of the clock that must remain for an answer to count as recognition rather
    /// than deliberation. At three or four options a slow correct answer is often a guess,
    /// and promoting on it would fill the mastered box with flags nobody knows.
    /// </summary>
    public const double FastFraction = 0.5d;

    /// <summary>Whether an answer came quickly enough to count as knowing it.</summary>
    public static bool IsFast(TimeSpan remaining, TimeSpan allowed) =>
        allowed > TimeSpan.Zero && remaining.TotalSeconds / allowed.TotalSeconds >= FastFraction;

    /// <summary>
    /// Where a flag sits after one answer: a quick correct answer promotes it, a slow one
    /// holds ground, and a miss costs a box. A flag can never fall out of the learning set
    /// once asked, so nothing is ever forgotten back into the pool.
    /// </summary>
    public static int NextBox(int box, bool correct, bool fast)
    {
        // An unseen flag is treated as if it were in the first box, so someone who already
        // knows it can carry it up from the very first round rather than spending a turn
        // being introduced to something they could name on sight.
        var current = Math.Clamp(box, FirstBox, GraduatedBox);

        if (!correct)
        {
            return current == GraduatedBox ? RelapseBox : Math.Max(FirstBox, current - 1);
        }

        return fast ? Math.Min(GraduatedBox, current + 1) : current;
    }

    /// <summary>Mastered flags as a whole-number percentage of the question pool.</summary>
    public static int MasteryPercent(int graduated, int total) =>
        total <= 0 ? 0 : (int)Math.Round(100d * Math.Clamp(graduated, 0, total) / total);
}
