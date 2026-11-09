using System.Collections.Concurrent;
using System.IO;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// The three places where the window and the Library meet in the app: a clip that ends, the window back at the front, and the
/// window closing, which gives the memory back. The app is built and never run; its timer and its collections are replaced.
/// </summary>
public sealed class AppWindowClosingTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
    private readonly List<(int Ms, Action Work)> _later = [];
    private readonly ConcurrentQueue<GCCollectionMode> _collections = new();
    private int _foldersAsked;
    private int _choicesListed;

    public void Dispose()
    {
        _rig.Dispose();
        _temp.Dispose();
    }

    private string Clips => _temp.Combine("clips");

    private HindsoundApp Built()
    {
        var options = AppOptions.Parse(["--settings", _temp.Combine("settings.json"), "--no-mic", "--offscreen"]);
        Directory.CreateDirectory(Clips);
        File.WriteAllBytes(Path.Combine(Clips, "a.mp3"), [1, 2, 3]);
        var app = new HindsoundApp(options, new Journal(_temp.Combine("settings.log")))
        {
            OpenInstead = _ => { },
            PostInstead = _loop.Enqueue,
            AfterInstead = (ms, work) => _later.Add((ms, work)),
            CollectInstead = _collections.Enqueue,
            LoadChoicesInstead = () => _choicesListed++,
            LibraryFoldersInstead = () =>
            {
                _foldersAsked++;
                return new ClipScanRequest { OutputFolder = Clips, RescueFolder = _temp.Combine("rescued") };
            },
        };
        app.WireScreen(_rig.Screen);
        _rig.Screen.UpdateStatus(ScreenRig.Capturing());
        return app;
    }

    private void Turn(Func<bool> until)
    {
        var end = Environment.TickCount64 + 20_000;
        while (!until())
        {
            if (_loop.TryDequeue(out var work))
            {
                work();
            }
            else if (Environment.TickCount64 > end)
            {
                Assert.Fail("the loop never got there");
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    private void ShowAndRead(HindsoundApp app)
    {
        _rig.Screen.ShowSection(Section.Library);
        Turn(() => Shows("a"));
    }

    private bool Shows(string text)
    {
        _rig.Canvas.Drawn.Clear();
        _rig.Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn.Contains(text);
    }

    [Fact]
    public void Closing_the_window_drops_its_library_so_that_no_clip_looks_again()
    {
        var app = Built();
        ShowAndRead(app);
        Assert.True(app.HasLibrary);

        app.OnSettingsClosed();
        var asked = _foldersAsked;
        app.OnClipEnded();
        app.OnWindowActivated();

        Assert.False(app.HasLibrary);
        Assert.Equal(asked, _foldersAsked);
    }

    [Fact]
    public void Closing_the_window_gives_the_memory_back_after_a_moment_with_a_collection_that_decommits()
    {
        var app = Built();
        ShowAndRead(app);

        app.OnSettingsClosed();

        var release = Assert.Single(_later, step => step.Ms == 500);
        Assert.Empty(_collections);
        release.Work();
        Turn(() => _collections.Count >= 2);
        Assert.Equal([GCCollectionMode.Forced, GCCollectionMode.Aggressive], _collections.ToArray());
    }

    [Fact]
    public void A_look_still_running_when_the_window_closes_gives_the_memory_back_once_when_it_ends()
    {
        var app = Built();
        _rig.Screen.ShowSection(Section.Library);

        // Closed with the reading on its way: nothing has been collected yet.
        app.OnSettingsClosed();
        Assert.Empty(_collections);

        Turn(() => _collections.Count >= 2);
        Thread.Sleep(100);
        Assert.Equal([GCCollectionMode.Forced, GCCollectionMode.Aggressive], _collections.ToArray());
    }

    [Fact]
    public void The_window_back_at_the_front_reads_the_settings_and_the_choices_and_looks_at_the_clips()
    {
        var app = Built();
        ShowAndRead(app);
        File.WriteAllBytes(Path.Combine(Clips, "b.mp3"), [1, 2, 3]);
        var asked = _foldersAsked;

        app.OnWindowActivated();

        Assert.Equal(1, _choicesListed);
        Turn(() => _foldersAsked > asked);
        Turn(() => Shows("b"));
    }

    [Fact]
    public void A_clip_that_ends_looks_at_the_clips_and_leaves_its_memory_for_a_moment_later()
    {
        var app = Built();
        ShowAndRead(app);
        var asked = _foldersAsked;

        app.OnClipEnded();

        Assert.Contains(_later, step => step.Ms == 500);
        Turn(() => _foldersAsked > asked);
    }
}
