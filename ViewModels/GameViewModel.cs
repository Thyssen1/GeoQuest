using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.ViewModels;

/// <summary>
/// Drives a "Guess the Flag" session: round lifecycle, the countdown, scoring, and the
/// 3 -> 4 -> 5 -> 6 difficulty ramp.
/// </summary>
public partial class GameViewModel : ViewModelBase, IDisposable
{
    /// <summary>UI refresh cadence only. The countdown itself is read from <see cref="_clock"/>.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly IQuestionGenerator _generator;
    /// <summary>Null in Find the City, which draws one map rather than a picture per country.</summary>
    private readonly ICountryArtwork? _artwork;
    private readonly FileScoreStore _scores;
    private readonly DifficultyProfile _profile;
    private readonly PlayerHistory _history;

    private readonly Dictionary<string, Country> _byName;
    private readonly SystemSoundPlayer? _sounds;
    private readonly Random _random;
    private readonly DispatcherTimer _roundTimer;
    private readonly DispatcherTimer _revealTimer;
    private readonly bool _isDesignMode;

    /// <summary>
    /// The authoritative round clock. Deriving the countdown from real elapsed time is what
    /// keeps it honest: DispatcherTimer ticks arrive late under load, so accumulating a
    /// fixed interval per tick made a 12-second round last roughly 14 real seconds.
    /// </summary>
    private readonly Stopwatch _clock = new();

    private FlagQuestion? _question;
    private TimeSpan _allowed;

    private readonly Dictionary<string, Capital>? _capitals;

    [ObservableProperty]
    private int _score;

    [ObservableProperty]
    private int _bestScore;

    [ObservableProperty]
    private int _streak;

    [ObservableProperty]
    private int _bestStreak;

    [ObservableProperty]
    private int _correctAnswers;

