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

        Assert.Equal(0, store.LoadBestScore());
    }

    [Fact]
    public void Round_trips_a_score()
    {
        var path = PathFor("scores.json");

        new FileScoreStore(path).SaveBestScore(1234);

        Assert.Equal(1234, new FileScoreStore(path).LoadBestScore());
    }

    [Fact]
    public void Creates_missing_directories()
    {
        var path = Path.Combine(_directory, "nested", "deeper", "scores.json");

        new FileScoreStore(path).SaveBestScore(7);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_corrupt_file_reads_as_zero_rather_than_throwing()
    {
        var path = PathFor("corrupt.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "this is not json");

        Assert.Equal(0, new FileScoreStore(path).LoadBestScore());
    }

    [Fact]
    public void A_negative_stored_value_is_not_trusted()
    {
        var path = PathFor("negative.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """{"BestScore":-500}""");

        Assert.Equal(0, new FileScoreStore(path).LoadBestScore());
    }

    [Fact]
    public void Saving_to_an_unwritable_path_does_not_throw()
    {
        // Losing a high score must never interrupt a run in progress.
        var store = new FileScoreStore(Path.Combine(_directory, "\0invalid", "scores.json"));

        var exception = Record.Exception(() => store.SaveBestScore(42));

        Assert.Null(exception);
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
