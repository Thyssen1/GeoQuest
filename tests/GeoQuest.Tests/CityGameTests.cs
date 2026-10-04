using GeoQuest.Models;
using GeoQuest.Services;
using GeoQuest.ViewModels;

namespace GeoQuest.Tests;

/// <summary>
/// A city run end to end: what a click does, what a miss costs, and what the player is
/// shown afterwards. The pin is the one answer with no option to mark right or wrong, so
/// the round lifecycle has a second path through it that nothing else covers.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public class CityGameTests : IDisposable
{
    private readonly HeadlessSession _avalonia;

    private readonly string _temp =
        Path.Combine(Path.GetTempPath(), "GeoQuestTests", Guid.NewGuid().ToString("N"));

    public CityGameTests(HeadlessSession avalonia) => _avalonia = avalonia;

    private GameViewModel NewGame(GameMode mode = GameMode.Normal, int seed = 20260423)
    {
        using var stream = File.OpenRead(TestPaths.CountriesJson);
        var repository = JsonCountryRepository.Load(stream);

        return new GameViewModel(
            new RandomQuestionGenerator(repository, new Random(seed)),
            artwork: null,
            new FileScoreStore(Path.Combine(_temp, Guid.NewGuid().ToString("N") + ".json")),
            DifficultyProfile.For(MiniGame.Cities, mode),
            sounds: null,
            random: new Random(seed),
            history: new PlayerHistory(poolSize: 197),
            choices: repository.QuestionPool,
            cities: new CityLoader());
    }

    [Fact]
    public void A_city_round_shows_a_map_and_asks_for_a_capital() => _avalonia.Run(() =>
    {
        using var game = NewGame();

        Assert.True(game.IsPinInput);
        Assert.True(game.ShowsMap);
        Assert.False(game.ShowsGrid);
        Assert.False(game.ShowsRecall);
        Assert.NotNull(game.WorldGeometry);
        Assert.NotNull(game.CurrentCapital);
        Assert.Equal(game.CurrentCapital!.Name, game.Prompt);
        Assert.Contains("Click the map", game.AnswerKeysText, StringComparison.Ordinal);
    });

    [Fact]
    public void A_pin_on_the_city_scores_and_counts() => _avalonia.Run(() =>
    {
        using var game = NewGame();
        var city = game.CurrentCapital!;

        game.DropPinCommand.Execute(city.Location);

        Assert.True(game.AnsweredCorrectly);
        Assert.True(game.Score > 0);
        Assert.Equal(1, game.CorrectAnswers);
        Assert.Equal(1, game.Streak);
        Assert.Equal(city.Location, game.DroppedPin);
        Assert.Equal(city.Location, game.AnswerPlace);
    });

    [Fact]
    public void A_pin_on_the_far_side_of_the_world_costs_a_life() => _avalonia.Run(() =>
    {
        using var game = NewGame();
        var city = game.CurrentCapital!;
        var lives = game.Lives;

        // Antipodal, so it is wrong wherever the city happens to be.
        game.DropPinCommand.Execute(new GeoPoint(-city.Location.Latitude, city.Location.Longitude - 180d));

        Assert.True(game.AnsweredWrongly);
        Assert.Equal(lives - 1, game.Lives);
        Assert.Equal(0, game.Streak);
        Assert.Contains(city.Name, game.ResultMessage, StringComparison.Ordinal);
    });

    /// <summary>A near miss is still a miss, but the player is told how near.</summary>
    [Fact]
    public void The_distance_is_reported_either_way() => _avalonia.Run(() =>
    {
        using var game = NewGame();
        var city = game.CurrentCapital!;

        game.DropPinCommand.Execute(new GeoPoint(city.Location.Latitude + 1d, city.Location.Longitude));

        Assert.True(game.LastDistanceKm > 0d);
        Assert.InRange(game.LastDistanceKm, 100d, 120d);   // a degree of latitude
        Assert.True(game.ShowsDistance);
    });

    [Fact]
    public void A_closer_pin_is_worth_more_than_a_looser_one() => _avalonia.Run(() =>
    {
        using var close = NewGame();
        using var loose = NewGame();

        var city = close.CurrentCapital!;

        close.DropPinCommand.Execute(city.Location);
        loose.DropPinCommand.Execute(new GeoPoint(city.Location.Latitude + 4d, city.Location.Longitude));

        Assert.True(loose.AnsweredCorrectly, "four degrees should still be inside the opening target");
        Assert.True(close.Score > loose.Score, $"{close.Score} should beat {loose.Score}");
    });

    [Fact]
    public void The_map_goes_dead_while_the_answer_is_showing() => _avalonia.Run(() =>
    {
        using var game = NewGame();

        Assert.True(game.IsMapLive);

        game.DropPinCommand.Execute(game.CurrentCapital!.Location);

        Assert.True(game.IsRevealing);
        Assert.False(game.IsMapLive);
    });

    [Fact]
    public void A_second_click_in_the_same_round_changes_nothing() => _avalonia.Run(() =>
    {
        using var game = NewGame();
        var city = game.CurrentCapital!;

        game.DropPinCommand.Execute(city.Location);

        var score = game.Score;
        game.DropPinCommand.Execute(new GeoPoint(0d, 0d));

        Assert.Equal(score, game.Score);
        Assert.Equal(city.Location, game.DroppedPin);
    });

    [Fact]
    public void The_next_round_clears_the_pin_and_asks_about_somewhere_else() => _avalonia.Run(() =>
    {
        using var game = NewGame();
        var first = game.CurrentCapital!.Code;

        game.DropPinCommand.Execute(game.CurrentCapital.Location);
        game.CompleteReveal();

        Assert.Null(game.DroppedPin);
        Assert.Null(game.AnswerPlace);
        Assert.False(game.ShowsDistance);
        Assert.NotEqual(first, game.CurrentCapital!.Code);
    });

    [Fact]
    public void Progress_is_recorded_against_the_country_like_every_other_game() => _avalonia.Run(() =>
    {
        using var stream = File.OpenRead(TestPaths.CountriesJson);
        var repository = JsonCountryRepository.Load(stream);
        var history = new PlayerHistory(poolSize: 197);

        using var game = new GameViewModel(
            new RandomQuestionGenerator(repository, new Random(7)),
            artwork: null,
            new FileScoreStore(Path.Combine(_temp, Guid.NewGuid().ToString("N") + ".json")),
            DifficultyProfile.For(MiniGame.Cities, GameMode.Normal),
            sounds: null,
            random: new Random(7),
            history: history,
            choices: repository.QuestionPool,
            cities: new CityLoader());

        var code = game.CurrentCapital!.Code;
        game.DropPinCommand.Execute(game.CurrentCapital.Location);

        Assert.Equal(1, history.For(code).Seen);
        Assert.Equal(1, history.For(code).Correct);
    });

    // ---- naming a marked city ----

    [Fact]
    public void Recall_marks_the_city_and_asks_for_its_name() => _avalonia.Run(() =>
    {
        using var game = NewGame(GameMode.Recall);

        Assert.True(game.IsNameInput);
        Assert.True(game.ShowsMap);
        Assert.True(game.ShowsNameEntry);
        Assert.False(game.ShowsRecall);

        // The marker is the question here, so it is up before any answer is given.
        Assert.Equal(game.CurrentCapital!.Location, game.AnswerPlace);
        Assert.Contains("Which city", game.PromptHeading, StringComparison.Ordinal);
    });

    [Fact]
    public void Recall_offers_capitals_rather_than_countries() => _avalonia.Run(() =>
    {
        using var game = NewGame(GameMode.Recall);

        Assert.Equal(197, game.Choices.Count);
        Assert.Contains("Copenhagen", game.Choices);
        Assert.Contains("Yamoussoukro", game.Choices);
        Assert.DoesNotContain("Denmark", game.Choices);
    });

    [Fact]
    public void Recall_accepts_the_right_city() => _avalonia.Run(() =>
    {
        using var game = NewGame(GameMode.Recall);

        game.SelectedChoice = game.CurrentCapital!.Name;
        game.SubmitCommand.Execute(null);

        Assert.True(game.AnsweredCorrectly);
        Assert.True(game.Score > 0);
    });

    [Fact]
    public void Recall_names_the_right_city_back_after_a_miss() => _avalonia.Run(() =>
    {
        using var game = NewGame(GameMode.Recall);
        var answer = game.CurrentCapital!.Name;

        game.SelectedChoice = game.Choices.First(name => name != answer);
        game.SubmitCommand.Execute(null);

        Assert.Equal($"That was {answer}", game.ResultMessage);
    });

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
