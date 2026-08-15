using Avalonia.Media.Imaging;
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

    public FlagOptionViewModel(Country country, Bitmap? image)
    {
        Country = country;
        Image = image;
    }

    public Country Country { get; }

    public Bitmap? Image { get; }
    
    public string Code => Country.Code.ToUpperInvariant();
    
    public bool HasImage => Image is not null;
    public bool IsCorrect => State == OptionState.Correct;
    public bool IsWrong => State == OptionState.Wrong;
    public bool IsRevealed => State == OptionState.Revealed;
}
