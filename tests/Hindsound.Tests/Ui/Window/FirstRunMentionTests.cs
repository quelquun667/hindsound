using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Updates;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The line about GitHub on the first-launch card: only when this run can update, left of the buttons, two lines at most.</summary>
public sealed class FirstRunMentionTests : IDisposable
{
    private readonly ScreenRig _rig = new(firstRun: true);

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private static FakePanel Active() => new() { View = UpdatesSectionTests.ReadyView() };

    private void GoToStep(int step)
    {
        for (var i = Screen.FirstRunStep; i < step; i++)
        {
            _rig.FocusOn("run.next");
            _rig.Press(Vk.Enter);
        }

        Assert.Equal(step, Screen.FirstRunStep);
    }

    // What is drawn at the left edge, from the top of the mention down: the mention's lines and nothing else.
    private List<string> MentionLines(RecordingCanvas canvas)
    {
        var top = Screen.WidgetById(SettingsScreen.FirstRunUpdatesId)!.Bounds.Y;
        return canvas.Texts.Where(t => t.Length > 0 && canvas.Places[t].X == 40 && canvas.Places[t].Y >= top - 0.01f).ToList();
    }

    private RecordingCanvas Paint()
    {
        var canvas = new RecordingCanvas();
        Screen.SetCanvas(canvas);
        Screen.Paint(canvas);
        return canvas;
    }

