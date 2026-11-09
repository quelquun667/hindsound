using System.Collections.Concurrent;
using System.IO;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>The star and the collections with the app built and never run: its own settings file, made-up clips, a loop turned by hand.</summary>
public sealed class AppCollectionsWiringTests : IDisposable
{
    private const string Canary = "Collection-Canary-7F3";

    private readonly TempFolder _temp = new();
    private readonly ScreenRig _rig = new();
    private readonly ConcurrentQueue<Action> _loop = new();

    public void Dispose()
    {
        _rig.Dispose();
        _temp.Dispose();
    }

    private string Clips => _temp.Combine("clips");

    private string LogFile => _temp.Combine("settings.log");

    private HindsoundApp Built()
    {
        var options = AppOptions.Parse(["--settings", _temp.Combine("settings.json"), "--no-mic", "--offscreen"]);
        Directory.CreateDirectory(Clips);
        File.WriteAllBytes(Path.Combine(Clips, "dogs.mp3"), [1, 2, 3]);
        return new HindsoundApp(options, new Journal(LogFile))
        {
            OpenInstead = _ => { },
            PostInstead = _loop.Enqueue,
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

    private WindowLibrary OpenPage(HindsoundApp app)
    {
        app.WireScreen(_rig.Screen);
        _rig.Screen.UpdateStatus(ScreenRig.Capturing());
        _rig.Screen.ShowSection(Section.Library);
        var panel = app.LibraryPanel!;
        Turn(() => panel.Lines is not null && !panel.IsWorking && _loop.IsEmpty);
        return panel;
    }

    [Fact]
    public void The_star_key_on_the_chosen_clip_writes_the_index_beside_the_settings_file_of_the_run()
    {
        var app = Built();
        var panel = OpenPage(app);
        _rig.Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));
        _rig.FocusOn(SettingsScreen.ClipListId);

        _rig.Press(RowList.StarKey);
        Turn(() => panel.Index is { } index && index.Clips.Exists(clip => clip.Favorite) && _loop.IsEmpty);

        Assert.True(File.Exists(_temp.Combine("library.json")));
        Assert.True(panel.LabelsOf(panel.Lines!.Lines[0]).Favorite);
        Assert.Equal(LibraryStatus.Loaded, panel.IndexStatus);
    }

    [Fact]
    public void A_collection_named_as_the_canary_is_made_and_deleted_and_never_reaches_the_log()
    {
        var app = Built();
        var panel = OpenPage(app);
        _rig.Screen.SelectClip(Path.Combine(Clips, "dogs.mp3"));

        panel.CreateCollection(Canary);
        Turn(() => panel.Index!.Collections.Count == 1 && _loop.IsEmpty);
        var id = panel.Index!.Collections[0].Id;
        panel.SetIn(panel.Lines!.Lines[0], id, true);
        panel.RenameCollection(id, Canary + " too");
        panel.DeleteCollection(id);
        Turn(() => panel.Index!.Collections.Count == 0 && _loop.IsEmpty && !panel.IsWorking);

        // A deleted name stays nowhere: not in the index, which keeps no copy of a sound file.
        Assert.DoesNotContain("Canary", string.Concat(Directory.GetFiles(_temp.Path, "library.json*").Select(File.ReadAllText)), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(LogFile) && File.ReadAllText(LogFile).Contains("Canary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_window_closed_while_an_action_is_on_its_way_still_has_it_written()
    {
        var app = Built();
        var panel = OpenPage(app);

        panel.CreateCollection("pets");
        app.LibraryWindowClosed();
        Turn(() => _loop.IsEmpty && File.Exists(_temp.Combine("library.json")) && File.ReadAllText(_temp.Combine("library.json")).Contains("pets", StringComparison.Ordinal));

        Assert.False(app.HasLibrary);
        Assert.Empty(panel.Index!.Collections);
    }
}
