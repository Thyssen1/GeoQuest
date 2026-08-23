using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
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
    /// <summary>How long the correct answer stays on screen before the next round.</summary>
    private static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(1300);

    /// <summary>UI refresh cadence only. The countdown itself is read from <see cref="_clock"/>.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly IQuestionGenerator _generator;
    private readonly IFlagImageLoader _images;
    private readonly IScoreStore _scores;
    private readonly DifficultyProfile _profile;
    private readonly PlayerHistory _history;
    private readonly ISoundPlayer? _sounds;
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
    [NotifyPropertyChangedFor(nameof(LivesText))]
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
    private bool _isGameOver;

    /// <summary>True between the player's pick and the start of the next round.</summary>
    [ObservableProperty]
    private bool _isRevealing;

    /// <summary>Set when this run beats the stored best score.</summary>
    [ObservableProperty]
    private bool _isNewBest;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    /// <summary>True for the round in which a life was won, so the scoreboard can say so.</summary>
    [ObservableProperty]
    private bool _hasWonLife;

    /// <summary>Rounds started in this run, including the one on screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoundText))]
    private int _roundsPlayed;

    /// <summary>Raised when the player asks to leave the run and return to the menu.</summary>
    public event EventHandler? MenuRequested;

    public GameViewModel(
        IQuestionGenerator generator,
        IFlagImageLoader images,
        IScoreStore scores,
        DifficultyProfile? profile = null,
        ISoundPlayer? sounds = null,
        Random? random = null,
        PlayerHistory? history = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(scores);

        _generator = generator;
        _images = images;
        _scores = scores;

        _profile = profile ?? DifficultyProfile.Normal;
        _history = history ?? new PlayerHistory();

        // Muting happens where the run is composed: a muted game is simply given no player.
        _sounds = sounds;
        _random = random ?? Random.Shared;
        _isDesignMode = Design.IsDesignMode;

        _roundTimer = new DispatcherTimer { Interval = TickInterval };
        _roundTimer.Tick += OnRoundTick;

        _revealTimer = new DispatcherTimer { Interval = RevealDelay };
        _revealTimer.Tick += OnRevealElapsed;

        BestScore = _scores.LoadBestScore(_profile.Mode);

        StartNewGame();
    }

    public ObservableCollection<FlagOptionViewModel> Options { get; } = [];

    public string LivesText => string.Concat(Enumerable.Repeat("♥", Math.Max(0, Lives)));

    /// <summary>False in a mode that cannot be lost, where the scoreboard hides lives entirely.</summary>
    public bool HasLives => _profile.HasLives;

    /// <summary>True in a mode that ends after a set number of rounds.</summary>
    public bool IsBounded => _profile.IsBounded;

    public string RoundText => $"{Math.Min(RoundsPlayed, _profile.RoundLimit)} / {_profile.RoundLimit}";

    /// <summary>A bounded session is finished rather than lost, and should not be told otherwise.</summary>
    public string EndTitle => _profile.IsBounded ? "Session complete" : "Run over";

    /// <summary>False in Hard, whose board is about the run rather than the long game.</summary>
    /// <summary>Names the keys that actually work: the grid is not always six wide.</summary>
    public string AnswerKeysText => $"Press 1–{OptionCount} to answer  ·  Esc for the menu";

    public bool ShowsMastery => _profile.ShowsMastery;

    /// <summary>Share of the pool in the mastered box. Falls as well as rises.</summary>
    public string MasteryText => $"{_history.MasteryPercent}%";

    public string MasteryDetail => $"{_history.Graduated} / {_history.PoolSize} flags mastered";

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
        if (option is null || _question is null || IsRevealing || IsGameOver)
        {
            return;
        }

        // Read the clock before stopping it so the speed bonus reflects the real answer time.
        var remaining = _allowed - _clock.Elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        _roundTimer.Stop();
        _clock.Stop();

        var correct = option.Country.Code == _question.Answer.Code;

        // Recorded before the boxes are read anywhere, and in every mode: mastery is a
        // claim about what the player knows, not about which mode they picked.
        Record(correct, LearningRules.IsFast(remaining, _allowed));

        if (correct)
        {
            option.State = OptionState.Correct;

            Streak++;
            BestStreak = Math.Max(BestStreak, Streak);
            CorrectAnswers++;
            AddScore(GameRules.ScoreFor(remaining, _allowed, Streak));

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
        }
        else
        {
            option.State = OptionState.Wrong;
            RevealAnswer();

            Streak = 0;
            LoseLife();
            ResultMessage = $"That was {option.Country.Name}";
            Play(GameSound.Wrong);
        }

        BeginReveal();
    }

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

    /// <summary>Abandons the run and hands control back to the menu.</summary>
    [RelayCommand]
    private void ReturnToMenu()
    {
        _roundTimer.Stop();
        _revealTimer.Stop();
        _clock.Stop();

        MenuRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSelect(FlagOptionViewModel? option) => !IsRevealing && !IsGameOver;

    /// <summary>Files the round's outcome against the flag that was the answer.</summary>
    private void Record(bool correct, bool fast)
    {
        if (_question is null)
        {
            return;
        }

        if (_history.Record(_question.Answer.Code, correct, fast))
        {
            OnPropertyChanged(nameof(MasteryText));
            OnPropertyChanged(nameof(MasteryDetail));
        }
    }

    /// <summary>Costs a life, in the modes that have them.</summary>
    private void LoseLife()
    {
        if (_profile.HasLives)
        {
            Lives--;
        }
    }

    /// <summary>Plays a sound, unless this is the previewer or the run was given no player.</summary>
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
        _scores.SaveBestScore(_profile.Mode, BestScore);
    }

    private void StartRound()
    {
        OptionCount = GameRules.OptionCountFor(CorrectAnswers, _profile);
        _allowed = GameRules.RoundDurationFor(CorrectAnswers, _profile);
        RoundsPlayed++;

        _question = _generator.Next(OptionCount);
        Prompt = _question.Answer.Name;

        // The reveal delay has already given the player time to read the previous
        // result; carrying it into a live round just reads as stale feedback.
        ResultMessage = string.Empty;
        HasWonLife = false;

        Options.Clear();
        foreach (var country in _question.Options)
        {
            Options.Add(new FlagOptionViewModel(country, _images.Load(country.Code)));
        }

        IsRevealing = false;
        UpdateTimeDisplay(_allowed);
        SelectCommand.NotifyCanExecuteChanged();

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

        Streak = 0;
        LoseLife();
        ResultMessage = "Out of time";
        Play(GameSound.Wrong);

        BeginReveal();
    }

    private void BeginReveal()
    {
        IsRevealing = true;
        SelectCommand.NotifyCanExecuteChanged();

        _revealTimer.Stop();
        _revealTimer.Start();
    }

    private void OnRevealElapsed(object? sender, EventArgs e)
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