    private static bool Overlap(Box a, Box b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    [Fact]
    public void Mention_is_there_when_the_panel_is_active()
    {
        Screen.SetUpdates(Active());

        Assert.NotNull(Screen.WidgetById(SettingsScreen.FirstRunUpdatesId));
        Assert.Equal(SettingsScreen.FirstRunUpdatesNote, string.Join(" ", MentionLines(Paint())));
    }

    [Fact]
    public void Mention_is_absent_without_a_panel()
    {
        Assert.Null(Screen.WidgetById(SettingsScreen.FirstRunUpdatesId));
        Assert.DoesNotContain(Paint().Texts, t => t.Contains("GitHub", StringComparison.Ordinal));
    }

    [Fact]
    public void Mention_is_absent_when_this_copy_does_not_update()
    {
        Screen.SetUpdates(new FakePanel { View = UpdatePanel.Inactive(new UpdateVersion(0, 1, 0)) });

        Assert.Null(Screen.WidgetById(SettingsScreen.FirstRunUpdatesId));
        Assert.DoesNotContain(Paint().Texts, t => t.Contains("GitHub", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Mention_stays_at_every_step_without_touching_the_buttons(int step)
    {
        Screen.SetUpdates(Active());
        GoToStep(step);

        var mention = Screen.WidgetById(SettingsScreen.FirstRunUpdatesId)!.Bounds;
        Assert.False(Overlap(mention, Screen.WidgetById("run.next")!.Bounds));
        if (Screen.WidgetById("run.back") is { } back)
        {
            Assert.False(Overlap(mention, back.Bounds));
            Assert.True(mention.Right <= back.Bounds.X);
        }

        Assert.Equal(40, mention.X);
        Assert.True(mention.Bottom <= SettingsScreen.WindowHeight);
    }

    [Fact]
    public void Mention_takes_two_lines_at_most()
    {
        Screen.SetUpdates(Active());
        GoToStep(2);
        var canvas = Paint();

        var lines = MentionLines(canvas);
        Assert.InRange(lines.Count, 1, 2);
        Assert.True(Screen.WidgetById(SettingsScreen.FirstRunUpdatesId)!.Bounds.H <= 2 * canvas.LineHeight(Face.Body, 13));
    }

    [Fact]
    public void Too_wide_a_font_cuts_the_second_line_rather_than_add_a_third()
    {
        Screen.SetUpdates(Active());
        GoToStep(2);
        var wide = new WideCanvas();
        Screen.SetCanvas(wide);
        Screen.Paint(wide);

        var mention = Screen.WidgetById(SettingsScreen.FirstRunUpdatesId)!.Bounds;
        var lines = wide.Inner.Texts.Where(t => t.Length > 0 && wide.Inner.Places[t].X == 40 && wide.Inner.Places[t].Y >= mention.Y - 0.01f).ToList();
        Assert.Equal(2, lines.Count);
        Assert.EndsWith("...", lines[1], StringComparison.Ordinal);
        Assert.True(mention.H <= 2 * wide.LineHeight(Face.Body, 13));
    }

    [Fact]
    public void Mention_comes_and_goes_with_the_panel()
    {
        Screen.SetUpdates(Active());
        Screen.SetUpdates(null);

        Assert.Null(Screen.WidgetById(SettingsScreen.FirstRunUpdatesId));
        Assert.Equal(Section.FirstRun, Screen.Section);
    }

    // The real fonts, measured by the same path the window draws with: DirectWrite, or GDI when it is missing.
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Real_fonts_fit_the_mention_in_two_whole_lines(double scale)
    {
        using var canvas = new GdiCanvas((int)(SettingsScreen.WindowWidth * scale), (int)(SettingsScreen.WindowHeight * scale), scale);
        Screen.SetUpdates(Active());
        Screen.SetCanvas(canvas);
        for (var step = 0; step < 3; step++)
        {
            GoToStep(step);
            var mention = Screen.WidgetById(SettingsScreen.FirstRunUpdatesId)!.Bounds;
            Assert.True(mention.H <= 2 * canvas.LineHeight(Face.Body, 13) + 0.01f);

            // Two whole lines: the words fit without the cut that would add "...".
            var room = mention.W;
            var words = SettingsScreen.FirstRunUpdatesNote.Split(' ');
            var first = string.Empty;
            var i = 0;
            for (; i < words.Length; i++)
            {
                var attempt = first.Length == 0 ? words[i] : first + " " + words[i];
                if (canvas.Measure(attempt, Face.Body, 13) > room)
                {
                    break;
                }

                first = attempt;
            }

            Assert.True(canvas.Measure(string.Join(' ', words.Skip(i)), Face.Body, 13) <= room);
            Screen.Paint(canvas);
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Real_fonts_fit_the_section_and_the_card(double scale)
    {
        using var canvas = new GdiCanvas((int)(SettingsScreen.WindowWidth * scale), (int)(SettingsScreen.WindowHeight * scale), scale);
        using var rig = new ScreenRig();
        rig.Screen.SetCanvas(canvas);
        rig.Screen.SetUpdates(Active());
        rig.Screen.ShowUpdates();
        var buttons = new[] { "updates.check", "updates.now", "updates.whatsnew", "updates.skip" }.Select(id => rig.Screen.WidgetById(id)!.Bounds).ToList();
        Assert.All(buttons, b => Assert.Equal(buttons[0].Y, b.Y));
        Assert.True(buttons[^1].Right <= 728);
        Assert.True(canvas.Measure("Version 0.2.0 is available. Switch to Ask me to download it.", Face.Body, 13) <= 528);
        rig.Screen.Paint(canvas);

        rig.Screen.ShowUpdateCard();
        var save = rig.Screen.WidgetById("card.save")!.Bounds;
        Assert.True(save.X >= 130 + 28);
        Assert.True(rig.Screen.WidgetById("card.cancel")!.Bounds.Bottom <= SettingsScreen.WindowHeight - 24);
        rig.Screen.Paint(canvas);
    }

    /// <summary>A font twice as wide as the recording one: the mention needs more than two lines.</summary>
    private sealed class WideCanvas : ICanvas
    {
        public RecordingCanvas Inner { get; } = new();

        public void SetOrigin(float dy) => Inner.SetOrigin(dy);
        public void FillRect(Box box, uint rgb) => Inner.FillRect(box, rgb);
        public void FillRound(Box box, float radius, uint rgb) => Inner.FillRound(box, radius, rgb);
        public void Frame(Box box, float radius, uint fill, uint border) => Inner.Frame(box, radius, fill, border);
        public void Ring(Box box, float radius, float thickness, uint rgb) => Inner.Ring(box, radius, thickness, rgb);
        public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) => Inner.Stroke(ax, ay, bx, by, width, rgb);
        public void Glow(Box box, float sigma, float opacity, uint rgb) => Inner.Glow(box, sigma, opacity, rgb);
        public void Text(string text, Face face, float size, float x, float top, uint rgb) => Inner.Text(text, face, size, x, top, rgb);
        public float Measure(string text, Face face, float size) => 2 * Inner.Measure(text, face, size);
        public float LineHeight(Face face, float size) => Inner.LineHeight(face, size);
        public void WarningIcon(float x, float y, float size, uint rgb) => Inner.WarningIcon(x, y, size, rgb);
        public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) => Inner.Notification(content, x, y, accent, mirrored);
    }
}
