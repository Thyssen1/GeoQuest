using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.ViewModels;

/// <summary>
/// Settings screen. Changes are written as they are made rather than gathered behind
/// an OK button, so backing out of this screen can never lose a change.
/// </summary>
public partial class OptionsViewModel : ViewModelBase
{
    private readonly ISettingsStore _settings;
    private readonly IScoreStore _scores;

    /// <summary>The stored settings as they currently stand, so saving one setting never
    /// writes over another that this screen happens not to show.</summary>
    private GameSettings _current;

    /// <summary>Suppresses saving while the initial value is being applied to the UI.</summary>
    private bool _loading;

    [ObservableProperty]
    private int _startingLives;

    [ObservableProperty]
    private bool _soundEnabled;

    /// <summary>True when any mode has a score to lose. The chooser shows the numbers.</summary>
    [ObservableProperty]
    private bool _hasBestScore;

    /// <summary>Drives the two-step confirmation on the destructive reset.</summary>
    [ObservableProperty]
    private bool _isConfirmingReset;

    public OptionsViewModel(ISettingsStore settings, IScoreStore scores)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scores);

        _settings = settings;
        _scores = scores;

        _current = settings.Load().Sanitised();

        _loading = true;
        StartingLives = _current.StartingLives;
        SoundEnabled = _current.SoundEnabled;
        _loading = false;

        HasBestScore = AnyScoreRecorded(scores);
    }

    public event EventHandler? BackRequested;

    // RadioButton binds naturally to a two-way bool, which avoids needing a converter
    // to compare the selected value against each choice.
    public bool IsOneLife
    {
        get => StartingLives == 1;
        set { if (value) StartingLives = 1; }
    }

    public bool IsThreeLives
    {
        get => StartingLives == 3;
        set { if (value) StartingLives = 3; }
    }

    public bool IsFiveLives
    {
        get => StartingLives == 5;
        set { if (value) StartingLives = 5; }
    }

    /// <summary>Surfaced so a player can find, back up or hand-edit their save.</summary>
    public string SaveLocation { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GeoQuest");

    partial void OnStartingLivesChanged(int value)
    {
        OnPropertyChanged(nameof(IsOneLife));
        OnPropertyChanged(nameof(IsThreeLives));
        OnPropertyChanged(nameof(IsFiveLives));

        if (_loading)
        {
            return;
        }

        Save(_current with { StartingLives = value });
    }

    partial void OnSoundEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Save(_current with { SoundEnabled = value });
    }

    private void Save(GameSettings settings)
    {
        _current = settings;
        _settings.Save(settings);
    }

    private static bool AnyScoreRecorded(IScoreStore scores) =>
        Enum.GetValues<GameMode>().Any(mode => scores.LoadBestScore(mode) > 0);

    [RelayCommand]
    private void BeginResetScore() => IsConfirmingReset = true;

    [RelayCommand]
    private void CancelResetScore() => IsConfirmingReset = false;

    [RelayCommand]
    private void ConfirmResetScore()
    {
        _scores.ClearBestScores();

        HasBestScore = false;
        IsConfirmingReset = false;
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}
