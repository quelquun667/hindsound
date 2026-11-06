using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Ui.Window;

/// <summary>What a widget needs to know to paint itself. Coordinates are in design pixels.</summary>
internal readonly record struct UiState(uint Accent, Widget? Focus, Widget? Pressed, Widget? Hover, float MouseX, float MouseY, float Scroll, bool ShowFocus, string? EditingId)
{
    public bool Focused(Widget widget) => ShowFocus && ReferenceEquals(Focus, widget);

    public bool Hovered(Widget widget) => ReferenceEquals(Hover, widget);

    /// <summary>What is laid on the accent: text, the check mark, the knob of a toggle.</summary>
    public uint OnAccent => AccentColors.TextOn(Accent);

    /// <summary>The mouse, in the coordinates this widget is laid out in.</summary>
    public float LocalY(Widget widget) => widget.Scrolls ? MouseY + Scroll : MouseY;

    public void FocusOutline(ICanvas c, Box box, float radius) => c.Ring(box.Inflate(3), radius + 3, 2, Accent);
}

/// <summary>A part of the window. Nothing here touches Win32: the keyboard and the mouse arrive as plain calls.</summary>
internal abstract class Widget
{
    /// <summary>Stable across rebuilds of the page: it is how the focus finds the widget again.</summary>
    public string Id { get; init; } = string.Empty;

    public Box Bounds { get; set; }

    /// <summary>Lives in the scrolled part of the page.</summary>
    public bool Scrolls { get; set; }

    public bool Enabled { get; init; } = true;

    public virtual bool CanFocus => false;

    /// <summary>What must be on screen when the focus lands here.</summary>
    public virtual Box RevealBox => Bounds;

    public abstract void Paint(ICanvas c, UiState ui);

    /// <summary>Returns true if the key did something.</summary>
    public virtual bool OnKey(uint vk) => false;

    public virtual void OnKeyUp(uint vk)
    {
    }

    public virtual void OnPress(float x, float y)
    {
    }

    public virtual void OnDrag(float x, float y)
    {
    }

    /// <param name="inside">The mouse came back up over the widget.</param>
    public virtual void OnRelease(bool inside)
    {
    }
}

/// <summary>Text or a shape that does nothing.</summary>
internal sealed class Painted(Action<ICanvas, UiState> paint) : Widget
{
    public override void Paint(ICanvas c, UiState ui) => paint(c, ui);
}

internal enum ButtonIcon
{
    None,
    Pause,
    Play,
}

internal class Button(string label, Action click) : Widget
{
    public string? SubLabel { get; init; }

    public bool Accented { get; init; }

    public ButtonIcon Icon { get; init; }

    public float FontSize { get; init; } = 14;

    public override bool CanFocus => Enabled;

    public override bool OnKey(uint vk)
    {
        if (vk is Vk.Space or Vk.Enter)
        {
            click();
            return true;
        }

        return false;
    }

    public override void OnRelease(bool inside)
    {
        if (inside)
        {
            click();
        }
    }

    public static float WidthFor(ICanvas c, string label, float size = 14, float padding = 16, bool icon = false) =>
        MathF.Ceiling(c.Measure(label, Face.Medium, size)) + 2 * padding + (icon ? 22 : 0);

    public override void Paint(ICanvas c, UiState ui)
    {
        var box = Bounds;
        if (Accented)
        {
            c.Glow(box, 4, 0.35f, ui.Accent);
            c.FillRound(box, 4, ui.Accent);
            if (SubLabel is null)
            {
                c.TextCentered(label, Face.Medium, FontSize, box, ui.OnAccent);
            }
            else
            {
                var labelWidth = c.Measure(label, Face.Medium, FontSize);
                var subWidth = c.Measure(SubLabel, Face.Mono, 11);
                var x = box.MidX - Math.Max(labelWidth, subWidth) / 2;
                c.TextMid(label, Face.Medium, FontSize, x, box.MidY - 9, ui.OnAccent);
                c.TextMid(SubLabel, Face.Mono, 11, x, box.MidY + 10, ui.OnAccent);
            }
        }
        else
        {
            var fill = !Enabled ? Theme.Panel : ui.Hovered(this) ? Theme.Hover : Theme.Raised;
            c.Frame(box, 4, fill, Enabled ? Theme.Control : Theme.Line);
            var color = Enabled ? Theme.Text : Theme.Faded;
            if (Icon == ButtonIcon.None)
            {
                c.TextCentered(label, Face.Medium, FontSize, box, color);
            }
            else
            {
                var width = c.Measure(label, Face.Medium, FontSize);
                var x = box.MidX - (width + 22) / 2;
                PaintIcon(c, x, box.MidY, color);
                c.TextMid(label, Face.Medium, FontSize, x + 22, box.MidY, color);
            }
        }

        if (ui.Focused(this))
        {
            ui.FocusOutline(c, box, 4);
        }
    }

