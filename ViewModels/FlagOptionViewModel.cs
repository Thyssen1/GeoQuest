using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GeoQuest.Models;

namespace GeoQuest.ViewModels;

/// <summary>How an option should present itself once the round is revealed.</summary>
public enum OptionState
{
    /// <summary>Awaiting the player's pick.</summary>
    Idle,

    /// <summary>The player picked this and it was right.</summary>
    Correct,

    /// <summary>The player picked this and it was wrong.</summary>
    Wrong,

    /// <summary>The right answer, surfaced after a wrong pick or a timeout.</summary>
    Revealed,
}

/// <summary>One selectable flag in the grid.</summary>
public partial class FlagOptionViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCorrect))]
    [NotifyPropertyChangedFor(nameof(IsWrong))]
    [NotifyPropertyChangedFor(nameof(IsRevealed))]
    private OptionState _state = OptionState.Idle;

    public FlagOptionViewModel(Country country, Bitmap? image)
    {
        Country = country;
        Image = image;
    }

    public Country Country { get; }

    public Bitmap? Image { get; }

    /// <summary>Shown as a fallback when the flag asset is missing.</summary>
    public string Code => Country.Code.ToUpperInvariant();

    /// <summary>False only when the flag asset failed to load, which is when the code fallback shows.</summary>
    public bool HasImage => Image is not null;

    public bool IsCorrect => State == OptionState.Correct;

    public bool IsWrong => State == OptionState.Wrong;

    public bool IsRevealed => State == OptionState.Revealed;
}
