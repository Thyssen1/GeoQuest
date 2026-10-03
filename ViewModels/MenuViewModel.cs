using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.ViewModels;

/// <summary>
/// The intro screen. Raises navigation intent as events rather than driving navigation
/// itself, so the shell stays the single place that knows how pages fit together.
/// </summary>
public partial class MenuViewModel : ViewModelBase
{
    private readonly IScoreStore _scores;
    private readonly ISettingsStore _settings;
    private readonly PlayerHistory _history;

    [ObservableProperty]
    private int _bestScore;

    /// <summary>Names the mode the score belongs to; four modes make a bare "BEST" ambiguous.</summary>
    [ObservableProperty]
    private string _bestScoreLabel = string.Empty;

    /// <summary>Lives a run will start with, so the setting is visible before pressing Play.</summary>
    [ObservableProperty]
    private string _startingLivesText = string.Empty;

    [ObservableProperty]
    private string _masteryDetail = string.Empty;

    [ObservableProperty]
    private int _masteryPercent;

    public MenuViewModel(IScoreStore scores, ISettingsStore settings, PlayerHistory? history = null)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(settings);

        _scores = scores;
        _settings = settings;
        _history = history ?? new PlayerHistory();

        Refresh();
    }

    public event EventHandler? PlayRequested;

    public event EventHandler? OptionsRequested;

    public event EventHandler? ExitRequested;

    /// <summary>Shown small in the corner so a bug report can name the build.</summary>
    public string VersionText { get; } = ResolveVersion();

    public bool HasBestScore => BestScore > 0;

    /// <summary>
    /// Re-reads the score, so returning from a run or from Options shows the current value.
    /// The mode shown is the one last played, which is the score the player is chasing.
    /// </summary>
    public void Refresh()
    {
        var settings = _settings.Load().Sanitised();

        BestScore = _scores.LoadBestScore(settings.Mode);
        BestScoreLabel = settings.Mode.ToString().ToUpperInvariant();
        StartingLivesText = settings.StartingLives.ToString();

        MasteryDetail = $"{_history.Graduated} / {_history.PoolSize}";
        MasteryPercent = _history.MasteryPercent;

        OnPropertyChanged(nameof(HasBestScore));
    }

    [RelayCommand]
    private void Play() => PlayRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Options() => OptionsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    private static string ResolveVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");
        }

        // Strip the source-control hash that SourceLink appends: "1.0.0+abc123" -> "1.0.0".
        var plus = informational.IndexOf('+');
        return "v" + (plus >= 0 ? informational[..plus] : informational);
    }
}
