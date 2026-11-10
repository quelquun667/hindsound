using Hindsound.Linux.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>Which layer the session gets, and the notice shown once. Pure logic: runs everywhere.</summary>
public sealed class HotkeyChoiceTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void A_wayland_session_is_known_by_its_socket_or_by_its_type()
    {
        Assert.True(HotkeyChoice.IsWayland(Env(("WAYLAND_DISPLAY", "wayland-0"))));
        Assert.True(HotkeyChoice.IsWayland(Env(("XDG_SESSION_TYPE", "Wayland"))));
        Assert.False(HotkeyChoice.IsWayland(Env(("XDG_SESSION_TYPE", "x11"), ("DISPLAY", ":0"))));
        Assert.False(HotkeyChoice.IsWayland(Env(("WAYLAND_DISPLAY", ""))));
        Assert.True(HotkeyChoice.HasXDisplay(Env(("DISPLAY", ":0"))));
        Assert.False(HotkeyChoice.HasXDisplay(Env()));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void X11_comes_first_only_in_an_x11_session(bool wayland, bool xDisplay, bool x11First)
    {
        Assert.Equal(x11First ? HotkeyLayer.X11 : HotkeyLayer.Portal, HotkeyChoice.First(wayland, xDisplay));
    }

    [Fact]
    public void The_marker_sits_in_the_config_folder_of_the_app()
    {
        Assert.Equal("/home/someone/.config/Hindsound/no-global-shortcut-notice", HotkeyChoice.NoticeMarkerPath(Env(("HOME", "/home/someone/"))));
        Assert.Equal("/cfg/Hindsound/no-global-shortcut-notice", HotkeyChoice.NoticeMarkerPath(Env(("HOME", "/home/someone"), ("XDG_CONFIG_HOME", "/cfg/"))));

        // A relative XDG_CONFIG_HOME is not to be used.
        Assert.Equal("/home/someone/.config/Hindsound/no-global-shortcut-notice", HotkeyChoice.NoticeMarkerPath(Env(("HOME", "/home/someone"), ("XDG_CONFIG_HOME", "cfg"))));
        Assert.Null(HotkeyChoice.NoticeMarkerPath(Env()));
        Assert.Null(HotkeyChoice.NoticeMarkerPath(Env(("HOME", "relative"))));
    }

    [Fact]
    public void The_notice_is_claimed_once_then_never_again()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hindsound-notice-" + Guid.NewGuid().ToString("N"));
        try
        {
            var marker = Path.Combine(folder, "Hindsound", HotkeyChoice.NoticeMarkerName);
            var log = new List<string>();

            Assert.True(HotkeyChoice.ClaimNotice(marker, log.Add));
            Assert.True(File.Exists(marker));
            Assert.False(HotkeyChoice.ClaimNotice(marker, log.Add));
            Assert.Empty(log);

            // Without a home, nothing remembers it: the notice still goes out.
            Assert.True(HotkeyChoice.ClaimNotice(null, log.Add));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void A_marker_that_cannot_be_written_is_logged_and_the_notice_goes_out()
    {
        var file = Path.GetTempFileName();
        try
        {
            // A file where the folder should be.
            var marker = Path.Combine(file, HotkeyChoice.NoticeMarkerName);
            var log = new List<string>();

            Assert.True(HotkeyChoice.ClaimNotice(marker, log.Add));
            Assert.Single(log);
            Assert.StartsWith("hotkeys: the notice marker could not be written: ", log[0]);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
