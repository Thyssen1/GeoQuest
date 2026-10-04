using System;
using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using GeoQuest.Models;

namespace GeoQuest.ViewModels;

/// <summary>
/// The first screen behind Play: which game to play, before the terms it is played under.
/// Mini-game and mode are independent, so they are chosen one after the other rather than
/// multiplied into one long list.
/// </summary>
public partial class GameSelectViewModel : ViewModelBase
{
    public GameSelectViewModel(Func<MiniGame, int> mastery, MiniGame lastPlayed = MiniGame.Flags)
    {
        ArgumentNullException.ThrowIfNull(mastery);

        Games =
        [
            Card("1", MiniGame.Flags,
                "Guess the Flag",
                "A country is named and you pick its flag — or the other way round.",
                "#EAF6FC", "#3FA9F5"),
            Card("2", MiniGame.Borders,
                "Guess the Border",
                "The same game played on country outlines, with nothing but the shape to go on.",
                "#EAFBF0", "#2E9E57"),
            Card("3", MiniGame.Cities,
                "Find the City",
                "A capital is named and you drop a pin on the world — scored by how close you land.",
                "#FFF3E2", "#E08A1E"),
        ];

        GameCard Card(string key, MiniGame game, string name, string summary, string tint, string ink) => new()
        {
            Key = key,
            Game = game,
            Name = name,
            Summary = summary,
            KeyBackground = SolidColorBrush.Parse(tint),
            KeyForeground = SolidColorBrush.Parse(ink),
            MasteryPercent = mastery(game),
            IsLastPlayed = game == lastPlayed,
        };
    }

    public event EventHandler<MiniGame>? GameChosen;

    public event EventHandler? BackRequested;

    public IReadOnlyList<GameCard> Games { get; }

    /// <summary>Derived from the cards, so adding a game cannot leave the hint behind.</summary>
    public string KeysHint => $"Press 1–{Games.Count}, or Enter for the one you played last";

    /// <summary>Keyboard entry point, mirroring how a round takes its number keys.</summary>
    public void ChooseByNumber(string? number)
    {
        if (!int.TryParse(number, out var position))
        {
            return;
        }

        var index = position - 1;

        if (index >= 0 && index < Games.Count)
        {
            GameChosen?.Invoke(this, Games[index].Game);
        }
    }

    [RelayCommand]
    private void Choose(GameCard? card)
    {
        if (card is not null)
        {
            GameChosen?.Invoke(this, card.Game);
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}

/// <summary>One mini-game as the chooser presents it.</summary>
public sealed record GameCard
{
    public required string Key { get; init; }

    public required MiniGame Game { get; init; }

    public required string Name { get; init; }

    public required string Summary { get; init; }

    public required IBrush KeyBackground { get; init; }

    public required IBrush KeyForeground { get; init; }

    /// <summary>How much of this game's pool has been mastered. Each game counts its own.</summary>
    public required int MasteryPercent { get; init; }

    public required bool IsLastPlayed { get; init; }
}
