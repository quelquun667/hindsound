using System.IO;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.App;

/// <summary>What the app writes to its journal when the sound of the recorded application is lost, and when it is back.</summary>
public sealed class AppSoundJournalTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static AudioEngineStatus Recording(string app, bool lost = false, bool running = true) =>
        new(CaptureActivity.Capturing, true, false, false, "Speakers", null, true, TimeSpan.Zero, 0, app, SourceAppMissing: !running, SourceAppLost: lost);

    private string[] JournalAfter(params AudioEngineStatus[] statuses)
    {
        var log = _temp.Combine("settings.log");
        var options = AppOptions.Parse(["--settings", _temp.Combine("settings.json"), "--no-mic", "--offscreen"]);
        var app = new HindsoundApp(options, new Journal(log));
        AudioEngineStatus? previous = null;
        foreach (var status in statuses)
        {
            app.JournalStatus(previous, status);
            previous = status;
        }

        return File.ReadAllLines(log);
    }

    [Fact]
    public void A_sound_lost_and_a_sound_back_are_each_journaled_once()
    {
        var lines = JournalAfter(Recording("Discord.exe"), Recording("Discord.exe", lost: true), Recording("Discord.exe", lost: true), Recording("Discord.exe"));

        Assert.Single(lines, line => line.Contains("sound lost: Discord.exe", StringComparison.Ordinal));
        Assert.Single(lines, line => line.Contains("sound back: Discord.exe", StringComparison.Ordinal));
        Assert.True(Array.FindIndex(lines, line => line.Contains("sound lost:", StringComparison.Ordinal)) < Array.FindIndex(lines, line => line.Contains("sound back:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Nothing_lost_journals_no_sound_line()
    {
        var lines = JournalAfter(Recording("Discord.exe"), Recording("Discord.exe"));

        Assert.DoesNotContain(lines, line => line.Contains("sound ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_line_of_a_change_of_status_follows_the_application_asked_for()
    {
        var lost = Recording("Discord.exe", lost: true);

        Assert.Equal("sound lost: Discord.exe", StatusTransitions.SoundLine(null, lost));
        Assert.Equal("sound lost: Discord.exe", StatusTransitions.SoundLine(Recording("Discord.exe"), lost));
        Assert.Null(StatusTransitions.SoundLine(lost, lost));
        Assert.Equal("sound back: Discord.exe", StatusTransitions.SoundLine(lost, Recording("Discord.exe")));

        // Another application chosen: nothing of the first one is told as back, and a loss of the new one is told.
        Assert.Null(StatusTransitions.SoundLine(lost, Recording("Game.exe")));
        Assert.Equal("sound lost: Game.exe", StatusTransitions.SoundLine(lost, Recording("Game.exe", lost: true)));

        // The application closed: its own line says so, not a sound back; the whole PC has no application to speak of.
        Assert.Null(StatusTransitions.SoundLine(lost, Recording("Discord.exe", running: false)));
        Assert.Null(StatusTransitions.SoundLine(lost, new AudioEngineStatus(CaptureActivity.Capturing, true, false, false, "Speakers", null, true, TimeSpan.Zero, 0)));
    }
}
