using System.Collections.Concurrent;
using System.IO;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// The two links that make the Library page look again, as the app wires them: the pipeline that reports a clip ended, and the
/// real window that comes back to the front. The app is built and never run; its clips folder is a made-up one.
/// </summary>
public sealed class AppLibraryRelookTests : IDisposable
{
    private const nuint WaActive = 1;

    private readonly TempFolder _temp = new();
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();
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
        return new HindsoundApp(options, new Journal(_temp.Combine("settings.log")))
        {
            OpenInstead = _ => { },
            PostInstead = _loop.Enqueue,
            AfterInstead = (_, _) => { },
            LoadChoicesInstead = () => _choicesListed++,
            LibraryFoldersInstead = () => new ClipScanRequest { OutputFolder = Clips, RescueFolder = _temp.Combine("rescued") },
        };
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

    private bool Shows(string text)
    {
        _rig.Canvas.Drawn.Clear();
        _rig.Screen.Paint(_rig.Canvas);
        return _rig.Canvas.Drawn.Contains(text);
    }

    [Fact]
    public void A_clip_the_pipeline_reports_ended_makes_the_open_page_look_again()
    {
        var app = Built();
        app.ShowOutcomeInstead = _ => { };
        app.WireScreen(_rig.Screen);
        _rig.Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.Screen.ShowSection(Section.Library);
        Turn(() => Shows("a"));
        File.WriteAllBytes(Path.Combine(Clips, "b.mp3"), [1, 2, 3]);
        var pipeline = app.NewPipeline(work => work(), (_, _) => { });

        pipeline.Finished!(new ClipOutcome(new ClipRequest(TimeSpan.FromSeconds(1), new ExportOptions(), null, false), null, ClipFailure.NothingCaptured));

        Turn(() => Shows("b"));
    }

    [Fact]
    public void The_window_the_app_opens_looks_again_when_it_comes_back_to_the_front()
    {
        var app = Built();
        app.CreateWindow(Section.Now);
        try
        {
            var window = app.WindowNow!;
            var listed = _choicesListed;

            WindowNative.SendMessageW(window.Hwnd, WindowNative.WM_ACTIVATE, WaActive, 0);

            Assert.True(_choicesListed > listed);
        }
        finally
        {
            app.WindowNow?.Dispose();
        }
    }
}
