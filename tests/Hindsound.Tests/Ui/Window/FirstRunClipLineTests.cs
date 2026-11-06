using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The card of the first launch, once the first clip is saved: it says to paste only if the clip can be pasted.</summary>
public sealed class FirstRunClipLineTests : IDisposable
{
    private const string Paste = "Now paste it anywhere.";
    private const uint Accent = 0xD4F53C;
    private static readonly TimeSpan Length = TimeSpan.FromSeconds(30);

    private readonly ScreenRig _rig = new(firstRun: true);

    public FirstRunClipLineTests()
    {
        _rig.FocusOn("run.next");
        _rig.Press(Vk.Enter, Vk.Enter);
        Assert.Equal(2, _rig.Screen.FirstRunStep);
    }

    public void Dispose() => _rig.Dispose();

    private static ClipResult Clip(bool silent = false, bool rescued = false, bool wav = false) =>
        new(["clip"], Length, 1000, silent, [0.5f], rescued, wav);

    private List<string> CardAfter(ClipResult clip, bool copyWanted, bool copied)
    {
        _rig.Screen.SetLastClip(new LastClipInfo(Length, DateTime.Now, 1000) { Notice = NotificationStates.ForClip(clip, Length, copyWanted, copied, Accent) });
        var canvas = new FakeCanvas();
        _rig.Screen.Paint(canvas);
        return canvas.Drawn;
    }

    [Fact]
    public void A_clip_copied_says_to_paste()
    {
        Assert.Contains(Paste, CardAfter(Clip(), copyWanted: true, copied: true));
    }

    [Fact]
    public void A_copy_that_failed_says_so()
    {
        var card = CardAfter(Clip(), copyWanted: true, copied: false);

        Assert.DoesNotContain(Paste, card);
        Assert.Contains("Couldn't copy. Use tray > Copy last clip.", card);
    }

    [Fact]
    public void A_copy_turned_off_says_where_the_clip_is()
    {
        var card = CardAfter(Clip(), copyWanted: false, copied: false);

        Assert.DoesNotContain(Paste, card);
        Assert.Contains("In your clips folder.", card);
    }

    [Fact]
    public void A_silent_clip_says_it_is_silent()
    {
        var card = CardAfter(Clip(silent: true), copyWanted: true, copied: true);

        Assert.DoesNotContain(Paste, card);
        Assert.Contains("It's silent. Check Settings > Audio.", card);
    }

    [Fact]
    public void A_wav_copied_can_be_pasted_and_one_not_copied_cannot()
    {
        Assert.Contains(Paste, CardAfter(Clip(wav: true), copyWanted: true, copied: true));
        Assert.DoesNotContain(Paste, CardAfter(Clip(wav: true), copyWanted: false, copied: false));
    }

    [Fact]
    public void Copy_last_clip_that_works_afterwards_changes_what_it_says()
    {
        CardAfter(Clip(), copyWanted: true, copied: false);

        Assert.Contains(Paste, CardAfter(Clip(), copyWanted: true, copied: true));
    }

    [Fact]
    public void A_clip_that_is_gone_says_nothing_about_it()
    {
        CardAfter(Clip(), copyWanted: true, copied: true);

        _rig.Screen.SetLastClip(null);
        var canvas = new FakeCanvas();
        _rig.Screen.Paint(canvas);

        Assert.DoesNotContain(Paste, canvas.Drawn);
    }
}