    private void PaintIcon(ICanvas c, float x, float midY, uint color)
    {
        if (Icon == ButtonIcon.Pause)
        {
            c.FillRound(new Box(x + 2, midY - 6, 3.5f, 12), 0.8f, color);
            c.FillRound(new Box(x + 8.5f, midY - 6, 3.5f, 12), 0.8f, color);
        }
        else
        {
            c.Stroke(x + 3, midY - 5, x + 12, midY, 2.5f, color);
            c.Stroke(x + 12, midY, x + 3, midY + 5, 2.5f, color);
            c.Stroke(x + 3, midY - 5, x + 3, midY + 5, 2.5f, color);
        }
    }
}

/// <summary>Buttons side by side, one of them chosen. One stop for Tab; the arrows move inside.</summary>
internal sealed class Segmented(IReadOnlyList<string> labels, Func<int> selected, Action<int> pick) : Widget
{
    public override bool CanFocus => Enabled;

    public override bool OnKey(uint vk)
    {
        var current = selected();
        var next = vk switch
        {
            Vk.Left or Vk.Up => Math.Max(0, current - 1),
            Vk.Right or Vk.Down => Math.Min(labels.Count - 1, current + 1),
            Vk.Home => 0,
            Vk.End => labels.Count - 1,
            _ => -1,
        };
        if (next < 0)
        {
            return false;
        }

        if (next != current)
        {
            pick(next);
        }

        return true;
    }

    public override void OnPress(float x, float y)
    {
        if (Enabled)
        {
            pick(Math.Clamp((int)((x - Bounds.X) / (Bounds.W / labels.Count)), 0, labels.Count - 1));
        }
    }

    public override void Paint(ICanvas c, UiState ui)
    {
        var box = Bounds;
        c.Frame(box, 4, Enabled ? Theme.Raised : Theme.Panel, Enabled ? Theme.Control : Theme.Line);
        var width = box.W / labels.Count;
        var chosen = selected();
        for (var i = 0; i < labels.Count; i++)
        {
            var cell = new Box(box.X + MathF.Round(i * width), box.Y, MathF.Round((i + 1) * width) - MathF.Round(i * width), box.H);
            if (i > 0)
            {
                c.FillRect(new Box(cell.X, box.Y, 1, box.H), Enabled ? Theme.Control : Theme.Line);
            }

            if (i == chosen)
            {
                var fill = Enabled ? ui.Accent : Theme.Line;
                c.FillRound(cell, i == 0 || i == labels.Count - 1 ? 4 : 0, fill);
                if (i == 0 && labels.Count > 1)
                {
                    c.FillRect(new Box(cell.MidX, cell.Y, cell.W / 2, cell.H), fill);
                }
                else if (i == labels.Count - 1 && labels.Count > 1)
                {
                    c.FillRect(new Box(cell.X, cell.Y, cell.W / 2, cell.H), fill);
                }
            }

            var color = i == chosen ? (Enabled ? ui.OnAccent : Theme.Muted) : Enabled ? Theme.Text : Theme.Faded;
            c.TextCentered(labels[i], Face.Medium, 14, cell, color);
        }

        if (ui.Focused(this))
        {
            ui.FocusOutline(c, box, 4);
        }
    }
}

/// <summary>A value on a track. The value moves while dragging or on arrow keys, and is kept when the mouse or the key comes up.</summary>
internal sealed class Slider(double min, double max, double step, Func<double> value, Action<double> preview, Action commit, string text) : Widget
{
    private const float TextWidth = 60;

