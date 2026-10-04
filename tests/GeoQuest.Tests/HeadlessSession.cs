using Avalonia.Headless;
using GeoQuest;

namespace GeoQuest.Tests;

/// <summary>
/// A running Avalonia application, headless, shared by every view-model test in a class.
/// The game owns two dispatcher timers and cannot be constructed without one, which is
/// why the round lifecycle went untested for so long.
///
/// Test bodies run on its UI thread through <see cref="Run"/>: Avalonia refuses to be
/// touched from anywhere else, and xUnit runs tests on its own threads.
/// </summary>
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