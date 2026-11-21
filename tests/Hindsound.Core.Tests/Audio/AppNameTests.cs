using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Audio;

/// <summary>
/// The name a person reads for the program recorded alone: a Windows name loses its ".exe" and nothing else, a Linux name,
/// which has no extension and may hold a dot, stays whole.
/// </summary>
public class AppNameTests
{
    [Theory]
    [InlineData("Discord.exe", "Discord")]
    [InlineData("RobloxPlayerBeta.EXE", "RobloxPlayerBeta")]
    [InlineData("chrome.Exe", "chrome")]
    [InlineData("My.Game.exe", "My.Game")]
    [InlineData("org.kde.foo", "org.kde.foo")]
    [InlineData("python3.12", "python3.12")]
    [InlineData("firefox", "firefox")]
    [InlineData("setup.exe.bak", "setup.exe.bak")]
    [InlineData(".exe", "")]
    public void Only_a_final_exe_is_left_out(string exeName, string shown) =>
        Assert.Equal(shown, AudioAppList.NameOf(exeName));

    [Fact]
    public void A_linux_program_with_a_dot_in_its_name_is_offered_whole_when_its_stream_gives_no_name()
    {
        var apps = AudioAppList.Build([(10, null), (11, null)], [new ProcessEntry(10, 1, "python3.12"), new ProcessEntry(11, 1, "Discord.exe")], 1);

        Assert.Equal(["python3.12", "Discord"], apps.Select(app => app.Name));
    }

    [Fact]
    public void A_linux_program_with_a_dot_in_its_name_is_shown_whole_in_the_record_list_the_wave_and_the_status_band()
    {
        var status = new AudioEngineStatus(CaptureActivity.Capturing, true, false, false, "org.kde.foo", null, false, TimeSpan.FromSeconds(5), 0, SourceApp: "org.kde.foo");

        Assert.Equal("org.kde.foo", SettingsEditor.RecordChoices([], "org.kde.foo", selectedRunning: true)[1].Label);
        Assert.Equal("org.kde.foo only from here", WaveView.MarkerText("org.kde.foo"));
        Assert.Equal("Capturing org.kde.foo", StatusText.From(status).Title);
        Assert.Equal("Can't capture org.kde.foo", StatusText.From(status with { Activity = CaptureActivity.NoDevice }).Title);
    }

    [Fact]
    public void A_windows_program_is_shown_without_its_exe_in_the_record_list_the_wave_and_the_status_band()
    {
        var status = new AudioEngineStatus(CaptureActivity.Capturing, true, false, false, "Discord.exe", null, false, TimeSpan.FromSeconds(5), 0, SourceApp: "Discord.exe");

        Assert.Equal("Discord", SettingsEditor.RecordChoices([], "Discord.exe", selectedRunning: true)[1].Label);
        Assert.Equal("Discord only from here", WaveView.MarkerText("Discord.exe"));
        Assert.Equal("Capturing Discord", StatusText.From(status).Title);
    }
}