    public override bool CanFocus => Enabled;

    private Box Track => new(Bounds.X, Bounds.MidY - 4, Bounds.W - TextWidth, 8);

    private float ThumbX => Track.X + 8 + (float)Math.Clamp((value() - min) / (max - min), 0, 1) * (Track.W - 16);

    public override bool OnKey(uint vk)
    {
        var current = value();
        double? next = vk switch
        {
            Vk.Left or Vk.Down => current - step,
            Vk.Right or Vk.Up => current + step,
            Vk.PageUp => current + 5 * step,
            Vk.PageDown => current - 5 * step,
            Vk.Home => min,
            Vk.End => max,
            _ => null,
        };
        if (next is not { } target)
        {
            return false;
        }

        preview(Math.Clamp(target, min, max));
        return true;
    }

    public override void OnKeyUp(uint vk)
    {
        if (vk is Vk.Left or Vk.Down or Vk.Right or Vk.Up or Vk.PageUp or Vk.PageDown or Vk.Home or Vk.End)
        {
            commit();
        }
    }

    public override void OnPress(float x, float y) => OnDrag(x, y);

    public override void OnDrag(float x, float y)
    {
        if (!Enabled)
        {
            return;
        }

        var share = Math.Clamp((x - Track.X - 8) / (Track.W - 16), 0, 1);
        var raw = min + share * (max - min);
        preview(Math.Clamp(min + Math.Round((raw - min) / step) * step, min, max));
    }

    public override void OnRelease(bool inside) => commit();

    public override void Paint(ICanvas c, UiState ui)
    {
        var track = Track;
        var thumb = ThumbX;
        c.Frame(track, 4, Theme.Line, Enabled ? Theme.Control : Theme.Line);
        var color = Enabled ? ui.Accent : Theme.Control;
        c.FillRound(new Box(track.X, track.Y, thumb - track.X, track.H), 4, color);
        if (Enabled)
        {
            c.Glow(new Box(thumb - 8, track.MidY - 8, 16, 16), 3, 0.3f, ui.Accent);
        }

        var knob = new Box(thumb - 8, track.MidY - 8, 16, 16);
        c.FillRound(knob, 8, color);
        c.TextRight(text, Face.Mono, 14, Bounds.Right, track.MidY, Enabled ? Theme.Text : Theme.Faded);
        if (ui.Focused(this))
        {
            ui.FocusOutline(c, knob, 8);
        }
    }
}

internal sealed class Toggle(Func<bool> on, Action<bool> set) : Widget
{
    public override bool CanFocus => Enabled;

    public override bool OnKey(uint vk)
    {
        if (vk is Vk.Space or Vk.Enter)
        {
            set(!on());
            return true;
        }

        return false;
    }

    public override void OnRelease(bool inside)
    {
        if (inside && Enabled)
        {
            set(!on());
        }
    }

    public override void Paint(ICanvas c, UiState ui)
    {
        var box = Bounds;
        if (on())
        {
            c.FillRound(box, 11, ui.Accent);
            c.FillRound(new Box(box.Right - 19, box.Y + 3, 16, 16), 8, ui.OnAccent);
        }
        else
        {
            c.Frame(box, 11, Theme.Raised, Theme.Control);
            c.FillRound(new Box(box.X + 3, box.Y + 3, 16, 16), 8, Theme.Muted);
        }

        if (ui.Focused(this))
        {
            ui.FocusOutline(c, box, 11);
        }
    }
}

/// <summary>A closed list. The list that opens is drawn by the screen, over everything.</summary>
internal sealed class Dropdown(Func<IReadOnlyList<DropItem>> items, Func<string> selected, Action<string> pick, Action<Dropdown> open) : Widget
{
    public IReadOnlyList<DropItem> Items => items();

    public int SelectedIndex
    {
        get
        {
            var value = selected();
            var list = items();
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }
    }

    public void Pick(int index) => pick(items()[index].Value);

    public void PickValue(string value) => pick(value);

    public override bool CanFocus => Enabled;

