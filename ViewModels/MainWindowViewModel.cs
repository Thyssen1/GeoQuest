using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoQuest.Services;

namespace GeoQuest.ViewModels;

/// <summary>
/// Navigation shell. Owns which page is on screen and the lifetime of each one;
/// the pages themselves only announce intent, so navigation rules live in one place.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly Func<GameViewModel> _newGame;
    private readonly ISettingsStore _settings;
    private readonly IScoreStore _scores;

    private MenuViewModel? _menu;
    private GameViewModel? _game;

    [ObservableProperty]
    private ViewModelBase _currentPage = null!;

    public MainWindowViewModel()
        : this(new FileSettingsStore(), new FileScoreStore())
    {
    }

    public MainWindowViewModel(ISettingsStore settings, IScoreStore scores)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scores);

        _settings = settings;
        _scores = scores;

        // A run is built fresh each time Play is pressed, picking up any settings
        // changed in between and leaving no timers running behind the menu.
        _newGame = () => new GameViewModel(
            AssetCountryData.Instance,
            new FlagImageLoader(),
            scores,
            settings.Load());

        ShowMenu();
    }

    /// <summary>Raised when the player chooses Exit; the window closes in response.</summary>
    public event EventHandler? ExitRequested;

    public void ShowMenu()
    {
        DisposeGame();

        if (_menu is null)
        {
            _menu = new MenuViewModel(_scores);
            _menu.PlayRequested += (_, _) => ShowGame();
            _menu.OptionsRequested += (_, _) => ShowOptions();
            _menu.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // A run may have set a new best score, or Options may have cleared it.
            _menu.Refresh();
        }

        CurrentPage = _menu;
    }

    private void ShowGame()
    {
        DisposeGame();

        _game = _newGame();
        _game.MenuRequested += (_, _) => ShowMenu();

        CurrentPage = _game;
    }

    private void ShowOptions()
    {
        var options = new OptionsViewModel(_settings, _scores);
        options.BackRequested += (_, _) => ShowMenu();

        CurrentPage = options;
    }

    /// <summary>
    /// Number keys are bound at the window so they work regardless of focus, which
    /// means they arrive here rather than at the game. Forwarded only while playing.
    /// </summary>
    [RelayCommand]
    private void SelectByNumber(string? number)
    {
        if (CurrentPage is GameViewModel game)
        {
            game.SelectByNumberCommand.Execute(number);
        }
    }

    /// <summary>Escape backs out of a run or of Options, and does nothing on the menu.</summary>
    [RelayCommand]
    private void Back()
    {
        if (CurrentPage is GameViewModel or OptionsViewModel)
        {
            ShowMenu();
        }
    }

    private void DisposeGame()
    {
        _game?.Dispose();
        _game = null;
    }

    public void Dispose() => DisposeGame();
}
