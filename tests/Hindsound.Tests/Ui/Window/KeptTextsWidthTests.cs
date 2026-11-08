using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The new words of the window, measured by the same engine and fonts as the window itself: a note of the card is never cut, it runs off the panel.</summary>
public unsafe class KeptTextsWidthTests
{
    public static TheoryData<double> Scales => new() { 1.0, 1.25, 1.5, 2.0 };

    [Theory]
    [MemberData(nameof(Scales))]
    public void The_notes_of_the_first_launch_card_stay_in_their_panel(double scale)
    {
        var s = (float)scale;
        using var canvas = new GdiCanvas(760, 488, scale);
        var room = SettingsScreen.FirstRunTextWidth;

        foreach (var seconds in new[] { 5, 15, 30, 60, 90, 300, 600, 1800 })
        {
            var note = StatusText.FirstRunHotkeyNote(seconds);
            Assert.True(canvas.Measure(note, Face.Body, 14) <= room, $"{note}: {canvas.Measure(note, Face.Body, 14)} of {room} at {s}");
        }

        var both = canvas.Measure(SettingsScreen.FirstRunWait, Face.Body, 14) + SettingsScreen.FirstRunNoteGap + canvas.Measure(SettingsScreen.FirstRunSay, Face.Body, 14);
        Assert.True(both <= room, $"{both} of {room} at {s}");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void The_band_never_cuts_the_word_capturing(double scale)
    {
        using var canvas = new GdiCanvas(760, 488, scale);
        var cut = new List<string>();

        // By the path of the drawing, with the widest cell the settings allow and each kind of save label the slider gives.
        foreach (var label in new[] { "Save up to 30s", "Save up to 1 min", "Save up to 5 min", "Save up to 1:30" })
        {
            var pauseLeft = SettingsScreen.BandPauseLeft(canvas, label, "Ctrl+Shift+F10");
            foreach (var mic in new[] { "mic off", "mic on", "mic not captured" })
            {
                foreach (var (cell, ram) in new[] { ("0:12 / 5:00", "~87 MB"), ("9:12 / 10:00", "~174 MB"), ("29:50 / 30:00", "~522 MB") })
                {
                    var band = new StatusBand(StatusMark.Dot, "Capturing", mic, cell, ram, "Pause", true, true);
                    var (title, gap) = SettingsScreen.BandTitle(canvas, band, pauseLeft, 28 + 20);
                    if (title != "Capturing")
                    {
                        cut.Add($"{label}, {mic}, {cell}: {title} with a gap of {gap}");
                    }
                }
            }
        }

        Assert.True(cut.Count == 0, string.Join("\n", cut));
    }

    [Fact]
    public void The_usual_band_keeps_its_wide_spaces()
    {
        using var canvas = new GdiCanvas(760, 488, 1.0);
        var pauseLeft = SettingsScreen.BandPauseLeft(canvas, "Save up to 30s", "Ctrl+Shift+F10");
        var band = new StatusBand(StatusMark.Dot, "Capturing", "mic on", "0:12 / 5:00", "~87 MB", "Pause", true, true);

        Assert.Equal(("Capturing", 20f), SettingsScreen.BandTitle(canvas, band, pauseLeft, 28 + 20));
    }

    [Fact]
    public void A_name_too_long_for_any_space_is_still_cut_at_its_end()
    {
        using var canvas = new GdiCanvas(760, 488, 1.0);
        var pauseLeft = SettingsScreen.BandPauseLeft(canvas, "Save up to 30s", "Ctrl+Shift+F10");
        var band = new StatusBand(StatusMark.Dot, "Capturing " + new string('n', 80), "mic on", "0:12 / 5:00", "~87 MB", "Pause", true, true);

        var (title, gap) = SettingsScreen.BandTitle(canvas, band, pauseLeft, 28 + 20);

        Assert.StartsWith("Capturing", title, StringComparison.Ordinal);
        Assert.EndsWith("...", title, StringComparison.Ordinal);
        Assert.Equal(12f, gap);
    }
}
