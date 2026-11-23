using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Input;

namespace Hindsound.Tests.App;

/// <summary>The shortcut of "Save and edit" in the app run whole: what a press asks of the clip path.</summary>
public sealed class SaveAndEditHotkeyRunTests
{
    private sealed class EmptyBackend : IClipBackend
    {
        public AudioSnapshot Snapshot(TimeSpan duration) => new([], []);

        public ClipResult Save(AudioSnapshot snapshot, ExportOptions options, string? appName) => throw new InvalidOperationException();
    }

    private static ClipRequest? PressedFor(HotkeyAction action)
    {
        using var rig = new ShellRunRig();
        ClipRequest? seen = null;
        rig.Run(run =>
        {
            using var done = new ManualResetEventSlim();
            run.App!.UsePipeline(new ClipPipeline(new EmptyBackend(), work => work(), (_, _) => { }, _ => { })
            {
                Finished = outcome =>
                {
                    seen = outcome.Request;
                    done.Set();
                },
            });

            run.App.OnHotkey(action);

            Assert.True(done.Wait(TimeSpan.FromSeconds(20)));
        });
        return seen;
    }

    [Fact]
    public void Save_and_edit_asks_for_the_length_of_Save_clip_and_for_the_editor_after()
    {
        var request = PressedFor(HotkeyAction.SaveAndEdit);

        Assert.NotNull(request);
        Assert.True(request.EditAfter);
        Assert.Equal(TimeSpan.FromSeconds(30), request.Requested);
    }

    [Fact]
    public void Save_clip_asks_for_the_same_length_and_never_for_the_editor()
    {
        var request = PressedFor(HotkeyAction.SaveDefault);

        Assert.NotNull(request);
        Assert.False(request.EditAfter);
        Assert.Equal(TimeSpan.FromSeconds(30), request.Requested);
    }
}