    public override bool OnKey(uint vk)
    {
        switch (vk)
        {
            case Vk.Space:
            case Vk.Enter:
                open(this);
                return true;
            case Vk.Up:
            case Vk.Left:
                Step(-1);
                return true;
            case Vk.Down:
            case Vk.Right:
                Step(1);
                return true;
            case Vk.Home:
                Step(-items().Count);
                return true;
            case Vk.End:
                Step(items().Count);
                return true;
            default:
                return false;
        }
    }

    private void Step(int by)
    {
        var next = Math.Clamp(SelectedIndex + by, 0, items().Count - 1);
        if (next != SelectedIndex)
        {
            Pick(next);
        }
    }

    public override void OnPress(float x, float y)
    {
        if (Enabled)
        {
            open(this);
        }
    }

    public override void Paint(ICanvas c, UiState ui)
    {
        var box = Bounds;
        var fill = !Enabled ? Theme.Panel : ui.Hovered(this) ? Theme.Hover : Theme.Raised;
        c.Frame(box, 4, fill, Enabled ? Theme.Control : Theme.Line);
        var list = items();
        var label = list.Count == 0 ? string.Empty : list[Math.Min(SelectedIndex, list.Count - 1)].Label;
        var color = Enabled ? Theme.Text : Theme.Faded;
        c.TextMid(c.Fit(label, Face.Body, 13, box.W - 34), Face.Body, 13, box.X + 10, box.MidY, color);
        var cx = box.Right - 14;
        c.Stroke(cx - 4, box.MidY - 2, cx, box.MidY + 2, 1.6f, color);
        c.Stroke(cx, box.MidY + 2, cx + 4, box.MidY - 2, 1.6f, color);
        if (ui.Focused(this))
        {
            ui.FocusOutline(c, box, 4);
        }
    }
}

/// <summary>What a text field holds apart from the settings: the text as typed, even when it cannot be kept.</summary>
internal sealed class FieldState(string draft)
{
    public string Draft { get; set; } = draft;

    public string? Message { get; set; }
}

/// <summary>
/// A text field. While it has the focus a native EDIT lies over it, which gives copy, paste and selection for free;
/// otherwise the text is simply drawn. An invalid value stays visible and flagged, and never reaches the settings.
/// </summary>
internal sealed class TextField(FieldState state, Func<string, FieldCheck> check, Action<string>? keep, Func<string> stored) : Widget
{
    public const float MessageHeight = 20;

    public FieldState State => state;

    public Face Face { get; init; } = Face.Mono;

    public float Size { get; init; } = 13;

    /// <summary>Shown under the field while the value is fine.</summary>
    public string? Help { get; init; }

    public Box TextBox => new(Bounds.X + 8, Bounds.Y + 2, Bounds.W - 16, Bounds.H - 4);

    public override bool CanFocus => Enabled;

    public override Box RevealBox => new(Bounds.X, Bounds.Y, Bounds.W, Bounds.H + MessageHeight);

    /// <summary>The text changed: the verdict is live, and a valid value goes in straight away if this field says so.</summary>
    public void Typed(string text, Action<string>? keepNow)
    {
        state.Draft = text;
        var verdict = check(text);
        state.Message = verdict.Message;
        if (verdict.Ok)
        {
            keepNow?.Invoke(text);
        }
    }

    /// <summary>Enter, or the focus leaving.</summary>
    public void Commit()
    {
        var verdict = check(state.Draft);
        state.Message = verdict.Message;
        if (verdict.Ok)
        {
            keep?.Invoke(state.Draft);
        }
    }

    public void Revert()
    {
        state.Draft = stored();
        state.Message = null;
    }

    public override void Paint(ICanvas c, UiState ui)
    {
        var box = Bounds;
        var bad = state.Message is not null;
        c.Frame(box, 4, Theme.Raised, bad ? Theme.Hot : Theme.Control);
        if (ui.EditingId != Id)
        {
            c.TextMid(c.Fit(state.Draft, Face, Size, box.W - 16), Face, Size, box.X + 8, box.MidY, Theme.Text);
        }

        if (ui.Focused(this))
        {
            ui.FocusOutline(c, box, 4);
        }

        var line = box.Bottom + MessageHeight / 2;
        if (bad)
        {
            Message.Error(c, state.Message!, box.X, line);
        }
        else if (Help is not null)
        {
            c.TextMid(Help, Face.Body, 13, box.X, line, Theme.Muted);
        }
    }
}

