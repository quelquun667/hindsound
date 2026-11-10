using Hindsound.Audio;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The words of the line of a change and the title of the PC side share a row. With long exe names the words must stay whole: the
/// "then" part of the title gives way, not the words.
/// </summary>
public class WaveTitleLongNameTests
{
    private const int Rate = NowRig.Rate;
    private const double Scale = 1.0;
    private const uint Accent = 0x7CFF3B;

    private static AudioEngineStatus On(string app) => ScreenRig.Capturing() with { SourceApp = app };

    private static SourceChange At(double seconds, string app, string before) => new((long)(seconds * Rate), app, before);

    private static (FakeCanvas Canvas, string Words) Paint(string app, string before, double changeAt)
    {
        var wave = new NowWave();
        var canvas = new FakeCanvas();
        var status = On(app);

        wave.Look(Scale, NowRig.FullUntil(null, null, 100, At(changeAt, app, before)), 30, status);
        wave.PaintLanes(canvas, Accent, status, titles: true);
        return (canvas, WaveView.MarkerText(wave.Marker.App));
    }

    [Fact]
    public void A_line_at_the_right_edge_keeps_its_words_whole_with_a_long_game_exe_after_another_app()
    {
        var (canvas, words) = Paint("FortniteClient-Win64-Shipping.exe", "Discord.exe", changeAt: 99.5);

        Assert.StartsWith("FortniteClient-Win64-Shipping", words, StringComparison.Ordinal);
        Assert.Contains(words, canvas.Drawn);
    }

    [Fact]
    public void The_title_is_cut_before_the_words_and_ends_left_of_them()
    {
        var (canvas, words) = Paint("FortniteClient-Win64-Shipping.exe", "Discord.exe", changeAt: 99.5);

        var title = Assert.Single(canvas.Drawn, text => text.StartsWith("Discord", StringComparison.Ordinal));
        Assert.EndsWith("...", title);
        var wordsX = canvas.DrawnAt.Single(drawn => drawn.Text == words).X;
        Assert.True(SettingsScreen.Left + canvas.Measure(title, Face.Medium, 13) <= wordsX);
    }

    [Fact]
    public void Names_that_fit_with_the_words_are_not_cut()
    {
        var (canvas, words) = Paint("SpaceTruckers.exe", "MoonMiners.exe", changeAt: 99.5);

        Assert.Contains("MoonMiners, then SpaceTruckers", canvas.Drawn);
        Assert.Contains(words, canvas.Drawn);
    }
}