    [ObservableProperty]
    private int _lives;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridColumns))]
    [NotifyPropertyChangedFor(nameof(AnswerKeysText))]
    private int _optionCount = GameRules.MinOptions;

    [ObservableProperty]
    private double _timeFraction = 1d;

    [ObservableProperty]
    private string _timeRemainingText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGrid))]
    [NotifyPropertyChangedFor(nameof(ShowsRecall))]
    [NotifyPropertyChangedFor(nameof(ShowsMap))]
    [NotifyPropertyChangedFor(nameof(IsMapLive))]
    private bool _isGameOver;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsAnswerName))]
    [NotifyPropertyChangedFor(nameof(IsMapLive))]
    [NotifyPropertyChangedFor(nameof(ShowsDistance))]
    private bool _isRevealing;

    [ObservableProperty]
    private bool _isNewBest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultMessage))]
    private string _resultMessage = string.Empty;

    [ObservableProperty]
    private bool _hasWonLife;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoundText))]
    private int _roundsPlayed;

    [ObservableProperty]
    private object? _promptArt;

    [ObservableProperty]
    private int _lastAward;

    [ObservableProperty]
    private bool _showsAward;

    [ObservableProperty]
    private bool _answeredCorrectly;

    [ObservableProperty]
    private bool _answeredWrongly;

    [ObservableProperty]
    private string? _selectedChoice;

    /// <summary>Where the player last clicked, or null before they have.</summary>
    [ObservableProperty]
    private GeoPoint? _droppedPin;

    /// <summary>Where the city actually is. Set only once the round is over.</summary>
    [ObservableProperty]
    private GeoPoint? _answerPlace;

    /// <summary>How far the last pin landed from the city, for the scoreboard to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DistanceText))]
    private double _lastDistanceKm;

    public event EventHandler? MenuRequested;

    public GameViewModel(
        IQuestionGenerator generator,
        ICountryArtwork? artwork,
        FileScoreStore scores,
        DifficultyProfile? profile = null,
        SystemSoundPlayer? sounds = null,
        Random? random = null,
        PlayerHistory? history = null,
        IReadOnlyList<Country>? choices = null,
        CityLoader? cities = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(scores);

        _generator = generator;
        _artwork = artwork;
        _scores = scores;

        _profile = profile ?? DifficultyProfile.Normal;
        _history = history ?? new PlayerHistory();

        // Find the City asks about a country's capital, so it runs on the same generator
        // and the same pool as every other game: the generated country is looked up here
        // and becomes a place to find rather than a picture to recognise.
        if (cities is not null)
        {
            _capitals = new Dictionary<string, Capital>(StringComparer.OrdinalIgnoreCase);

            foreach (var capital in cities.Capitals())
            {
                _capitals[capital.Code] = capital;
            }

            WorldGeometry = cities.World();
        }

        // What a recall round is answered with. Every game names the thing it showed, so a
        // city run offers capitals where the others offer countries — the same 197 entries
        // either way, because each capital stands for exactly one country.
        var answerable = (choices ?? [])
            .Select(country => (Country: country, Name: AnswerName(country)))
            .OrderBy(entry => entry.Name, StringComparer.CurrentCulture)
            .ToArray();

        _byName = answerable.ToDictionary(entry => entry.Name, entry => entry.Country, StringComparer.CurrentCulture);
        Choices = answerable.Select(entry => entry.Name).ToArray();

        // Muting happens where the run is composed: a muted game is simply given no player.
        _sounds = sounds;
        _random = random ?? Random.Shared;
        _isDesignMode = Design.IsDesignMode;

        _roundTimer = new DispatcherTimer { Interval = TickInterval };
        _roundTimer.Tick += OnRoundTick;

        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_profile.RevealSeconds) };
        _revealTimer.Tick += OnRevealElapsed;

        BestScore = _scores.LoadBestScore(_profile.Game, _profile.Mode);

        StartNewGame();
    }

    public ObservableCollection<FlagOptionViewModel> Options { get; } = [];

    public IReadOnlyList<string> Choices { get; }

    public bool IsNameInput => _profile.Input == RoundInput.Name;

    public bool IsPinInput => _profile.Input == RoundInput.Pin;

    /// <summary>The world, drawn once and shared by every round of a city run.</summary>
    public Geometry? WorldGeometry { get; }

    /// <summary>
    /// What this country is called in this game: its capital in a city run, its own name
    /// everywhere else. The prompt, the answer list and the reveal all go through here, so
    /// none of them can disagree about what the round was asking.
    /// </summary>
    private string AnswerName(Country country) =>
        _profile.Subject == RoundSubject.Place && _capitals is not null &&
        _capitals.TryGetValue(country.Code, out var capital)
            ? capital.Name
            : country.Name;

    /// <summary>The capital this round is about, or null in a game that is not about cities.</summary>
    public Capital? CurrentCapital =>
        _question is not null && _capitals is not null && _capitals.TryGetValue(_question.Answer.Code, out var capital)
            ? capital
            : null;

    public string PromptHeading => _profile.Input switch
    {
        RoundInput.Pin => "Find the capital of",
        RoundInput.Name => _profile.Subject == RoundSubject.Place
            ? "Which city is marked?"
            : "Which country is this?",
        _ => _profile.Subject == RoundSubject.Outline ? "Which outline belongs to" : "Which flag belongs to",
    };

    /// <summary>In Recall the country is the answer, so its name only appears once the round is over.</summary>
    public bool ShowsAnswerName => !IsNameInput || IsRevealing;

    /// <summary>The result strip keeps its height between rounds, but not its chrome.</summary>
    public bool HasResultMessage => !string.IsNullOrEmpty(ResultMessage);

    public bool ShowsGrid => !IsGameOver && _profile.Input == RoundInput.Grid;

    /// <summary>Picture recall. A city run names what is marked on the map instead.</summary>
    public bool ShowsRecall => !IsGameOver && IsNameInput && _profile.Subject != RoundSubject.Place;

    /// <summary>The answer box, wherever the question happens to be drawn.</summary>
    public bool ShowsNameEntry => !IsGameOver && IsNameInput;

    /// <summary>What that box is asking for, which is the only thing it changes per game.</summary>
    public string AnswerPlaceholder =>
        _profile.Subject == RoundSubject.Place ? "Type a city…" : "Type a country…";

    /// <summary>
    /// The map is on screen for both ways a city round can be played: dropping a pin on it,
    /// and being shown a pin on it to name.
    /// </summary>
    public bool ShowsMap => !IsGameOver && _profile.Subject == RoundSubject.Place;

    /// <summary>A click only counts while the round is live.</summary>
    public bool IsMapLive => ShowsMap && !IsRevealing;

    /// <summary>The distance strip only has something to say once a pin has been judged.</summary>
    public bool ShowsDistance => IsPinInput && IsRevealing;

    public string DistanceText => Describe(LastDistanceKm);

    public bool HasLives => _profile.HasLives;

    public bool IsBounded => _profile.IsBounded;

    public string RoundText => $"{Math.Min(RoundsPlayed, _profile.RoundLimit)} / {_profile.RoundLimit}";

    /// <summary>A bounded session is finished rather than lost, and should not be told otherwise.</summary>
    public string EndTitle => _profile.IsBounded ? "Session complete" : "Run over";

    public string AnswerKeysText => _profile.Input switch
    {
        RoundInput.Pin => "Click the map to drop a pin  ·  Esc for the menu",
        RoundInput.Name => "Type to search  ·  Enter to answer  ·  Esc for the menu",
        _ => $"Press 1–{OptionCount} to answer  ·  Esc for the menu",
    };

    public bool ShowsMastery => _profile.ShowsMastery;

    /// <summary>Share of the pool in the mastered box. Falls as well as rises.</summary>
    public string MasteryText => $"{_history.MasteryPercent}%";

    public string MasteryDetail => $"{_history.Graduated} / {_history.PoolSize} countries mastered";

    /// <summary>
    /// Column count that keeps the grid balanced at each size: 3 and 6 sit in rows of
    /// three, 4 as a square, 5 as three over two.
    /// </summary>
    public int GridColumns => OptionCount switch
    {
        <= 3 => 3,
        4 => 2,
        _ => 3,
    };

    [RelayCommand]
    public void StartNewGame()
    {
        _revealTimer.Stop();

        Score = 0;
        Streak = 0;
        BestStreak = 0;
        CorrectAnswers = 0;
        Lives = _profile.StartingLives;
        RoundsPlayed = 0;
        HasWonLife = false;
        IsGameOver = false;
        IsNewBest = false;
        ResultMessage = string.Empty;

        StartRound();
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void Select(FlagOptionViewModel? option)
    {
        if (option is null || !CanAnswer())
        {
            return;
        }

        var correct = Commit(option.Country);

        option.State = correct ? OptionState.Correct : OptionState.Wrong;

        if (!correct)
        {
            RevealAnswer();
        }

        BeginReveal();
    }

    /// <summary>
    /// A click on the map. The control has already turned pixels into a place, so all that
    /// is left is to judge it — there is no option to mark right or wrong, only a pin to
    /// leave where the player put it and the real city to show beside it.
    /// </summary>
    [RelayCommand]
    private void DropPin(GeoPoint place)
    {
        if (!IsPinInput || CurrentCapital is null || !CanAnswer())
        {
            return;
        }

        DroppedPin = place;

        CommitPin(place);

        AnswerPlace = CurrentCapital.Location;

        BeginReveal();
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private void Submit()
    {
        if (SelectedChoice is null || !_byName.TryGetValue(SelectedChoice, out var picked) || !CanAnswer())
        {
            return;
        }

        Commit(picked);

        BeginReveal();
    }

    /// <summary>What a round does with an answer, however it was given.</summary>
    private bool Commit(Country picked)
    {
        // Read the clock before stopping it so the speed bonus reflects the real answer time.
        var remaining = _allowed - _clock.Elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        _roundTimer.Stop();
        _clock.Stop();

        var correct = picked.Code == _question!.Answer.Code;

        // Recorded before the boxes are read anywhere, and in every mode: mastery is a
        // claim about what the player knows, not about which mode they picked.
        Record(correct, LearningRules.IsFast(remaining, _allowed));

        if (correct)
        {
            TakeCorrect(GameRules.ScoreFor(remaining, _allowed, Streak + 1));

            return true;
        }

        // Naming the wrong country teaches nothing unless the right one is named back.
        // Pointing at the wrong flag is better explained by which flag was pointed at.
        TakeWrong(IsNameInput
            ? $"That was {AnswerName(_question.Answer)}"
            : $"That was {AnswerName(picked)}");

        return false;
    }

    /// <summary>
    /// A pin answer. Nothing was chosen, so there is no wrong option to name back — only a
    /// distance, and whether the mode accepts it at this point in the run.
    /// </summary>
    private bool CommitPin(GeoPoint dropped)
    {
        var remaining = _allowed - _clock.Elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        _roundTimer.Stop();
        _clock.Stop();

        var city = CurrentCapital!;
        var distance = dropped.DistanceTo(city.Location);
        var tolerance = GameRules.ToleranceFor(CorrectAnswers, _profile);
        var correct = distance <= tolerance;

        LastDistanceKm = distance;
        Record(correct, LearningRules.IsFast(remaining, _allowed));

        if (correct)
        {
            TakeCorrect(GameRules.ScoreForPin(distance, tolerance, remaining, _allowed, Streak + 1));

            return true;
        }

        TakeWrong($"That was {city.Name}");

        return false;
    }

    /// <summary>
    /// How far off, rounded the way a person would say it rather than to a false precision.
    /// The map is about a thousand pixels across, so the last digits of a distance say more
    /// about which pixel was clicked than about what the player knew.
    /// </summary>
    private static string Describe(double distanceKm) => distanceKm switch
    {
        < 10d => "Spot on",
        < 1000d => $"{Math.Round(distanceKm / 10d) * 10d:N0} km away",
        _ => $"{Math.Round(distanceKm / 100d) * 100d:N0} km away",
    };

    /// <summary>What a right answer does, however it was given.</summary>
    private void TakeCorrect(int award)
    {
        Streak++;
        BestStreak = Math.Max(BestStreak, Streak);
        CorrectAnswers++;

        AddScore(award);

        LastAward = award;
        ShowsAward = true;

        if (GameRules.AwardsBonusLife(Lives, _random.NextDouble(), _profile.BonusLifeChance))
        {
            Lives++;
            HasWonLife = true;

            ResultMessage = "Correct — extra life!";
            Play(GameSound.BonusLife);
        }
        else
        {
            ResultMessage = Streak > 1 ? $"Correct — {Streak} in a row" : "Correct";
            Play(GameSound.Correct);
        }

        AnsweredCorrectly = true;
    }

    /// <summary>And what a wrong one does.</summary>
    private void TakeWrong(string message)
    {
        AnsweredWrongly = true;

        Streak = 0;
        LoseLife();

        ResultMessage = message;

        Play(GameSound.Wrong);
    }

    private bool CanAnswer() => _question is not null && !IsRevealing && !IsGameOver;

    /// <summary>Free text that matches no country is not an answer, so it cannot be committed.</summary>
    private bool CanSubmit() =>
        SelectedChoice is not null && _byName.ContainsKey(SelectedChoice) && !IsRevealing && !IsGameOver;

    partial void OnSelectedChoiceChanged(string? value) => SubmitCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Keyboard entry point. The parameter arrives from XAML as a string, so it is parsed
    /// here rather than typed as int, which a KeyBinding cannot supply directly.
    /// </summary>
    [RelayCommand]
    private void SelectByNumber(string? number)
    {
        if (!int.TryParse(number, out var position))
        {
            return;
        }

        var index = position - 1;

        if (index < 0 || index >= Options.Count)
        {
            return;
        }

        var option = Options[index];

        if (CanSelect(option))
        {
            Select(option);
        }
    }

    [RelayCommand]
    private void ReturnToMenu()
    {
        _roundTimer.Stop();
        _revealTimer.Stop();
        _clock.Stop();

        MenuRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSelect(FlagOptionViewModel? option) => !IsRevealing && !IsGameOver;

    private void Record(bool correct, bool fast)
    {
        if (_history.Record(_question!.Answer.Code, correct, fast))
        {
            OnPropertyChanged(nameof(MasteryText));
            OnPropertyChanged(nameof(MasteryDetail));
        }
    }

    private void LoseLife()
    {
        if (_profile.HasLives)
        {
            Lives--;
        }
    }

    private void Play(GameSound sound)
    {
        if (!_isDesignMode)
        {
            _sounds?.Play(sound);
        }
    }

    private void AddScore(int points)
    {
        Score += points;

        if (Score <= BestScore)
        {
            return;
        }

        BestScore = Score;
        IsNewBest = true;

        // Persisted the moment the record is beaten, so closing the app mid-run
        // does not throw the score away.
        _scores.SaveBestScore(_profile.Game, _profile.Mode, BestScore);
    }

    private void StartRound()
    {
        OptionCount = GameRules.OptionCountFor(CorrectAnswers, _profile);
        _allowed = GameRules.RoundDurationFor(CorrectAnswers, _profile);
        RoundsPlayed++;

        _question = _generator.Next(OptionCount);

        // In a city run the country is only how the question is drawn from the pool; what
        // the player is shown, and judged on, is its capital.
        Prompt = AnswerName(_question.Answer);

        // The reveal delay has already given the player time to read the previous
        // result; carrying it into a live round just reads as stale feedback.
        ResultMessage = string.Empty;
        HasWonLife = false;

        Options.Clear();
        PromptArt = null;
        SelectedChoice = null;
        AnsweredCorrectly = false;
        AnsweredWrongly = false;
        ShowsAward = false;
        DroppedPin = null;
        AnswerPlace = null;

        OnPropertyChanged(nameof(CurrentCapital));

        // Naming a marked city makes the marker the question, so it goes up with the round
        // rather than waiting for the reveal the way a dropped pin's answer does.
        if (IsNameInput && _profile.Subject == RoundSubject.Place)
        {
            AnswerPlace = CurrentCapital?.Location;
        }

        // A pin round builds nothing else here: the map is the same every round, and the
        // question is a city's name rather than anything drawn.
        if (IsNameInput)
        {
            // The flag is the question here, so the generated options go unused.
            PromptArt = _artwork?.For(_question.Answer.Code);
        }
        else if (!IsPinInput)
        {
            foreach (var country in _question.Options)
            {
                Options.Add(new FlagOptionViewModel(country, _artwork?.For(country.Code))
                {
                    Key = (Options.Count + 1).ToString(),
                });
            }
        }

        IsRevealing = false;
        UpdateTimeDisplay(_allowed);
        SelectCommand.NotifyCanExecuteChanged();
        SubmitCommand.NotifyCanExecuteChanged();

        // The XAML previewer gets a fully rendered round but no running clock, so it
        // neither burns CPU nor ticks down to a game over inside the IDE.
        if (!_isDesignMode)
        {
            _clock.Restart();
            _roundTimer.Start();
        }
    }

    private void OnRoundTick(object? sender, EventArgs e)
    {
        var remaining = _allowed - _clock.Elapsed;

        if (remaining > TimeSpan.Zero)
        {
            UpdateTimeDisplay(remaining);
            return;
        }

        UpdateTimeDisplay(TimeSpan.Zero);

        _roundTimer.Stop();
        _clock.Stop();

        RevealAnswer();

        Record(correct: false, fast: false);

        AnsweredWrongly = true;

        Streak = 0;
        LoseLife();
        ResultMessage = "Out of time";
        Play(GameSound.Wrong);

        BeginReveal();
    }

    private void BeginReveal()
    {
        foreach (var option in Options)
        {
            option.IsDimmed = option.State == OptionState.Idle;
        }

        IsRevealing = true;
        SelectCommand.NotifyCanExecuteChanged();
        SubmitCommand.NotifyCanExecuteChanged();

        _revealTimer.Stop();
        _revealTimer.Start();
    }

    private void OnRevealElapsed(object? sender, EventArgs e) => CompleteReveal();

    /// <summary>
    /// Ends the reveal and moves the run on. The timer only schedules this; keeping the
    /// decision separate is what lets a test step through a run without racing a clock.
    /// </summary>
    internal void CompleteReveal()
    {
        _revealTimer.Stop();

        if (_profile.HasLives && Lives <= 0)
        {
            EndGame();
            return;
        }

        if (_profile.IsBounded && RoundsPlayed >= _profile.RoundLimit)
        {
            EndGame();
            return;
        }

        StartRound();
    }

    private void RevealAnswer()
    {
        if (_question is null)
        {
            return;
        }

        // A pin round reveals by marking the city on the map rather than lighting a tile,
        // which matters most on a timeout, where no pin was ever dropped to compare against.
        if (IsPinInput)
        {
            AnswerPlace = CurrentCapital?.Location;
            return;
        }

        var answer = Options.FirstOrDefault(o => o.Country.Code == _question.Answer.Code);

        if (answer is not null && answer.State == OptionState.Idle)
        {
            answer.State = OptionState.Revealed;
        }
    }

    private void EndGame()
    {
        _roundTimer.Stop();
        _revealTimer.Stop();
        _clock.Stop();

        IsGameOver = true;
        IsRevealing = false;
        Options.Clear();
        ResultMessage = BestStreak > 0 ? $"Best streak: {BestStreak}" : string.Empty;

        SelectCommand.NotifyCanExecuteChanged();
        SubmitCommand.NotifyCanExecuteChanged();
    }

    private void UpdateTimeDisplay(TimeSpan remaining)
    {
        TimeFraction = _allowed > TimeSpan.Zero
            ? Math.Clamp(remaining.TotalSeconds / _allowed.TotalSeconds, 0d, 1d)
            : 0d;

        TimeRemainingText = remaining.TotalSeconds.ToString("0.0");
    }

    public void Dispose()
    {
        _roundTimer.Stop();
        _roundTimer.Tick -= OnRoundTick;

        _revealTimer.Stop();
        _revealTimer.Tick -= OnRevealElapsed;

        _clock.Stop();
    }
}
