using Hindsound.Audio;
using Hindsound.Mac.Audio.Devices;

namespace Hindsound.Mac.Tests.Audio.Devices;

public sealed class MacProcessNamesTests
{
    private const string SafariPath = "/System/Volumes/Preboot/Cryptexes/App/System/Applications/Safari.app/Contents/MacOS/Safari";
    private const string WebContentPath = "/System/Library/Frameworks/WebKit.framework/Versions/A/XPCServices/com.apple.WebKit.WebContent.xpc/Contents/MacOS/com.apple.WebKit.WebContent";
    private const string ChromePath = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
    private const string ChromeHelperPath = "/Applications/Google Chrome.app/Contents/Frameworks/Google Chrome Framework.framework/Versions/131.0/Helpers/Google Chrome Helper (Renderer).app/Contents/MacOS/Google Chrome Helper (Renderer)";

    [Theory]
    [InlineData(SafariPath, "Safari")]
    [InlineData(ChromeHelperPath, "Google Chrome")]
    [InlineData("/Applications/Hindsound.app/Contents/MacOS/hindsound", "Hindsound")]
    [InlineData("/Applications/Old.APP/Contents/MacOS/old", "Old")]
    [InlineData("/usr/bin/afplay", null)]
    [InlineData(WebContentPath, null)]
    [InlineData("/Applications/Lone.app", null)]
    [InlineData("/tmp/.app/run", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_program_is_named_after_the_outermost_bundle_that_holds_it(string? path, string? expected) =>
        Assert.Equal(expected, MacProcessNames.BundleName(path));

    [Fact]
    public void A_program_outside_a_bundle_keeps_its_file_name_or_the_kernel_name()
    {
        var named = MacProcessNames.Name(
            [new MacProcessFacts(300, 200, "/usr/bin/afplay", "afplay"), new MacProcessFacts(301, 200, null, "kernel_task_ish")],
            _ => null);

        Assert.Equal([new ProcessEntry(300, 200, "afplay"), new ProcessEntry(301, 200, "kernel_task_ish")], named);
    }

    [Fact]
    public void A_service_launchd_started_for_an_application_takes_its_name_and_has_it_as_parent()
    {
        var named = MacProcessNames.Name(
            [
                new MacProcessFacts(520, 1, WebContentPath, "com.apple.WebKi"),
                new MacProcessFacts(500, 1, SafariPath, "Safari"),
                new MacProcessFacts(1, 0, "/sbin/launchd", "launchd"),
            ],
            pid => pid == 520 ? 500 : pid);

        Assert.Equal([new ProcessEntry(1, 0, "launchd"), new ProcessEntry(500, 1, "Safari"), new ProcessEntry(520, 500, "Safari")], named);

        // The capture of Safari then starts at Safari itself, and follows its service as a child.
        Assert.Equal(500, SourceAppFinder.FindCaptureTarget(named, "Safari"));
    }

    [Fact]
    public void A_program_a_shell_started_stays_itself_whatever_it_runs_for()
    {
        // afplay started in Terminal runs for Terminal, but its parent is the shell, not launchd.
        var named = MacProcessNames.Name(
            [
                new MacProcessFacts(400, 1, "/System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal", "Terminal"),
                new MacProcessFacts(410, 400, "/bin/zsh", "zsh"),
                new MacProcessFacts(420, 410, "/usr/bin/afplay", "afplay"),
            ],
            _ => 400);

        Assert.Equal(new ProcessEntry(420, 410, "afplay"), named[2]);
        Assert.Equal(new ProcessEntry(410, 400, "zsh"), named[1]);
    }

    [Fact]
    public void A_service_keeps_its_own_name_when_it_runs_for_itself_for_nothing_known_or_for_a_program_outside_a_bundle()
    {
        var named = MacProcessNames.Name(
            [
                new MacProcessFacts(90, 1, "/usr/sbin/coreaudiod", "coreaudiod"),
                new MacProcessFacts(91, 1, "/usr/libexec/helper", "helper"),
                new MacProcessFacts(92, 1, "/usr/libexec/other", "other"),
                new MacProcessFacts(93, 1, "/usr/libexec/orphan", "orphan"),
            ],
            pid => pid switch
            {
                90 => 90,
                91 => null,
                92 => 90,
                _ => 7777,
            });

        Assert.Equal(
            [new ProcessEntry(90, 1, "coreaudiod"), new ProcessEntry(91, 1, "helper"), new ProcessEntry(92, 1, "other"), new ProcessEntry(93, 1, "orphan")],
            named);
    }

    [Fact]
    public void The_helpers_of_an_application_share_its_name_and_the_capture_starts_at_the_application()
    {
        var named = MacProcessNames.Name(
            [
                new MacProcessFacts(712, 700, ChromeHelperPath, "Google Chrome H"),
                new MacProcessFacts(700, 1, ChromePath, "Google Chrome"),
                new MacProcessFacts(711, 700, ChromeHelperPath, "Google Chrome H"),
            ],
            pid => pid);

        Assert.Equal([700, 711, 712], named.Select(entry => entry.Id));
        Assert.All(named, entry => Assert.Equal("Google Chrome", entry.ExeName));
        Assert.Equal(700, SourceAppFinder.FindCaptureTarget(named, "google chrome"));
    }
}
