using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
public partial class GameViewModel : ViewModelBase
{
    /// <summary>Wrong answers allowed before the run ends.</summary>
    private const int StartingLives = 3;

    /// <summary>How long the correct answer stays on screen before the next round.</summary>
    private static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(1300);

    private static readonly TimeSpan TimerInterval = TimeSpan.FromMilliseconds(50);

    private readonly IQuestionGenerator _generator;
    private readonly IFlagImageLoader _images;
    private readonly DispatcherTimer _timer;

    private FlagQuestion? _question;
    private TimeSpan _allowed;
    private TimeSpan _remaining;

    /// <summary>Guards against a late reveal from an abandoned round resuming play.</summary>
    private CancellationTokenSource? _revealCts;

    [ObservableProperty]
    private int _score;

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

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    public GameViewModel(ICountryRepository repository, IFlagImageLoader images)
        : this(new RandomQuestionGenerator(repository), images)
    {
    }

    public GameViewModel(IQuestionGenerator generator, IFlagImageLoader images)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(images);

        _generator = generator;
        _images = images;

        _timer = new DispatcherTimer { Interval = TimerInterval };
        _timer.Tick += OnTick;

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
        CancelReveal();

        Score = 0;
        Streak = 0;
        BestStreak = 0;
        CorrectAnswers = 0;
        Lives = StartingLives;
        IsGameOver = false;
        ResultMessage = string.Empty;

        StartRound();
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private async Task SelectAsync(FlagOptionViewModel? option)
    {
        if (option is null || _question is null || IsRevealing || IsGameOver)
        {
            return;
        }

        _timer.Stop();
        IsRevealing = true;
        SelectCommand.NotifyCanExecuteChanged();

        var correct = option.Country.Code == _question.Answer.Code;

        if (correct)
        {
            option.State = OptionState.Correct;

            Streak++;
            BestStreak = Math.Max(BestStreak, Streak);
            CorrectAnswers++;
            Score += GameRules.ScoreFor(_remaining, _allowed, Streak);
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

        await ContinueAfterRevealAsync();
    }

    private bool CanSelect(FlagOptionViewModel? option) => !IsRevealing && !IsGameOver;

    private void StartRound()
    {
        OptionCount = GameRules.OptionCountFor(CorrectAnswers);
        _allowed = GameRules.RoundDurationFor(CorrectAnswers);
        _remaining = _allowed;

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
        UpdateTimeDisplay();
        SelectCommand.NotifyCanExecuteChanged();

        _timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        _remaining -= TimerInterval;

        if (_remaining > TimeSpan.Zero)
        {
            UpdateTimeDisplay();
            return;
        }

        _remaining = TimeSpan.Zero;
        UpdateTimeDisplay();

        _timer.Stop();
        IsRevealing = true;
        SelectCommand.NotifyCanExecuteChanged();

        RevealAnswer();
        Streak = 0;
        Lives--;
        ResultMessage = "Out of time";

        await ContinueAfterRevealAsync();
    }

    private async Task ContinueAfterRevealAsync()
    {
        CancelReveal();
        _revealCts = new CancellationTokenSource();
        var token = _revealCts.Token;

        try
        {
            await Task.Delay(RevealDelay, token);
        }
        catch (OperationCanceledException)
        {
            // A new game started during the reveal; that path owns the next round.
            return;
        }

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
        _timer.Stop();
        IsGameOver = true;
        IsRevealing = false;
        Options.Clear();
        ResultMessage = BestStreak > 0
            ? $"Best streak: {BestStreak}"
            : string.Empty;

        SelectCommand.NotifyCanExecuteChanged();
    }

    private void CancelReveal()
    {
        _revealCts?.Cancel();
        _revealCts?.Dispose();
        _revealCts = null;
    }

    private void UpdateTimeDisplay()
    {
        TimeFraction = _allowed > TimeSpan.Zero
            ? Math.Clamp(_remaining.TotalSeconds / _allowed.TotalSeconds, 0d, 1d)
            : 0d;

        TimeRemainingText = _remaining.TotalSeconds.ToString("0.0");
    }
}