/// <summary>A sentence that says something is wrong: the triangle doubles the colour.</summary>
internal static class Message
{
    public static void Error(ICanvas c, string text, float x, float midY)
    {
        c.WarningIcon(x, midY - 7, 14, Theme.Hot);
        c.TextMid(text, Face.Body, 13, x + 18, midY, Theme.Hot);
    }
}

/// <summary>What a hotkey field shows at this moment.</summary>
internal readonly record struct HotkeyFieldView(string? Gesture, bool Listening, string? Held, string? Message, bool Failed);

/// <summary>The drawn keys of a hotkey. Click, Enter or Space starts "Press your combo...".</summary>
internal sealed class HotkeyField(HotkeyAction action, Func<HotkeyFieldView> view, Action<HotkeyAction> begin) : Widget
{
    public const float MessageHeight = 20;

    public override bool CanFocus => Enabled;

    public override Box RevealBox => new(Bounds.X, Bounds.Y, Bounds.W, Bounds.H + MessageHeight);

    public override bool OnKey(uint vk)
    {
        if (vk is Vk.Enter or Vk.Space)
        {
            begin(action);
            return true;
        }

        return false;
    }

    public override void OnPress(float x, float y) => begin(action);

    public override void Paint(ICanvas c, UiState ui)
    {
        var box = Bounds;
        var state = view();
        var border = state.Listening ? ui.Accent : state.Failed ? Theme.Hot : Theme.Control;
        var fill = ui.Hovered(this) ? Theme.Hover : Theme.Raised;
        c.Frame(box, 4, fill, border);
        var x = box.X + 8;
        if (state.Listening)
        {
            if (string.IsNullOrEmpty(state.Held))
            {
                c.TextMid(HotkeyCapture.Prompt, Face.Body, 13, x, box.MidY, AccentColors.AccentAsText(ui.Accent, fill, Theme.Text));
            }
            else
            {
                c.Keycaps(state.Held, x, box.MidY);
            }
        }
        else if (state.Gesture is null)
        {
            c.TextMid("Not set", Face.Body, 13, x, box.MidY, Theme.Muted);
        }
        else
        {
            c.Keycaps(state.Gesture, x, box.MidY);
        }

        if (ui.Focused(this))
        {
            ui.FocusOutline(c, box, 4);
        }

        var line = box.Bottom + MessageHeight / 2;
        if (state.Listening && state.Message is null)
        {
            c.TextMid("Esc cancels, Backspace clears", Face.Body, 13, box.X, line, Theme.Muted);
        }
        else if (state.Message is not null)
        {
            Message.Error(c, state.Message, box.X, line);
        }
    }
}

/// <summary>The sections of the side bar. One stop for Tab; the arrows change the section.</summary>
internal sealed class NavList(IReadOnlyList<string> labels, Func<int> selected, Action<int> pick) : Widget
{
    private const float ItemHeight = 36, Gap = 4;

    public override bool CanFocus => true;

    public override bool OnKey(uint vk)
    {
        var current = selected();
        var next = vk switch
        {
            Vk.Up or Vk.Left => Math.Max(0, current - 1),
            Vk.Down or Vk.Right => Math.Min(labels.Count - 1, current + 1),
            Vk.Home => 0,
            Vk.End => labels.Count - 1,
            _ => -1,
        };
        if (next < 0)
        {
            return false;
        }

        if (next != current)
        {
            pick(next);
        }

        return true;
    }

    private int IndexAt(float y)
    {
        var index = (int)((y - Bounds.Y) / (ItemHeight + Gap));
        return index >= 0 && index < labels.Count && y - Bounds.Y - index * (ItemHeight + Gap) < ItemHeight ? index : -1;
    }

    public override void OnPress(float x, float y)
    {
        var index = IndexAt(y);
        if (index >= 0)
        {
            pick(index);
        }
    }

