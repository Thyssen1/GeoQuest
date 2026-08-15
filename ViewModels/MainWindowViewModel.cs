using GeoQuest.Services;

namespace GeoQuest.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
        : this(new GameViewModel(AssetCountryData.Instance, new FlagImageLoader(), new FileScoreStore()))
    {
    }

    public MainWindowViewModel(GameViewModel game)
    {
        Game = game;
    }

    public GameViewModel Game { get; }
}
