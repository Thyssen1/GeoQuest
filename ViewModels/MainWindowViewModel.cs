using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.ViewModels;

/// <summary>
/// Navigation shell. Owns which page is on screen and the lifetime of each one;
/// the pages themselves only announce intent, so navigation rules live in one place.
///
/// Play asks two questions in turn — which game, then which mode — because the two are
/// independent and every mode exists for every game.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly FileSettingsStore _settings;
    private readonly FileScoreStore _scores;
    private readonly FileHistoryStore _historyStore;

    /// <summary>Shared across runs: it caches the unpacked WAVs, so one instance keeps that work done once.</summary>
    private readonly SystemSoundPlayer _sounds = new SystemSoundPlayer();

    /// <summary>Each game's artwork source, built once: both cache what they load.</summary>
    private readonly CityLoader _cities = new();

    /// <summary>Each game's artwork source, built once: all of them cache what they load.</summary>
    private readonly Dictionary<MiniGame, ICountryArtwork> _artwork;

    private readonly Dictionary<MiniGame, PlayerHistory> _history = [];

    private MenuViewModel? _menu;
    private GameViewModel? _game;
    private MiniGame _runningGame = MiniGame.Flags;

    [ObservableProperty]
    private ViewModelBase _currentPage = null!;

    public MainWindowViewModel()
        : this(new FileSettingsStore(), new FileScoreStore(), new FileHistoryStore())
    {
    }

    public MainWindowViewModel(FileSettingsStore settings, FileScoreStore scores, FileHistoryStore? history = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scores);

        _settings = settings;
        _scores = scores;
        _historyStore = history ?? new FileHistoryStore();

        // Built here rather than inline because Find the City's loader is shared with the
        // run itself, which needs the map and the capitals as well as the artwork slot.
        _artwork = new Dictionary<MiniGame, ICountryArtwork>
        {
            [MiniGame.Flags] = new FlagImageLoader(),
            [MiniGame.Borders] = new OutlineLoader(),
        };

        ShowMenu();
    }

    public event EventHandler? ExitRequested;

    /// <summary>
    /// What the player has shown in one game. Each keeps its own progress: knowing a
    /// country's flag says nothing about whether you would recognise its outline.
    /// </summary>
    private PlayerHistory History(MiniGame game)
    {
        if (!_history.TryGetValue(game, out var history))
        {
            history = _historyStore.Load(game, AssetCountryData.Instance.QuestionPool.Count);
            _history[game] = history;
        }

        return history;
    }

    public void ShowMenu()
    {
        DisposeGame();

        var settings = _settings.Load().Sanitised();

        if (_menu is null)
        {
            _menu = new MenuViewModel(_scores, _settings, History(settings.Game));
            _menu.PlayRequested += (_, _) => ShowGameSelect();
            _menu.OptionsRequested += (_, _) => ShowOptions();
            _menu.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // A run may have set a new best score, or Options may have cleared it.
            _menu.Refresh(History(settings.Game));
        }

        CurrentPage = _menu;
    }

    private void ShowGameSelect()
    {
        DisposeGame();

        var settings = _settings.Load().Sanitised();
        var chooser = new GameSelectViewModel(game => History(game).MasteryPercent, settings.Game);

        chooser.GameChosen += (_, game) => ShowModeSelect(game);
        chooser.BackRequested += (_, _) => ShowMenu();

        CurrentPage = chooser;
    }

    private void ShowModeSelect(MiniGame game)
    {
        DisposeGame();

        var settings = _settings.Load().Sanitised();
        var chooser = new ModeSelectViewModel(_scores, game, settings.Mode, History(game));

        chooser.ModeChosen += (_, mode) => StartRun(game, mode);

        // Back steps one level rather than all the way out: the game was a separate choice.
        chooser.BackRequested += (_, _) => ShowGameSelect();

        CurrentPage = chooser;
    }

    /// <summary>
    /// A run is built fresh for the chosen game and mode, picking up any settings changed
    /// in between and leaving no timers running behind the menu.
    /// </summary>
    private void StartRun(MiniGame game, GameMode mode)
    {
        DisposeGame();

        var settings = _settings.Load().Sanitised();

        // Remembered so the choosers offer this game and mode first next time.
        if (settings.Game != game || settings.Mode != mode)
        {
            _settings.Save(settings with { Game = game, Mode = mode });
        }

        var repository = AssetCountryData.Instance;

        // Learning draws from what is being learned; the other modes draw from the world.
        IQuestionGenerator generator = mode == GameMode.Learning
            ? new LearningQuestionGenerator(repository, History(game))
            : new RandomQuestionGenerator(repository);

        _runningGame = game;

        _game = new GameViewModel(
            generator,
            _artwork.GetValueOrDefault(game),
            _scores,
            DifficultyProfile.For(game, mode, settings),
            // Muting a run is giving it no sound player at all.
            settings.SoundEnabled ? _sounds : null,
            random: null,
            history: History(game),
            // Only Recall shows the list; the other modes never need it.
            choices: repository.QuestionPool,
            // Only Find the City reads it, and it caches, so one instance serves every run.
            cities: game == MiniGame.Cities ? _cities : null);

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

            case ModeSelectViewModel modes:
                modes.ChooseByNumber(number);
                break;

            case GameSelectViewModel games:
                games.ChooseByNumber(number);
                break;
        }
    }

    /// <summary>Escape backs out one page at a time, and does nothing on the menu.</summary>
    [RelayCommand]
    private void Back()
    {
        switch (CurrentPage)
        {
            case ModeSelectViewModel modes:
                ShowGameSelect();
                break;

            case MenuViewModel:
                break;

            default:
                ShowMenu();
                break;
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
        if (_history.TryGetValue(_runningGame, out var history))
        {
            _historyStore.Save(_runningGame, history);
        }
    }

    public void Dispose() => DisposeGame();
}