    public override void Paint(ICanvas c, UiState ui)
    {
        var chosen = selected();
        var hover = ui.Hovered(this) ? IndexAt(ui.MouseY) : -1;
        for (var i = 0; i < labels.Count; i++)
        {
            var item = new Box(Bounds.X, Bounds.Y + i * (ItemHeight + Gap), Bounds.W, ItemHeight);
            var on = i == chosen;
            if (on)
            {
                c.FillRound(item, 4, Theme.Raised);
                c.FillRound(new Box(item.X, item.Y + 8, 3, ItemHeight - 16), 1.5f, ui.Accent);
            }
            else if (i == hover)
            {
                c.FillRound(item, 4, Theme.Raised);
            }

            c.TextMid(labels[i], Face.Medium, 14, item.X + 14, item.MidY, on || i == hover ? Theme.Text : Theme.Muted);
            if (on && ui.Focused(this))
            {
                ui.FocusOutline(c, item, 4);
            }
        }
    }
}

/// <summary>The 6 colours and Custom, as one stop for Tab. The arrows choose as they move.</summary>
internal sealed class SwatchGroup(IReadOnlyList<uint> colors, IReadOnlyList<string> names, Func<int> selected, Action<int> pick) : Widget
{
    private const float Size = 30, Gap = 14, CustomGap = 18, CustomWidth = 70;

    public override bool CanFocus => true;

    private int Count => colors.Count + 1;

    public override bool OnKey(uint vk)
    {
        var current = selected();
        var next = vk switch
        {
            Vk.Left or Vk.Up => Math.Max(0, current - 1),
            Vk.Right or Vk.Down => Math.Min(Count - 1, current + 1),
            Vk.Home => 0,
            Vk.End => Count - 1,
            _ => -1,
        };
        if (next < 0)
        {
            return false;
        }

        if (next != current)
        {
            pick(next);
        }

        return true;
    }

    private Box ItemBox(int index) => index < colors.Count
        ? new Box(Bounds.X + index * (Size + Gap), Bounds.Y, Size, Size)
        : new Box(Bounds.X + colors.Count * (Size + Gap) - Gap + CustomGap, Bounds.Y, CustomWidth, Size);

    public override void OnPress(float x, float y)
    {
        for (var i = 0; i < Count; i++)
        {
            if (ItemBox(i).Inflate(3).Contains(x, y))
            {
                pick(i);
                return;
            }
        }
    }

    public override void Paint(ICanvas c, UiState ui)
    {
        var chosen = selected();
        for (var i = 0; i < colors.Count; i++)
        {
            var box = ItemBox(i);
            c.FillRound(box, Size / 2, colors[i]);
            if (i == chosen)
            {
                c.Ring(box.Inflate(4), Size / 2 + 4, 2, Theme.Text);
                if (ui.Focused(this))
                {
                    c.Ring(box.Inflate(7), Size / 2 + 7, 2, ui.Accent);
                }
            }
        }

        var custom = ItemBox(colors.Count);
        c.Frame(custom, 4, chosen == colors.Count ? Theme.Hover : Theme.Raised, chosen == colors.Count ? Theme.Text : Theme.Control);
        c.TextCentered(names[^1], Face.Medium, 13, custom, Theme.Text);
        if (chosen == colors.Count && ui.Focused(this))
        {
            ui.FocusOutline(c, custom, 4);
        }
    }
}

/// <summary>The level of the microphone: nothing is drawn but a bar.</summary>
internal sealed class MeterBar(Func<float> level, Func<string?> caption) : Widget
{
    private const int Cells = 32;

    public override void Paint(ICanvas c, UiState ui)
    {
        var note = caption();
        if (note is not null)
        {
            c.TextMid(note, Face.Body, 13, Bounds.X, Bounds.MidY, Theme.Muted);
            return;
        }

        var lit = (int)MathF.Round(level() * Cells);
        var width = Bounds.W / Cells;
        for (var i = 0; i < Cells; i++)
        {
            var left = MathF.Round(Bounds.X + i * width);
            var right = MathF.Round(Bounds.X + (i + 1) * width) - 2;
            c.FillRect(new Box(left, Bounds.Y, right - left, Bounds.H), i < lit ? ui.Accent : Theme.Line);
        }
    }
}
