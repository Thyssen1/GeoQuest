using GeoQuest.Services;

namespace GeoQuest.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// Parameterless so the XAML previewer can construct it. Both this and the app's
    /// real startup path go through <see cref="AssetCountryData"/>, so the previewer
    /// renders against the actual dataset rather than stand-in data.
    /// </summary>
    public MainWindowViewModel()
        : this(new GameViewModel(AssetCountryData.Instance, new FlagImageLoader()))
    {
    }

    public MainWindowViewModel(GameViewModel game)
    {
        Game = game;
    }

    public GameViewModel Game { get; }
}
