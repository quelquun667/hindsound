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
