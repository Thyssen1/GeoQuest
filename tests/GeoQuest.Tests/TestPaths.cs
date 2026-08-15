namespace GeoQuest.Tests;

/// <summary>
/// Locates repository assets from the test assembly. countries.json is copied to the
/// output directory, but the flag images are not — copying 255 files per test run would
/// be wasteful — so the flags directory is found by walking up to the repository root.
/// </summary>
internal static class TestPaths
{
    public static string CountriesJson => Path.Combine(AppContext.BaseDirectory, "countries.json");

    public static string FlagsDirectory { get; } = LocateFlags();

    private static string LocateFlags()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Assets", "Flags");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate Assets/Flags by walking up from {AppContext.BaseDirectory}.");
    }
}
