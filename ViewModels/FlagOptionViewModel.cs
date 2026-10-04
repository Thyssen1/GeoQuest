using CommunityToolkit.Mvvm.ComponentModel;
using GeoQuest.Models;

namespace GeoQuest.ViewModels;

public enum OptionState
{
    Idle,
    Correct,
    Wrong,
    Revealed
}

public partial class FlagOptionViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCorrect))]
    [NotifyPropertyChangedFor(nameof(IsWrong))]
    [NotifyPropertyChangedFor(nameof(IsRevealed))]
    private OptionState _state = OptionState.Idle;

    /// <summary>
    /// Dimmed once the round resolves, so the answer is the only tile still lit. Set by
    /// the game rather than derived here: a tile does not know the round is over.
    /// </summary>
    [ObservableProperty]
    private bool _isDimmed;

    /// <summary>Position in the grid, shown as the key that picks it.</summary>
    [ObservableProperty]
    private string _key = string.Empty;

    public FlagOptionViewModel(Country country, object? art)
    {
        Country = country;
        Art = art;
    }

    public Country Country { get; }

    /// <summary>A flag bitmap or a country outline, whichever the mode draws.</summary>
    public object? Art { get; }
    
    public string Code => Country.Code.ToUpperInvariant();
    
    public bool HasArt => Art is not null;
    public bool IsCorrect => State == OptionState.Correct;
    public bool IsWrong => State == OptionState.Wrong;
    public bool IsRevealed => State == OptionState.Revealed;
}
