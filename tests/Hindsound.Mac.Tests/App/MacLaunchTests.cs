using Hindsound.App;
using Hindsound.Mac.App;
using Hindsound.Mac.Tray;
using Hindsound.Ui;

namespace Hindsound.Mac.Tests.App;

/// <summary>The command line of the Mac app, and the check of the menu its proof reads back. Pure: runs on every system.</summary>
public sealed class MacLaunchTests
{
    [Fact]
    public void Save_and_the_proof_of_the_loop_are_taken_out_before_the_options_are_read()
    {
        var launch = MacLaunch.Read(["--settings", "/tmp/s.json", "--save", "--prove-loop", "--no-mic"]);

        Assert.True(launch.Save);
        Assert.True(launch.ProveLoop);
        Assert.Equal(["--settings", "/tmp/s.json", "--no-mic"], launch.Rest);
        Assert.Equal(InstanceMessage.Save, launch.Message);
        Assert.Equal("/tmp/s.json", AppOptions.Parse(launch.Rest).SettingsPath);
    }

    [Fact]
    public void A_second_plain_launch_asks_the_running_one_to_show_itself()
    {
        var launch = MacLaunch.Read(["--settings", "/tmp/s.json"]);

        Assert.False(launch.Save);
        Assert.False(launch.ProveLoop);
        Assert.Equal(InstanceMessage.Show, launch.Message);
        Assert.Equal("--save", MacLaunch.SaveOption);
    }

    [Theory]
    [InlineData("/Applications/Hindsound.app/Contents/MacOS/hindsound", "/Applications/Hindsound.app")]
    [InlineData("/Users/me/Applications/My Apps/Hindsound.app/Contents/MacOS/hindsound", "/Users/me/Applications/My Apps/Hindsound.app")]
    [InlineData("/tmp/out/Hindsound.APP/Contents/MacOS/hindsound", "/tmp/out/Hindsound.APP")]
    public void A_program_inside_a_bundle_names_its_bundle(string program, string bundle) =>
        Assert.Equal(bundle, MacLaunch.BundleOf(program));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/src/Hindsound.Mac/bin/Release/net10.0/osx-arm64/hindsound")]
    [InlineData("/Applications/Hindsound/Contents/MacOS/hindsound")]
    [InlineData("/Applications/Hindsound.app/Contents/MacOS/")]
    [InlineData("/Applications/Hindsound.app/Contents/MacOS/sub/hindsound")]
    [InlineData("/.app/Contents/MacOS/hindsound")]
    [InlineData("Hindsound.app/Contents/MacOS/hindsound")]
    public void A_program_outside_a_bundle_has_none(string? program) =>
        Assert.Null(MacLaunch.BundleOf(program));

    [Fact]
    public void The_bundle_is_started_again_as_a_new_process_with_the_options_of_the_launch_but_not_save()
    {
        var launch = MacLaunch.Read(["--save", "--settings", "/tmp/my settings.json"]);

        Assert.Equal(["-n", "/Applications/Hindsound.app", "--args", "--settings", "/tmp/my settings.json"],
            MacLaunch.RelaunchArguments("/Applications/Hindsound.app", launch.Rest));
        Assert.Equal(["-n", "/Applications/Hindsound.app"], MacLaunch.RelaunchArguments("/Applications/Hindsound.app", MacLaunch.Read(["--save"]).Rest));
        Assert.Equal("/usr/bin/open", MacLaunch.OpenProgram);
    }

    [Fact]
    public void The_menu_read_back_matches_only_when_every_line_is_the_one_of_the_app()
    {
        var expected = TrayMenu.Items(paused: false, hasLastClip: false, closingAfterClip: false);
        var lines = expected.Select(Line).ToList();

        Assert.True(MacLoopProof.SameLines(expected, lines));

        var copy = expected.ToList().FindIndex(item => item.Command == TrayCommand.CopyLast);
        Assert.False(lines[copy].Enabled);
        Assert.False(MacLoopProof.SameLines(expected, Changed(lines, copy, lines[copy] with { Enabled = true })));
        Assert.False(MacLoopProof.SameLines(expected, Changed(lines, 0, lines[0] with { Title = "Open" })));
        Assert.False(MacLoopProof.SameLines(expected, Changed(lines, 0, lines[0] with { Targeted = false })));
        Assert.False(MacLoopProof.SameLines(expected, Changed(lines, 0, lines[0] with { Command = TrayCommand.Quit })));
        Assert.False(MacLoopProof.SameLines(expected, lines.Take(lines.Count - 1).ToList()));

        var separator = expected.ToList().FindIndex(item => item.Command == TrayCommand.None);
        Assert.False(MacLoopProof.SameLines(expected, Changed(lines, separator, lines[separator] with { Separator = false })));
    }

    [Fact]
    public void A_checked_line_must_be_on_in_the_menu()
    {
        var expected = TrayMenu.WindowlessItems(paused: false, startsWithSession: true, clipSeconds: 30, closingAfterClip: false);
        var lines = expected.Select(Line).ToList();
        var start = expected.ToList().FindIndex(item => item.Command == TrayCommand.StartWithSession);

        Assert.True(lines[start].On);
        Assert.True(MacLoopProof.SameLines(expected, lines));
        Assert.False(MacLoopProof.SameLines(expected, Changed(lines, start, lines[start] with { On = false })));
    }

    private static MenuLine Line(TrayMenuItem item) => item.Command == TrayCommand.None
        ? new MenuLine(string.Empty, TrayCommand.None, true, true, false, false)
        : new MenuLine(item.Text, item.Command, item.Enabled, false, item.Checked == true, true);

    private static List<MenuLine> Changed(List<MenuLine> lines, int index, MenuLine line)
    {
        var copy = lines.ToList();
        copy[index] = line;
        return copy;
    }
}
