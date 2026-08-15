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
    /// <summary>Wrong answers allowed before the run ends.</summary>
    private const int StartingLives = 3;

    /// <summary>How long the correct answer stays on screen before the next round.</summary>
    private static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(1300);

    /// <summary>UI refresh cadence only. The countdown itself is read from <see cref="_clock"/>.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly IQuestionGenerator _generator;
    private readonly IFlagImageLoader _images;
    private readonly IScoreStore _scores;
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
    private int _lives = StartingLives;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridColumns))]
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

    public GameViewModel(ICountryRepository repository, IFlagImageLoader images, IScoreStore scores)
        : this(new RandomQuestionGenerator(repository), images, scores)
    {
    }

    public GameViewModel(IQuestionGenerator generator, IFlagImageLoader images, IScoreStore scores)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(scores);

        _generator = generator;
        _images = images;
        _scores = scores;
        _isDesignMode = Design.IsDesignMode;

        _roundTimer = new DispatcherTimer { Interval = TickInterval };
        _roundTimer.Tick += OnRoundTick;

        _revealTimer = new DispatcherTimer { Interval = RevealDelay };
        _revealTimer.Tick += OnRevealElapsed;

        BestScore = _scores.LoadBestScore();

        StartNewGame();
    }

    public ObservableCollection<FlagOptionViewModel> Options { get; } = [];

    public string LivesText => string.Concat(Enumerable.Repeat("♥", Math.Max(0, Lives)));

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
        Lives = StartingLives;
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

        if (option.Country.Code == _question.Answer.Code)
        {
            option.State = OptionState.Correct;

            Streak++;
            BestStreak = Math.Max(BestStreak, Streak);
            CorrectAnswers++;
            AddScore(GameRules.ScoreFor(remaining, _allowed, Streak));

            ResultMessage = Streak > 1 ? $"Correct — {Streak} in a row" : "Correct";
        }
        else
        {
            option.State = OptionState.Wrong;
            RevealAnswer();

            Streak = 0;
            Lives--;
            ResultMessage = $"That was {option.Country.Name}";
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

    private bool CanSelect(FlagOptionViewModel? option) => !IsRevealing && !IsGameOver;

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
        _scores.SaveBestScore(BestScore);
    }

    private void StartRound()
    {
        OptionCount = GameRules.OptionCountFor(CorrectAnswers);
        _allowed = GameRules.RoundDurationFor(CorrectAnswers);

        _question = _generator.Next(OptionCount);
        Prompt = _question.Answer.Name;

        // The reveal delay has already given the player time to read the previous
        // result; carrying it into a live round just reads as stale feedback.
        ResultMessage = string.Empty;

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
        Streak = 0;
        Lives--;
        ResultMessage = "Out of time";

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

        if (Lives <= 0)
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
