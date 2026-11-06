using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>What is written on the accent, and the accent as a colour of text, for every colour that can be chosen.</summary>
public sealed class AccentTextTests : IDisposable
{
    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    private static uint Rgb(string hex)
    {
        Assert.True(AccentColors.TryParseHex(hex, out var r, out var g, out var b));
        return ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private static UiState Ui(uint accent) => new(accent, null, null, null, -1, -1, 0, false, null);

    // ---------------------------------------------------------------- the rule

    [Theory]
    [InlineData("#D4F53C")]
    [InlineData("#2EF2C4")]
    [InlineData("#35D6FF")]
    [InlineData("#B79CFF")]
    [InlineData("#FF6FB5")]
    [InlineData("#F2F4F7")]
    public void The_six_presets_keep_the_dark_text_of_the_mockup(string hex)
    {
        Assert.Equal(AccentColors.DarkOnAccent, AccentColors.TextOn(Rgb(hex)));
        Assert.True(AccentColors.ContrastRatio(Rgb(hex), AccentColors.DarkOnAccent) >= AccentColors.MinTextContrast);
    }

    [Theory]
    [InlineData("#646464")]
    [InlineData("#BF2828")]
    public void A_dim_accent_that_is_accepted_gets_the_light_text(string hex)
    {
        Assert.Equal(AccentCheck.Valid, AccentColors.Validate(hex));
        Assert.Equal(AccentColors.LightOnAccent, AccentColors.TextOn(Rgb(hex)));
        Assert.True(AccentColors.ContrastRatio(Rgb(hex), AccentColors.TextOn(Rgb(hex))) >= AccentColors.MinTextContrast);
    }

    [Fact]
    public void A_grey_that_neither_colour_can_serve_gets_pure_black_or_white()
    {
        var grey = Rgb("#777777");
        Assert.True(AccentColors.ContrastRatio(grey, AccentColors.DarkOnAccent) < AccentColors.MinTextContrast);
        Assert.True(AccentColors.ContrastRatio(grey, AccentColors.LightOnAccent) < AccentColors.MinTextContrast);

        Assert.Equal(0x000000u, AccentColors.TextOn(grey));
        Assert.Equal(0xFFFFFFu, AccentColors.TextOn(Rgb("#707070")));
    }

    [Fact]
    public void Every_accepted_accent_reads_at_four_and_a_half_to_one()
    {
        var checkedColors = 0;
        void Check(uint rgb)
        {
            if (AccentColors.Validate($"#{rgb:X6}") == AccentCheck.Valid)
            {
                checkedColors++;
                Assert.True(AccentColors.ContrastRatio(rgb, AccentColors.TextOn(rgb)) >= AccentColors.MinTextContrast, $"#{rgb:X6}");
            }
        }

        for (uint v = 0; v <= 255; v++)
        {
            Check((v << 16) | (v << 8) | v);
        }

        for (uint r = 0; r <= 255; r += 15)
        {
            for (uint g = 0; g <= 255; g += 15)
            {
                for (uint b = 0; b <= 255; b += 15)
                {
                    Check((r << 16) | (g << 8) | b);
                }
            }
        }

        Assert.True(checkedColors > 1000);
    }

    [Fact]
    public void The_rule_for_accepting_an_accent_has_not_moved()
    {
        Assert.Equal(AccentCheck.Valid, AccentColors.Validate("#646464"));
        Assert.Equal(AccentCheck.TooDark, AccentColors.Validate("#202040"));
        Assert.Equal(3.0, AccentColors.MinContrast);
    }

    [Fact]
    public void The_colours_of_the_rule_are_those_of_the_window()
    {
        Assert.Equal(Theme.Text, AccentColors.LightOnAccent);
        Assert.Equal(Rgb("#F2F4F7"), AccentColors.LightOnAccent);
        Assert.Equal(Rgb("#14170A"), AccentColors.DarkOnAccent);
    }

    [Theory]
    [InlineData("#D4F53C", true)]
    [InlineData("#BF2828", false)]
    [InlineData("#646464", false)]
    public void The_accent_as_text_on_raised_gives_way_under_three_to_one(string hex, bool stays)
    {
        var accent = Rgb(hex);
        Assert.Equal(stays, AccentColors.ContrastRatio(accent, Theme.Raised) >= 3);
        Assert.Equal(stays ? accent : Theme.Text, AccentColors.AccentAsText(accent, Theme.Raised, Theme.Text));
    }

    // ---------------------------------------------------------------- what is really drawn

    [Theory]
    [InlineData("#D4F53C", AccentColors.DarkOnAccent)]
    [InlineData("#646464", AccentColors.LightOnAccent)]
    [InlineData("#BF2828", AccentColors.LightOnAccent)]
    public void The_big_save_button_writes_in_the_right_colour(string hex, uint expected)
    {
        _rig.Settings.General.AccentColor = hex;
        Screen.UpdateStatus(ScreenRig.Capturing());
        var canvas = new FakeCanvas();

        Screen.Paint(canvas);

        Assert.Equal(expected, canvas.TextColors[StatusText.SaveLabel(_rig.Settings.ClipSeconds)]);
        Assert.Equal(expected, canvas.TextColors[_rig.Settings.Hotkeys.SaveDefault!]);
    }

    [Theory]
    [InlineData("#D4F53C", AccentColors.DarkOnAccent)]
    [InlineData("#BF2828", AccentColors.LightOnAccent)]
    public void An_accented_button_with_no_second_line_writes_in_the_right_colour(string hex, uint expected)
    {
        var canvas = new FakeCanvas();
        var button = new Button("Next", () => { }) { Accented = true, Bounds = new Box(0, 0, 80, 34) };

        button.Paint(canvas, Ui(Rgb(hex)));

        Assert.Equal(expected, canvas.TextColors["Next"]);
    }

    [Theory]
    [InlineData("#D4F53C", AccentColors.DarkOnAccent)]
    [InlineData("#BF2828", AccentColors.LightOnAccent)]
    public void The_chosen_segment_writes_in_the_right_colour(string hex, uint expected)
    {
        var canvas = new FakeCanvas();
        var group = new Segmented(["WAV", "MP3"], () => 1, _ => { }) { Bounds = new Box(0, 0, 200, 30) };

        group.Paint(canvas, Ui(Rgb(hex)));

        Assert.Equal(expected, canvas.TextColors["MP3"]);
        Assert.Equal(Theme.Text, canvas.TextColors["WAV"]);
    }

    [Theory]
    [InlineData("#D4F53C", AccentColors.DarkOnAccent)]
    [InlineData("#BF2828", AccentColors.LightOnAccent)]
    public void The_knob_of_a_toggle_that_is_on_stands_out(string hex, uint expected)
    {
        var canvas = new FakeCanvas();
        var toggle = new Toggle(() => true, _ => { }) { Bounds = new Box(0, 0, 40, 22) };

        toggle.Paint(canvas, Ui(Rgb(hex)));

        Assert.Equal(expected, canvas.LastRoundFill);
    }

    [Theory]
    [InlineData("#D4F53C", AccentColors.DarkOnAccent)]
    [InlineData("#BF2828", AccentColors.LightOnAccent)]
    public void The_steps_of_the_first_launch_write_in_the_right_colour(string hex, uint expected)
    {
        using var rig = new ScreenRig(firstRun: true);
        rig.Settings.General.AccentColor = hex;
        rig.Screen.UpdateStatus(ScreenRig.Capturing());
        var first = new FakeCanvas();
        rig.Screen.Paint(first);
        Assert.Equal(expected, first.TextColors["1"]);

        rig.FocusOn("run.next");
        rig.Press(Vk.Enter);
        var second = new FakeCanvas();
        rig.Screen.Paint(second);

        Assert.Equal(expected, second.TextColors["2"]);
        Assert.False(second.TextColors.ContainsKey("1"));
    }

    [Theory]
    [InlineData("#D4F53C", AccentColors.DarkOnAccent)]
    [InlineData("#BF2828", AccentColors.LightOnAccent)]
    public void The_check_mark_of_a_finished_step_stands_out(string hex, uint expected)
    {
        using var rig = new ScreenRig(firstRun: true);
        rig.Settings.General.AccentColor = hex;
        rig.Screen.UpdateStatus(ScreenRig.Capturing());
        rig.FocusOn("run.next");
        rig.Press(Vk.Enter);
        var canvas = new FakeCanvas();

        rig.Screen.Paint(canvas);

        // Two strokes of this width: the arrow of the list on that step has a thinner line.
        var mark = canvas.Strokes.Where(s => s.Width == 2).ToList();
        Assert.Equal(2, mark.Count);
        Assert.All(mark, s => Assert.Equal(expected, s.Rgb));
    }

    private (FakeCanvas Canvas, uint Accent) PaintThePrompt(string hex, bool hovered)
    {
        _rig.Settings.General.AccentColor = hex;
        _rig.FocusOn("hotkey.SaveDefault");
        Screen.Key(Vk.Enter, 0);
        Assert.True(Screen.Listening);
        if (hovered)
        {
            var box = Screen.Focus!.Bounds;
            Screen.MouseMove(box.MidX, box.MidY);
        }

        var canvas = new FakeCanvas();
        Screen.Paint(canvas);
        return (canvas, Rgb(hex));
    }

    [Theory]
    [InlineData("#BF2828")]
    [InlineData("#646464")]
    public void The_prompt_of_a_hotkey_field_gives_up_a_dim_accent(string hex)
    {
        var (canvas, _) = PaintThePrompt(hex, hovered: false);

        Assert.Equal(Theme.Text, canvas.TextColors[HotkeyCapture.Prompt]);
    }

    [Fact]
    public void The_prompt_of_a_hotkey_field_keeps_a_bright_accent()
    {
        var (canvas, accent) = PaintThePrompt("#D4F53C", hovered: false);

        Assert.Equal(accent, canvas.TextColors[HotkeyCapture.Prompt]);
    }

    [Fact]
    public void The_prompt_counts_the_lighter_fill_under_the_mouse()
    {
        const string grey = "#737373";
        Assert.True(AccentColors.ContrastRatio(Rgb(grey), Theme.Raised) >= 3);
        Assert.True(AccentColors.ContrastRatio(Rgb(grey), Theme.Hover) < 3);

        Assert.Equal(Rgb(grey), PaintThePrompt(grey, hovered: false).Canvas.TextColors[HotkeyCapture.Prompt]);
        Assert.Equal(Theme.Text, PaintThePrompt(grey, hovered: true).Canvas.TextColors[HotkeyCapture.Prompt]);
    }
}
