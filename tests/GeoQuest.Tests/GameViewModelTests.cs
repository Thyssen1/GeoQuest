using GeoQuest.Models;
using GeoQuest.Services;
using GeoQuest.ViewModels;

namespace GeoQuest.Tests;

/// <summary>
/// The run itself: lives, scoring, the two ways of answering, and what each mode does
/// differently. These are the combinations the profiles multiply together, and nothing
/// covered them until the headless session made the view model constructible.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public class GameViewModelTests : IDisposable
{
    private readonly HeadlessSession _avalonia;

    private readonly string _temp =
        Path.Combine(Path.GetTempPath(), "GeoQuestTests", Guid.NewGuid().ToString("N"));

    public GameViewModelTests(HeadlessSession avalonia) => _avalonia = avalonia;

    private FileScoreStore NewScores() => new(Path.Combine(_temp, Guid.NewGuid().ToString("N") + ".json"));

    private static JsonCountryRepository LoadRepository()
    {
        using var stream = File.OpenRead(TestPaths.CountriesJson);
        return JsonCountryRepository.Load(stream);
    }

    /// <summary>A run under a chosen profile, seeded so a failure is reproducible.</summary>
    private GameViewModel NewGame(
        DifficultyProfile? profile = null,
        PlayerHistory? history = null,
        FileScoreStore? scores = null,
        int seed = 20260423)
    {
        var repository = LoadRepository();

        return new GameViewModel(
            new RandomQuestionGenerator(repository, new Random(seed)),
            new NoImages(),
            scores ?? NewScores(),
            profile,
            sounds: null,
            random: new Random(seed),
            history: history,
            choices: repository.QuestionPool);
    }

    private static FlagOptionViewModel Right(GameViewModel game) =>
        game.Options.First(o => o.Country.Name == game.Prompt);

    private static FlagOptionViewModel Wrong(GameViewModel game) =>
        game.Options.First(o => o.Country.Name != game.Prompt);

    // ---- what each mode sets up ----

    [Fact]
    public void A_normal_run_starts_from_the_settings() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.For(GameMode.Normal, new GameSettings { StartingLives = 5 }));

        Assert.Equal(5, game.Lives);
        Assert.Equal(3, game.Options.Count);
        Assert.True(game.HasLives);
        Assert.False(game.IsBounded);
    });

    [Fact]
    public void Hard_gives_three_lives_whatever_the_settings_say() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.For(GameMode.Hard, new GameSettings { StartingLives = 5 }));

        Assert.Equal(3, game.Lives);
        Assert.Equal(4, game.Options.Count);
    });

    [Fact]
    public void Learning_cannot_be_lost_and_counts_its_rounds() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.Learning);

        Assert.False(game.HasLives);
        Assert.True(game.IsBounded);
        Assert.Equal("1 / 20", game.RoundText);
        Assert.Equal("Session complete", game.EndTitle);
    });

    [Fact]
    public void Recall_offers_every_country_and_no_grid() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.Recall);

        Assert.True(game.IsNameInput);
        Assert.Empty(game.Options);
        Assert.Equal(197, game.Choices.Count);
        Assert.False(game.ShowsAnswerName);
        Assert.Contains("Type to search", game.AnswerKeysText, StringComparison.Ordinal);
    });

    // ---- answering by pointing ----

    [Fact]
    public void A_right_pick_scores_and_counts() => _avalonia.Run(() =>
    {
        using var game = NewGame();

        game.SelectCommand.Execute(Right(game));

        Assert.True(game.Score > 0);
        Assert.Equal(1, game.CorrectAnswers);
        Assert.Equal(1, game.Streak);
        Assert.True(game.AnsweredCorrectly);
        Assert.True(game.ShowsAward);
        Assert.Equal(game.Score, game.LastAward);
    });

    [Fact]
    public void A_wrong_pick_costs_a_life_and_names_what_was_picked() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.For(GameMode.Normal, new GameSettings { StartingLives = 3 }));
        var picked = Wrong(game);

        game.SelectCommand.Execute(picked);

        Assert.Equal(2, game.Lives);
        Assert.Equal(0, game.Streak);
        Assert.Equal($"That was {picked.Country.Name}", game.ResultMessage);
        Assert.True(game.AnsweredWrongly);
    });

    [Fact]
    public void A_wrong_pick_reveals_the_answer_and_dims_the_rest() => _avalonia.Run(() =>
    {
        using var game = NewGame();
        var answer = Right(game);
        var picked = Wrong(game);

        game.SelectCommand.Execute(picked);

        Assert.True(answer.IsRevealed);
        Assert.True(picked.IsWrong);
        Assert.False(picked.IsDimmed);
        Assert.All(game.Options.Where(o => o != answer && o != picked), o => Assert.True(o.IsDimmed));
    });

    [Fact]
    public void Learning_takes_no_life_for_a_wrong_answer() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.Learning);

        game.SelectCommand.Execute(Wrong(game));

        Assert.Equal(0, game.Lives);
        Assert.False(game.IsGameOver);
    });

    [Fact]
    public void Answering_twice_in_one_round_changes_nothing() => _avalonia.Run(() =>
    {
        using var game = NewGame();

        game.SelectCommand.Execute(Right(game));

        var score = game.Score;
        game.SelectCommand.Execute(Wrong(game));

        Assert.Equal(score, game.Score);
        Assert.Equal(1, game.CorrectAnswers);
    });

    // ---- answering by naming ----

    [Fact]
    public void Recall_refuses_anything_that_is_not_a_country() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.Recall);

        Assert.False(game.SubmitCommand.CanExecute(null));

        game.SelectedChoice = "Atlantis";
        Assert.False(game.SubmitCommand.CanExecute(null));

        game.SelectedChoice = game.Prompt;
        Assert.True(game.SubmitCommand.CanExecute(null));
    });

    [Fact]
    public void Recall_names_the_right_country_back_after_a_miss() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.Recall);
        var answer = game.Prompt;

        game.SelectedChoice = game.Choices.First(name => name != answer);
        game.SubmitCommand.Execute(null);

        Assert.Equal($"That was {answer}", game.ResultMessage);
        Assert.Equal(2, game.Lives);
        Assert.True(game.ShowsAnswerName);
    });

    [Fact]
    public void Recall_scores_a_named_country() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.Recall);

        game.SelectedChoice = game.Prompt;
        game.SubmitCommand.Execute(null);

        Assert.True(game.Score > 0);
        Assert.True(game.AnsweredCorrectly);
    });

    // ---- what a run leaves behind ----

    [Fact]
    public void Every_answer_is_filed_against_the_flag_it_was_about() => _avalonia.Run(() =>
    {
        var history = new PlayerHistory(poolSize: 197);
        using var game = NewGame(history: history);

        var code = Right(game).Country.Code;
        game.SelectCommand.Execute(Right(game));

        Assert.Equal(1, history.For(code).Seen);
        Assert.Equal(1, history.For(code).Correct);
        Assert.True(history.BoxOf(code) >= LearningRules.FirstBox);
    });

    [Fact]
    public void Hard_records_progress_even_though_it_does_not_show_it() => _avalonia.Run(() =>
    {
        var history = new PlayerHistory(poolSize: 197);
        using var game = NewGame(DifficultyProfile.Hard, history: history);

        game.SelectCommand.Execute(Right(game));

        Assert.False(game.ShowsMastery);
        Assert.Single(history.Flags);
    });

    [Fact]
    public void A_best_score_is_stored_under_its_own_mode() => _avalonia.Run(() =>
    {
        var scores = NewScores();
        using var game = NewGame(DifficultyProfile.Hard, scores: scores);

        game.SelectCommand.Execute(Right(game));

        Assert.Equal(game.Score, scores.LoadBestScore(MiniGame.Flags, GameMode.Hard));
        Assert.Equal(0, scores.LoadBestScore(MiniGame.Flags, GameMode.Normal));
        Assert.True(game.IsNewBest);
    });

    // ---- the round lifecycle ----

    [Fact]
    public void The_next_round_follows_the_reveal() => _avalonia.Run(() =>
    {
        using var game = NewGame();

        game.SelectCommand.Execute(Right(game));
        Assert.True(game.IsRevealing);

        game.CompleteReveal();

        Assert.Equal(2, game.RoundsPlayed);
        Assert.False(game.AnsweredCorrectly);
        Assert.False(game.ShowsAward);
        Assert.Equal(string.Empty, game.ResultMessage);
    });

    [Fact]
    public void Running_out_of_lives_ends_the_run() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.For(GameMode.Normal, new GameSettings { StartingLives = 1 }));

        game.SelectCommand.Execute(Wrong(game));
        game.CompleteReveal();

        Assert.Equal(0, game.Lives);
        Assert.Empty(game.Options);
        Assert.Equal("Run over", game.EndTitle);
    });

    [Fact]
    public void A_bounded_session_stops_at_its_round_limit() => _avalonia.Run(() =>
    {
        using var game = NewGame(DifficultyProfile.Learning with { RoundLimit = 3 });

        for (var round = 0; round < 3 && !game.IsGameOver; round++)
        {
            game.SelectCommand.Execute(Right(game));
            game.CompleteReveal();
        }

        Assert.True(game.IsGameOver);
        Assert.Equal("Session complete", game.EndTitle);
        Assert.Equal(3, game.CorrectAnswers);
    });

    [Fact]
    public void Starting_again_clears_the_board_but_not_the_best() => _avalonia.Run(() =>
    {
        using var game = NewGame();

        game.SelectCommand.Execute(Right(game));
        var best = game.BestScore;

        game.StartNewGameCommand.Execute(null);

        Assert.Equal(0, game.Score);
        Assert.Equal(0, game.Streak);
        Assert.Equal(0, game.CorrectAnswers);
        Assert.Equal(1, game.RoundsPlayed);
        Assert.False(game.IsGameOver);
        Assert.Equal(best, game.BestScore);
    });

    [Fact]
    public void Leaving_a_run_asks_the_shell_rather_than_navigating() => _avalonia.Run(() =>
    {
        using var game = NewGame();
        var asked = 0;

        game.MenuRequested += (_, _) => asked++;
        game.ReturnToMenuCommand.Execute(null);

        Assert.Equal(1, asked);
    });

    private sealed class NoImages : ICountryArtwork
    {
        public object? For(string code) => null;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_temp))
            {
                Directory.Delete(_temp, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup of a temp directory.
        }
    }
}
