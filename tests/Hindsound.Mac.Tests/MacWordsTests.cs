using Hindsound.App;
using Hindsound.Export;
using Hindsound.Ui;

namespace Hindsound.Mac.Tests;

/// <summary>The words of macOS for the keys: the modifiers of a shortcut and the keys that paste. On any system.</summary>
public sealed class MacWordsTests
{
    private static readonly TimeSpan Thirty = TimeSpan.FromSeconds(30);

    private static PlatformFeatures Mac => PlatformFeatures.Mac(static () => null);

    private static ClipResult Clip(bool wav = false) => new(["clip.mp3"], Thirty, 1, false, [0.5f, 0.5f], false, wav);

    [Theory]
    [InlineData("Ctrl+Alt+Shift+Win+F10", "Control+Option+Shift+Cmd+F10")]
    [InlineData("Win+Shift+S", "Cmd+Shift+S")]
    [InlineData("Alt+Delete", "Option+Delete")]
    [InlineData("Ctrl+Shift", "Control+Shift")]
    [InlineData("F9", "F9")]
    [InlineData("", "")]
    public void On_macOS_each_modifier_has_its_Mac_name_and_the_key_stays(string gesture, string expected) =>
        Assert.Equal(expected, Mac.KeyText(gesture));

    [Fact]
    public void On_macOS_the_keys_that_paste_are_Command_and_V()
    {
        Assert.Equal("Cmd+V", Mac.KeyText(Mac.PasteGesture));
        Assert.Equal(["Cmd", "V"], Mac.PasteKeys);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Shift+Win+F10")]
    [InlineData("Win+Shift+S")]
    [InlineData("Ctrl+Shift")]
    public void Windows_and_Linux_show_the_gesture_as_it_is(string gesture)
    {
        Assert.Same(gesture, PlatformFeatures.Windows.KeyText(gesture));
        Assert.Same(gesture, PlatformFeatures.Linux(static () => ShortcutMode.Capture).KeyText(gesture));
        Assert.Equal("Ctrl+V", PlatformFeatures.Windows.PasteGesture);
        Assert.Equal(["Ctrl", "V"], PlatformFeatures.Linux(static () => ShortcutMode.Capture).PasteKeys);
    }

    [Fact]
    public void The_notification_of_a_clip_on_macOS_says_Command_V_and_still_says_paste()
    {
        var saved = NotificationStates.ForClip(Clip(), Thirty, true, true, 0x00FF00, Mac);
        var wav = NotificationStates.ForClip(Clip(wav: true), Thirty, true, true, 0x00FF00, Mac);

        Assert.Equal(["Cmd", "V"], saved.Keys);
        Assert.Equal(["Cmd", "V"], wav.Keys);
        Assert.True(NotificationStates.SaysPaste(saved));
        Assert.True(NotificationStates.SaysPaste(wav));
    }

    [Fact]
    public void The_notification_of_a_clip_on_Windows_keeps_Ctrl_V_and_one_not_copied_does_not_say_paste()
    {
        var windows = NotificationStates.ForClip(Clip(), Thirty, true, true, 0x00FF00, PlatformFeatures.Windows);
        var notCopied = NotificationStates.ForClip(Clip(), Thirty, true, false, 0x00FF00, Mac);

        Assert.Equal(["Ctrl", "V"], windows.Keys);
        Assert.True(NotificationStates.SaysPaste(windows));
        Assert.Empty(notCopied.Keys);
        Assert.False(NotificationStates.SaysPaste(notCopied));
    }

    [Fact]
    public void The_notification_of_the_capture_names_the_keys_of_the_shortcut_as_the_system_does()
    {
        Assert.Equal(["Control", "Shift", "S"], NotificationStates.CaptureOn(300, "Ctrl+Shift+S", features: Mac).Keys);
        Assert.Equal(["Option", "Cmd", "F10"], NotificationStates.CaptureOn(300, "Alt+Win+F10", features: Mac).Keys);
        Assert.Equal(["Ctrl", "Shift", "S"], NotificationStates.CaptureOn(300, "Ctrl+Shift+S", features: PlatformFeatures.Windows).Keys);
        Assert.Empty(NotificationStates.CaptureOn(300, null, features: Mac).Keys);
    }
}
