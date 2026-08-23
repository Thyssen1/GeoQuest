using GeoQuest.Models;
using GeoQuest.Services;

namespace GeoQuest.Tests;

public class ScoreStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GeoQuestTests", Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public void Reports_zero_when_nothing_has_been_stored()
    {
        var store = new FileScoreStore(PathFor("absent.json"));

        Assert.Equal(0, store.LoadBestScore(GameMode.Normal));
    }

    [Fact]
    public void Round_trips_a_score()
    {
        var path = PathFor("scores.json");

        new FileScoreStore(path).SaveBestScore(GameMode.Normal, 1234);

        Assert.Equal(1234, new FileScoreStore(path).LoadBestScore(GameMode.Normal));
    }

    [Fact]
    public void Creates_missing_directories()
    {
        var path = Path.Combine(_directory, "nested", "deeper", "scores.json");

        new FileScoreStore(path).SaveBestScore(GameMode.Normal, 7);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_corrupt_file_reads_as_zero_rather_than_throwing()
    {
        var path = PathFor("corrupt.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "this is not json");

        Assert.Equal(0, new FileScoreStore(path).LoadBestScore(GameMode.Normal));
    }

    [Fact]
    public void A_negative_stored_value_is_not_trusted()
    {
        var path = PathFor("negative.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """{"BestScore":-500}""");

        Assert.Equal(0, new FileScoreStore(path).LoadBestScore(GameMode.Normal));
    }

    [Fact]
    public void Saving_to_an_unwritable_path_does_not_throw()
    {
        // Losing a high score must never interrupt a run in progress.
        var store = new FileScoreStore(Path.Combine(_directory, "\0invalid", "scores.json"));

        var exception = Record.Exception(() => store.SaveBestScore(GameMode.Normal, 42));

        Assert.Null(exception);
    }

    [Fact]
    public void Each_mode_keeps_its_own_best()
    {
        var path = PathFor("modes.json");
        var store = new FileScoreStore(path);

        store.SaveBestScore(GameMode.Normal, 2400);
        store.SaveBestScore(GameMode.Hard, 900);

        Assert.Equal(2400, store.LoadBestScore(GameMode.Normal));
        Assert.Equal(900, store.LoadBestScore(GameMode.Hard));
        Assert.Equal(0, store.LoadBestScore(GameMode.Learning));
    }

    [Fact]
    public void A_file_written_before_modes_existed_reads_as_the_normal_best()
    {
        var path = PathFor("legacy.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """{"BestScore":2376}""");

        var store = new FileScoreStore(path);

        Assert.Equal(2376, store.LoadBestScore(GameMode.Normal));
        Assert.Equal(0, store.LoadBestScore(GameMode.Hard));
    }

    [Fact]
    public void Recording_a_new_mode_does_not_discard_the_legacy_score()
    {
        // The upgrade path everyone with a saved score will take: the first Hard run must
        // not cost them the Normal best they arrived with.
        var path = PathFor("upgrade.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """{"BestScore":2376}""");

        new FileScoreStore(path).SaveBestScore(GameMode.Hard, 40);

        var reloaded = new FileScoreStore(path);

        Assert.Equal(2376, reloaded.LoadBestScore(GameMode.Normal));
        Assert.Equal(40, reloaded.LoadBestScore(GameMode.Hard));
    }

    [Fact]
    public void Resetting_clears_every_mode()
    {
        var path = PathFor("reset.json");
        var store = new FileScoreStore(path);

        store.SaveBestScore(GameMode.Normal, 100);
        store.SaveBestScore(GameMode.Learning, 200);
        store.SaveBestScore(GameMode.Hard, 300);

        store.ClearBestScores();

        Assert.All(Enum.GetValues<GameMode>(), mode => Assert.Equal(0, store.LoadBestScore(mode)));
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
