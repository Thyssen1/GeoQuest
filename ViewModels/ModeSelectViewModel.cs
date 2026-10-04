using System;
using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.ViewModels;

/// <summary>
/// The second screen behind Play: the terms a run is played under, for the game already
/// chosen. Every mode exists for every game, so the copy names what the game draws rather
/// than assuming flags.
/// </summary>
public partial class ModeSelectViewModel : ViewModelBase
{
    public ModeSelectViewModel(
        FileScoreStore scores,
        MiniGame game,
        GameMode lastPlayed = GameMode.Normal,
        PlayerHistory? history = null)
    {
        ArgumentNullException.ThrowIfNull(scores);

        var progress = history ?? new PlayerHistory();

        Game = game;
        Title = game == MiniGame.Borders ? "Guess the Border" : "Guess the Flag";

        // The one word that makes every mode's description true for either game.
        var subject = game == MiniGame.Borders ? "outline" : "flag";

        Modes =
        [
            Card("1", GameMode.Normal,
                "Normal",
                $"The classic run. Three {subject}s to start, growing to six, and the clock tightens as you go.",
                "Lives from Options  ·  Extra lives possible",
                "#EAF6FC", "#3FA9F5"),
            Card("2", GameMode.Learning,
                "Learning",
                $"Twenty rounds at a steady four {subject}s on a clock that never tightens, drawn from the ones you are actually learning.",
                "20 rounds  ·  No lives",
                "#EAFBF0", "#2E9E57"),
            Card("3", GameMode.Hard,
                "Hard",
                $"Opens at four {subject}s and climbs to six. Three lives, and no way to earn any back.",
                "3 lives  ·  No extra lives",
                "#FFECEA", "#D9453B"),
            Card("4", GameMode.Recall,
                "Recall",
                $"The question the other way round: one {subject} is shown and you name the country, from all 197.",
                "3 lives  ·  18 seconds a round  ·  Type to search",
                "#F1EBFD", "#7A4FD0"),
        ];

        ModeCard Card(string key, GameMode mode, string name, string summary, string terms, string tint, string ink) => new()
        {
            Key = key,
            KeyBackground = SolidColorBrush.Parse(tint),
            KeyForeground = SolidColorBrush.Parse(ink),
            Mode = mode,
            Name = name,
            Summary = summary,
            Terms = terms,
            BestScore = scores.LoadBestScore(game, mode),
            IsLastPlayed = mode == lastPlayed,
            ShowsProgress = DifficultyProfile.For(game, mode).ShowsMastery,
            Progress = $"{progress.Graduated} / {progress.PoolSize}",
            MasteryPercent = progress.MasteryPercent,
        };
    }

    public event EventHandler<GameMode>? ModeChosen;

    public event EventHandler? BackRequested;

    public MiniGame Game { get; }

    public string Title { get; }

    public IReadOnlyList<ModeCard> Modes { get; }

    /// <summary>
    /// Keyboard entry point, mirroring how a round takes 1-6. Called by the shell, which
    /// owns the number keys so they work regardless of what has focus.
    /// </summary>
    public void ChooseByNumber(string? number)
    {
        if (!int.TryParse(number, out var position))
        {
            return;
        }

        var index = position - 1;

        if (index >= 0 && index < Modes.Count)
        {
            ModeChosen?.Invoke(this, Modes[index].Mode);
        }
    }

    [RelayCommand]
    private void Choose(ModeCard? card)
    {
        if (card is not null)
        {
            ModeChosen?.Invoke(this, card.Mode);
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// One mode as the chooser presents it. Built when the screen opens and never changes,
/// so it needs no change notification of its own.
/// </summary>
public sealed record ModeCard
{
    /// <summary>The number key that picks this mode.</summary>
    public required string Key { get; init; }

    /// <summary>The mode's own tint, carried on its key chip so the four read apart at a glance.</summary>
    public required IBrush KeyBackground { get; init; }

    public required IBrush KeyForeground { get; init; }

    public required GameMode Mode { get; init; }

    public required string Name { get; init; }

    public required string Summary { get; init; }

    /// <summary>The rules that make this mode what it is, stated before the run starts.</summary>
    public required string Terms { get; init; }

    public required int BestScore { get; init; }

    public required bool IsLastPlayed { get; init; }

    /// <summary>Hidden for Hard, which is measured by its best score rather than by progress.</summary>
    public required bool ShowsProgress { get; init; }

    /// <summary>How much of this game's pool has been mastered.</summary>
    public required string Progress { get; init; }

    public required int MasteryPercent { get; init; }

    public bool HasBestScore => BestScore > 0;
}
