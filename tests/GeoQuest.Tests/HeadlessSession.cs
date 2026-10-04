using Avalonia.Headless;

namespace GeoQuest.Tests;

/// <summary>
/// A running Avalonia application, headless, shared by every test that needs one.
/// The game owns two dispatcher timers and cannot be constructed without one, which is
/// why the round lifecycle went untested for so long.
///
/// Test bodies run on its UI thread through <see cref="Run"/>: Avalonia refuses to be
/// touched from anywhere else, and xUnit runs tests on its own threads.
/// </summary>
/// <summary>
/// Binds every test class that needs Avalonia to one shared session, and stops xUnit
/// running them in parallel.
///
/// Avalonia allows one application per process, and <c>IClassFixture</c> would build a
/// separate one per class. With two such classes that mostly got away with it; the third
/// crashed the test host partway through a run, while each class still passed on its own.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaCollection : ICollectionFixture<HeadlessSession>
{
    public const string Name = "avalonia";
}

public sealed class HeadlessSession : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        _session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose() => _session.Dispose();
}