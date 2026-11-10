using System.Collections.Concurrent;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>A clip this run may not play is told on the loop like any state of a preview, and a window that has gone is told nothing.</summary>
public sealed class WindowLibraryRefusalTests : IDisposable
{
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly ConcurrentQueue<string> _journal = new();
    private WindowLibrary? _panel;

    public void Dispose()
    {
        _panel?.Close();
        _rig.Dispose();
    }

    private WindowLibrary Panel()
    {
        _panel = new WindowLibrary(
            _rig.Screen,
            new LibraryStore(Path.Combine(Path.GetTempPath(), "hindsound-none", "settings.json"), _journal.Enqueue),
            () => new ClipScanRequest(),
            _loop.Enqueue,
            new OffscreenFolderShell(_journal.Enqueue),
            _ => true,
            _ => true,
            (attempt, done) => done(attempt()),
            _journal.Enqueue,
            player: () => throw new InvalidOperationException("a refused clip never reaches a player"),
            mayPlay: _ => false);
        return _panel;
    }

    private void PumpAll()
    {
        while (_loop.TryDequeue(out var work))
        {
            work();
        }
    }

    [Fact]
    public void A_refusal_on_its_way_when_the_window_goes_reaches_nobody()
    {
        var panel = Panel();
        panel.Play(new ClipGroup([new ClipFile(@"C:\Clips\dogs.mp3", 1000, default)]));

        panel.Close();
        PumpAll();

        Assert.Equal(PreviewState.Stopped, _rig.Screen.Preview.State);
    }
}
