using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>What <c>{app}</c> says in a clip's name: the one program recorded alone, or else the window in front.</summary>
public sealed class ClipAppNameTests : IDisposable
{
    private readonly ShellRunRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void One_program_recorded_alone_gives_the_name_the_list_shows_for_it()
    {
        Assert.Equal("Google Chrome", ClipAppName.For("chrome.exe", "Google Chrome", "Notepad"));
    }

    [Fact]
    public void One_program_with_no_name_in_the_list_gives_its_executable_without_exe()
    {
        Assert.Equal("Discord", ClipAppName.For("Discord.exe", null, "Notepad"));
        Assert.Equal("Discord", ClipAppName.For("Discord.EXE", "  ", "Notepad"));
        Assert.Equal("tool", ClipAppName.For("tool", null, "Notepad"));
    }

    [Fact]
    public void No_program_recorded_alone_gives_the_window_in_front()
    {
        Assert.Equal("Notepad", ClipAppName.For(null, "Google Chrome", "Notepad"));
        Assert.Equal("Notepad", ClipAppName.For("", "Google Chrome", "Notepad"));
        Assert.Equal("Notepad", ClipAppName.For("   ", null, "Notepad"));
        Assert.Null(ClipAppName.For(null, null, null));
    }

    [Fact]
    public void An_executable_that_is_only_the_suffix_falls_back_to_the_window_in_front()
    {
        Assert.Equal("Notepad", ClipAppName.For(".exe", null, "Notepad"));
    }

    [Fact]
    public void The_app_gives_one_name_to_the_shortcut_and_to_the_menu()
    {
        _rig.SourceApp = "Discord.exe";
        string? shortcut = null, menu = null;

        _rig.Run(rig =>
        {
            rig.Screens.App = "game.exe";
            shortcut = rig.App!.RecordedAppName(rig.Screens.Foreground().AppName);

            rig.Shell.Tray.Gesture!(TrayGesture.Menu);
            rig.Screens.App = "other.exe";
            menu = rig.App.RecordedAppName("game.exe");
        });

        Assert.Equal("Discord", shortcut);
        Assert.Equal("Discord", menu);
    }

    [Fact]
    public void Without_a_program_recorded_alone_the_app_keeps_the_window_in_front()
    {
        string? name = null;

        _rig.Run(rig => name = rig.App!.RecordedAppName("game.exe"));

        Assert.Equal("game.exe", name);
    }

    [Fact]
    public void Two_names_of_one_program_differ_only_by_case()
    {
        Assert.True(ClipAppName.SameProgram("Discord.exe", "discord.EXE"));
        Assert.False(ClipAppName.SameProgram("Discord.exe", "chrome.exe"));
    }
}
