using Hindsound.App;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>"Start with Windows" turned on, and Windows refuses: the file and the switch must both say what is really true.</summary>
public sealed class StartupRefusedTests
{
    private sealed class RefusingKey : IRunKey
    {
        public RunEntry State(string? program) => RunEntry.None;

        public void Enable(string program) => throw new UnauthorizedAccessException();

        public void Disable() => throw new UnauthorizedAccessException();
    }

    private sealed class RefusingBackend(AppSettings settings, FakeHost host) : ISettingsBackend
    {
        public int InvalidatedAtTheRefusal { get; private set; } = -1;

        public void Applied(SettingsChange change)
        {
            StartupSwitch.Apply(settings.General, new RefusingKey(), "C:\\Hindsound.exe");
            InvalidatedAtTheRefusal = host.Invalidated;
        }

        public void SaveNow() { }

        public void TogglePause() { }

        public void CopyLast() { }

        public void OpenFolder() { }

        public void ShowTestNotification() { }

        public void SuspendHotkeys() { }

        public IReadOnlyList<HotkeyResult> ApplyHotkeys(HotkeySettings hotkeys) => [];

        public IReadOnlyList<HotkeyResult> ResumeHotkeys() => [];
    }

    private sealed class DrawingLog : ICanvas
    {
        public List<string> Calls { get; } = [];

        public void SetOrigin(float dy) => Calls.Add($"origin {dy}");

        public void FillRect(Box box, uint rgb) => Calls.Add($"rect {box} {rgb}");

        public void FillRound(Box box, float radius, uint rgb) => Calls.Add($"round {box} {radius} {rgb}");

        public void Frame(Box box, float radius, uint fill, uint border) => Calls.Add($"frame {box} {radius} {fill} {border}");

        public void Ring(Box box, float radius, float thickness, uint rgb) => Calls.Add($"ring {box} {radius} {thickness} {rgb}");

        public void Stroke(float ax, float ay, float bx, float by, float width, uint rgb) => Calls.Add($"stroke {ax} {ay} {bx} {by} {width} {rgb}");

        public void Glow(Box box, float sigma, float opacity, uint rgb) => Calls.Add($"glow {box} {sigma} {opacity} {rgb}");

        public void Text(string text, Face face, float size, float x, float top, uint rgb) => Calls.Add($"text {text} {face} {size} {x} {top} {rgb}");

        public float Measure(string text, Face face, float size) => text.Length * size * 0.5f;

        public float LineHeight(Face face, float size) => size * 1.3f;

        public void WarningIcon(float x, float y, float size, uint rgb) => Calls.Add($"warning {x} {y} {size} {rgb}");

        public void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored) => Calls.Add($"notification {x} {y} {accent}");
    }

    // The page with the focus on the switch, as drawn. Pressing nothing: the switch simply says what the setting says.
    private static List<string> DrawnWith(bool startWithWindows)
    {
        var settings = new AppSettings().Normalize();
        settings.General.StartWithWindows = startWithWindows;
        var host = new FakeHost();
        var screen = new SettingsScreen(settings, new RefusingBackend(settings, host), host, new FakeCanvas(), false);
        screen.ShowSection(Section.General);
        screen.TabTo("general.startup");
        var log = new DrawingLog();
        screen.Paint(log);
        return log.Calls;
    }

    [Fact]
    public void The_switch_shows_off_after_the_refusal_and_not_what_was_asked()
    {
        var settings = new AppSettings().Normalize();
        var host = new FakeHost();
        var screen = new SettingsScreen(settings, new RefusingBackend(settings, host), host, new FakeCanvas(), false);
        screen.ShowSection(Section.General);
        screen.TabTo("general.startup");

        screen.Key(Vk.Space, 0);
        var log = new DrawingLog();
        screen.Paint(log);

        Assert.Equal(DrawnWith(false), log.Calls);
        Assert.NotEqual(DrawnWith(true), log.Calls);
    }

    [Fact]
    public void The_file_gets_what_the_entry_really_is()
    {
        var general = new GeneralSettings { StartWithWindows = true };
        bool? written = null;

        StartupSwitch.ApplyThenSave(true, () => StartupSwitch.Apply(general, new RefusingKey(), "C:\\Hindsound.exe"), () => written = general.StartWithWindows);

        Assert.False(written);
    }

    [Fact]
    public void A_change_that_is_not_about_startup_leaves_the_entry_alone()
    {
        var applied = 0;
        var saved = 0;

        StartupSwitch.ApplyThenSave(false, () => applied++, () => saved++);

        Assert.Equal(0, applied);
        Assert.Equal(1, saved);
    }

    [Fact]
    public void The_switch_is_drawn_again_after_the_refusal()
    {
        var settings = new AppSettings().Normalize();
        var host = new FakeHost();
        var backend = new RefusingBackend(settings, host);
        var screen = new SettingsScreen(settings, backend, host, new FakeCanvas(), false);
        screen.ShowSection(Section.General);
        screen.TabTo("general.startup");

        screen.Key(Vk.Space, 0);

        Assert.False(settings.General.StartWithWindows);
        Assert.True(host.Invalidated > backend.InvalidatedAtTheRefusal, "nothing asked for a new drawing after Windows refused");
    }
}
