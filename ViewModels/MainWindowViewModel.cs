using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.ViewModels;

/// <summary>
/// Navigation shell. Owns which page is on screen and the lifetime of each one;
/// the pages themselves only announce intent, so navigation rules live in one place.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly IScoreStore _scores;
    private readonly IHistoryStore _historyStore;

    /// <summary>Shared across runs: it caches the unpacked WAVs, so one instance keeps that work done once.</summary>
    private readonly ISoundPlayer _sounds = new SystemSoundPlayer();

    private MenuViewModel? _menu;
    private GameViewModel? _game;

    /// <summary>Loaded on first use so the previewer does not read the player's files just to draw.</summary>
    private PlayerHistory? _history;

    [ObservableProperty]
    private ViewModelBase _currentPage = null!;

    public MainWindowViewModel()
        : this(new FileSettingsStore(), new FileScoreStore(), new FileHistoryStore())
    {
    }

    public MainWindowViewModel(ISettingsStore settings, IScoreStore scores, IHistoryStore? history = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scores);

        _settings = settings;
        _scores = scores;
        _historyStore = history ?? new FileHistoryStore();

        ShowMenu();
    }

    /// <summary>
    /// Everything the player has shown about each flag, shared by the chooser, which reports
    /// progress, and the run, which adds to it.
    /// </summary>
    private PlayerHistory History =>
        _history ??= _historyStore.Load(AssetCountryData.Instance.QuestionPool.Count);

    /// <summary>Raised when the player chooses Exit; the window closes in response.</summary>
    public event EventHandler? ExitRequested;

    public void ShowMenu()
    {
        DisposeGame();

        if (_menu is null)
        {
            _menu = new MenuViewModel(_scores, _settings, History);
            _menu.PlayRequested += (_, _) => ShowModeSelect();
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

    private void ShowModeSelect()
    {
        DisposeGame();

        var chooser = new ModeSelectViewModel(_scores, _settings.Load().Sanitised().Mode, History);

        chooser.ModeChosen += (_, mode) => StartRun(mode);
        chooser.BackRequested += (_, _) => ShowMenu();

        CurrentPage = chooser;
    }

    /// <summary>
    /// A run is built fresh for the chosen mode, picking up any settings changed in
    /// between and leaving no timers running behind the menu.
    /// </summary>
    private void StartRun(GameMode mode)
    {
        DisposeGame();

        var settings = _settings.Load().Sanitised();

        // Remembered so the chooser offers this mode first next time.
        if (settings.Mode != mode)
        {
            _settings.Save(settings with { Mode = mode });
        }

        // Learning draws from the flags being learned; the other modes draw from the world.
        IQuestionGenerator generator = mode == GameMode.Learning
            ? new LearningQuestionGenerator(AssetCountryData.Instance, History)
            : new RandomQuestionGenerator(AssetCountryData.Instance);

        _game = new GameViewModel(
            generator,
            new FlagImageLoader(),
            _scores,
            DifficultyProfile.For(mode, settings),
            // Muting a run is giving it no sound player at all.
            settings.SoundEnabled ? _sounds : null,
            random: null,
            history: History,
            // Only Recall needs the full name list; the other modes never show it.
            choices: mode == GameMode.Recall ? AssetCountryData.Instance.QuestionPool : null);

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
    /// Number keys are bound at the window so they work regardless of focus, which means
    /// they arrive here rather than at the page. Forwarded to whichever page wants them.
    /// </summary>
    [RelayCommand]
    private void SelectByNumber(string? number)
    {
        switch (CurrentPage)
        {
            case GameViewModel game:
                game.SelectByNumberCommand.Execute(number);
                break;

            case ModeSelectViewModel chooser:
                chooser.ChooseByNumber(number);
                break;
        }
    }

    /// <summary>Escape backs out of any page to the menu, and does nothing on the menu.</summary>
    [RelayCommand]
    private void Back()
    {
        if (!ReferenceEquals(CurrentPage, _menu))
        {
            ShowMenu();
        }
    }

    private void DisposeGame()
    {
        if (_game is null)
        {
            return;
        }

        _game.Dispose();
        _game = null;

        // Written when the run ends, however it ended: backing out to the menu with Escape
        // is an ordinary way to stop playing, not an abort.
        if (_history is not null)
        {
            _historyStore.Save(_history);
        }
    }

    public void Dispose() => DisposeGame();
}
