using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GeoQuestTests", Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public void Nothing_stored_reads_as_a_blank_slate()
    {
        var history = new FileHistoryStore(PathFor("absent.json")).Load(197);

        Assert.Empty(history.Flags);
        Assert.Equal(0, history.Graduated);
        Assert.Equal(197, history.PoolSize);
    }

    [Fact]
    public void Round_trips_what_the_player_has_shown()
    {
        var path = PathFor("history.json");
        var history = new PlayerHistory(poolSize: 197);

        for (var i = 0; i < 3; i++)
        {
            history.Record("dk", correct: true, fast: true);
        }

        history.Record("se", correct: false, fast: false);

        new FileHistoryStore(path).Save(history);

        var reloaded = new FileHistoryStore(path).Load(197);

        Assert.Equal(LearningRules.GraduatedBox, reloaded.BoxOf("dk"));
        Assert.Equal(3, reloaded.For("dk").Correct);
        Assert.Equal(LearningRules.FirstBox, reloaded.BoxOf("se"));
        Assert.Equal(0, reloaded.For("se").Correct);
    }

    [Fact]
    public void Mastery_survives_a_restart()
    {
        var path = PathFor("mastery.json");
        var history = new PlayerHistory(poolSize: 100);

        foreach (var code in new[] { "dk", "se", "no" })
        {
            for (var i = 0; i < 3; i++)
            {
                history.Record(code, correct: true, fast: true);
            }
        }

        new FileHistoryStore(path).Save(history);

        Assert.Equal(3, new FileHistoryStore(path).Load(100).MasteryPercent);
    }

    [Fact]
    public void A_corrupt_file_starts_over_rather_than_throwing()
    {
        var path = PathFor("corrupt.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "not json at all");

        Assert.Empty(new FileHistoryStore(path).Load(197).Flags);
    }

    [Fact]
    public void Saving_to_an_unwritable_path_does_not_throw()
    {
        var store = new FileHistoryStore(Path.Combine(_directory, "\0invalid", "history.json"));

        Assert.Null(Record.Exception(() => store.Save(new PlayerHistory(poolSize: 197))));
    }

    [Fact]
    public void Progress_is_nested_under_the_game_it_belongs_to()
    {
        // Knowing a country's flag is not knowing its outline; the later mini-games need
        // their own progress, and the nesting is what leaves room for it.
        var path = PathFor("nested.json");
        var history = new PlayerHistory(poolSize: 197);
        history.Record("dk", correct: true, fast: true);

        new FileHistoryStore(path).Save(history);

        Assert.Contains("\"flags\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup of a temp directory.
        }
    }
}
