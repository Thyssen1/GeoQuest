using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoQuest.Models;

/// <summary>
/// What the player has shown about every flag, held in memory for the length of a run.
/// Every mode records into it, because mastery is a claim about what someone knows rather
/// than about which mode they chose. Learning mode also selects from it.
/// </summary>
public sealed class PlayerHistory
{
    private readonly Dictionary<string, FlagHistory> _flags;

    public PlayerHistory(IReadOnlyDictionary<string, FlagHistory>? flags = null, int poolSize = 0)
    {
        _flags = flags is null
            ? new Dictionary<string, FlagHistory>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, FlagHistory>(flags, StringComparer.OrdinalIgnoreCase);

        PoolSize = poolSize;
    }

    /// <summary>How many flags mastery is measured against: the size of the question pool.</summary>
    public int PoolSize { get; }

    /// <summary>Only flags that have been asked appear here; the rest are in the pool by absence.</summary>
    public IReadOnlyDictionary<string, FlagHistory> Flags => _flags;

    /// <summary>Flags in the mastered box.</summary>
    public int Graduated => _flags.Values.Count(flag => flag.Box >= LearningRules.GraduatedBox);

    /// <summary>Mastered flags as a percentage of the pool. Falls as well as rises.</summary>
    public int MasteryPercent => LearningRules.MasteryPercent(Graduated, PoolSize);

    /// <summary>The history for one flag, or a blank one for a flag never asked.</summary>
    public FlagHistory For(string code) =>
        _flags.TryGetValue(code, out var flag) ? flag : new FlagHistory();

    public int BoxOf(string code) => For(code).Box;

    /// <summary>
    /// Records one answer and moves the flag between boxes. Returns true when that answer
    /// changed how many flags are mastered, which is what the scoreboard reacts to.
    /// </summary>
    public bool Record(string code, bool correct, bool fast)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var current = For(code);
        var next = LearningRules.NextBox(current.Box, correct, fast);

        _flags[code] = current with
        {
            Seen = current.Seen + 1,
            Correct = current.Correct + (correct ? 1 : 0),
            Streak = correct ? current.Streak + 1 : 0,
            Box = next,
            LastSeen = DateTimeOffset.UtcNow,
        };

        var wasMastered = current.Box >= LearningRules.GraduatedBox;
        var isMastered = next >= LearningRules.GraduatedBox;

        return wasMastered != isMastered;
    }
}
