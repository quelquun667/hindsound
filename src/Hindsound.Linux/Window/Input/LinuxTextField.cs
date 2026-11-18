using Hindsound.Ui.Window;

namespace Hindsound.Linux.Window.Input;

/// <summary>
/// The text field of the Linux window, drawn on the page where Windows puts its native EDIT box, and looking as that box
/// does: one line, as tall as 1.5 times the size, centred on the field; the theme's raised colour behind the text colour; the
/// face of the field (mono, or the body face for the rest); the selection in the highlight colour of Windows while the field
/// has the keyboard; a caret of one pixel, blinking on a timer the window runs; the text scrolled by a third of the width
/// to keep the caret in view, as ES_AUTOHSCROLL does. What the box keeps and what it gives back is <see cref="TextFieldModel"/>.
/// <para>
/// Plain values in and out: the window turns its SDL events into virtual keys (<see cref="SdlKeys"/>), text and composition,
/// and repaints the field's box when <see cref="Repaint"/> asks. A key the field does not take (Tab, Enter, Escape, or any
/// key while it does not have the keyboard) goes to the screen, as on Windows.
/// </para>
/// </summary>
internal sealed class LinuxTextField
{
    /// <summary>The caret blink of Windows (GetCaretBlinkTime): the window calls <see cref="Blink"/> this often.</summary>
    public const int BlinkMilliseconds = 530;

    /// <summary>COLOR_HIGHLIGHT and COLOR_HIGHLIGHTTEXT of Windows 10 and 11.</summary>
    public const uint Highlight = 0x0078D7, HighlightText = 0xFFFFFF;

    // Wine's edit control scrolls by this fraction of the width (HSCROLL_FRACTION).
    private const int ScrollFraction = 3;

    private readonly TextFieldModel _model;

    private Face _face = Face.Body;
    private float _size = 13;
    private bool _dragging;
    private string _composition = string.Empty;
    private int _compositionCursor;

    // Where the last paint put things, for the mouse and the input method. Design units, except the area.
    private bool _painted;
    private float _textLeft;
    private float[] _edges = [0];
    private (string Text, Face Face, float Size, double Scale) _edgesKey;
    private int _first;
    private (int X, int Y, int W, int H) _area;
    private float _scale = 1;
    private Box _control;

    /// <param name="readClipboard">The text on the clipboard, or null (<c>SdlClipboardText.Get</c>).</param>
    /// <param name="writeClipboard">Puts text on the clipboard (<c>SdlClipboardText.Set</c>).</param>
    public LinuxTextField(Func<string?> readClipboard, Action<string> writeClipboard) =>
        _model = new TextFieldModel(readClipboard, writeClipboard);

    /// <summary>The user changed the text: the window passes it to <c>SettingsScreen.EditChanged</c>, as EN_CHANGE does.</summary>
    public Action<string>? Changed { get; set; }

    /// <summary>The field looks different: the window repaints <see cref="Box"/>.</summary>
    public Action? Repaint { get; set; }

    public TextFieldModel Model => _model;

    public string Text => _model.Text;

    /// <summary>The field's box on the page, in design units, as the screen gave it.</summary>
    public Box Box { get; private set; }

    public bool Visible { get; private set; }

    /// <summary>The field has the keyboard.</summary>
    public bool Focused { get; private set; }

    /// <summary>The caret is in its shown half of the blink.</summary>
    public bool CaretOn { get; private set; } = true;

    /// <summary>The window's text input should be on: typed text and composition come to this field.</summary>
    public bool WantsTextInput => Visible && Focused;

    /// <summary>What the input method is composing, shown at the caret until it is typed.</summary>
    public string ComposingText => _composition;

    /// <summary>ShowEdit: the box is placed on <paramref name="box"/>; a reset puts <paramref name="text"/> in it, all selected.</summary>
    public void Show(Box box, string text, Face face, float size, bool resetText)
    {
        Box = box;
        _face = face == Face.Mono ? Face.Mono : Face.Body;
        _size = size;
        if (resetText)
        {
            _model.SetText(text, selectAll: true);
            _composition = string.Empty;
            _first = 0;
        }

        Visible = true;
        Repaint?.Invoke();
    }

    /// <summary>HideEdit: the box goes, and the keyboard with it.</summary>
    public void Hide()
    {
        if (!Visible)
        {
            return;
        }

        Visible = false;
        Focused = false;
        _dragging = false;
        _composition = string.Empty;
        Repaint?.Invoke();
    }

    /// <summary>FocusEdit: the field takes the keyboard.</summary>
    public void Focus()
    {
        Focused = true;
        Wake();
    }

    /// <summary>
    /// The window lost the keyboard: the field shows no caret and no selection until it gets it back, and what the input
    /// method was composing is dropped, as it is told to stop, but the field no longer takes the event that clears it.
    /// </summary>
    public void Blur()
    {
        Focused = false;
        _dragging = false;
        _composition = string.Empty;
        _compositionCursor = 0;
        Repaint?.Invoke();
    }

    /// <summary>The window's blink timer: flips the caret while the field has the keyboard.</summary>
    public void Blink()
    {
        if (WantsTextInput)
        {
            CaretOn = !CaretOn;
            Repaint?.Invoke();
        }
    }

    /// <summary>A key went down. True when the field took it; false sends it to the screen.</summary>
    /// <param name="vk">The Windows virtual key (<see cref="SdlKeys.VirtualKey"/>).</param>
    /// <param name="modifiers">The <c>HotkeyGesture</c> flags (<see cref="SdlKeys.Modifiers"/>).</param>
    public bool KeyDown(uint vk, uint modifiers)
    {
        if (!WantsTextInput)
        {
            return false;
        }

        // The input method is composing: its keys are its own.
        if (_composition.Length > 0)
        {
            return true;
        }

        var result = _model.Key(vk, modifiers);
        if (result == TextKeyResult.NotMine)
        {
            return false;
        }

        Wake();
        if (result == TextKeyResult.Changed)
        {
            Changed?.Invoke(_model.Text);
        }

        return true;
    }

    /// <summary>Text typed or committed by the input method (SDL_EVENT_TEXT_INPUT).</summary>
    public void TextInput(string text)
    {
        if (!WantsTextInput)
        {
            return;
        }

        _composition = string.Empty;
        var changed = _model.Type(text);
        Wake();
        if (changed)
        {
            Changed?.Invoke(_model.Text);
        }
    }

    /// <summary>What the input method is composing (SDL_EVENT_TEXT_EDITING); empty when it is done or gave up.</summary>
    /// <param name="cursor">Its cursor, in characters (code points) from the start of the text, as SDL counts; -1 for the end.</param>
    public void Composition(string text, int cursor)
    {
        if (!WantsTextInput)
        {
            return;
        }

        _composition = text;
        _compositionCursor = CodePointsToIndex(text, cursor);
        Wake();
    }

    /// <summary>A press of the left button. True when it is on the box: the field takes the keyboard and places the caret.</summary>
    /// <param name="clicks">2 for a double click, which selects a word.</param>
    public bool MouseDown(float x, float y, bool shift, int clicks)
    {
        if (!Visible || !_painted || !Contains(_control, x, y))
        {
            return false;
        }

        Focused = true;
        if (_composition.Length == 0)
        {
            var at = IndexAt(x);
            if (clicks >= 2)
            {
                _model.SelectWordAt(at);
            }
            else
            {
                _model.Select(shift ? _model.Anchor : at, at);
                _dragging = true;
            }
        }

        Wake();
        return true;
    }

    /// <summary>The mouse moved: a drag that began on the box moves the end of the selection. True while dragging.</summary>
    public bool MouseMove(float x)
    {
        if (!_dragging)
        {
            return false;
        }

        _model.Select(_model.Anchor, IndexAt(x));
        Wake();
        return true;
    }

    public void MouseUp() => _dragging = false;

    /// <summary>Where the input method puts its window: the box, and the caret from its left, in physical pixels.</summary>
    public (int X, int Y, int W, int H, int Cursor) InputArea()
    {
        var caret = _painted ? _textLeft + Edge(CaretIndex) - Edge(_first) - _control.X : 0;
        return (_area.X, _area.Y, _area.W, _area.H, (int)MathF.Round(caret * _scale));
    }

    public void Paint(ICanvas canvas)
    {
        if (!Visible)
        {
            return;
        }

        _scale = (float)canvas.Scale;
        var lineHeight = MathF.Ceiling(_size * _scale * 1.5f);
        var left = MathF.Round(Box.X * _scale);
        var width = MathF.Round(Box.W * _scale);
        var top = MathF.Round(Box.MidY * _scale - lineHeight / 2f);
        _area = ((int)left, (int)top, (int)width, (int)lineHeight);
        _control = new Box(left / _scale, top / _scale, width / _scale, lineHeight / _scale);
        canvas.FillRect(_control, Theme.Raised);

        var margin = MathF.Max(1, MathF.Round(_size * _scale / 8)) / _scale;
        var inner = MathF.Max(0, _control.W - 2 * margin);
        var display = DisplayText();
        Measure(canvas, display);
        _textLeft = _control.X + margin;
        _painted = true;
        Scroll(display.Length, inner);

        var last = _first;
        while (last < display.Length && Edge(last + 1) - Edge(_first) <= inner + 0.01f)
        {
            last++;
        }

        var textTop = _control.Y;
        var textHeight = canvas.LineHeight(_face, _size);
        var composing = _composition.Length > 0;
        var (selStart, selEnd) = composing || !Focused ? (0, 0) : (_model.SelectionStart, _model.SelectionEnd);
        var a = Math.Clamp(selStart, _first, last);
        var b = Math.Clamp(selEnd, _first, last);
        if (b > a)
        {
            canvas.FillRect(new Box(X(a), textTop, Edge(b) - Edge(a), textHeight), Highlight);
        }

        Segment(canvas, display, _first, a, Theme.Text, textTop);
        Segment(canvas, display, a, b, HighlightText, textTop);
        Segment(canvas, display, b, last, Theme.Text, textTop);

        if (composing)
        {
            var from = Math.Clamp(_model.SelectionStart, _first, last);
            var to = Math.Clamp(_model.SelectionStart + _composition.Length, _first, last);
            if (to > from)
            {
                canvas.FillRect(new Box(X(from), textTop + textHeight - 1 / _scale, Edge(to) - Edge(from), 1 / _scale), Theme.Text);
            }
        }

        var caret = CaretIndex;
        if (Focused && CaretOn && caret >= _first && caret <= last)
        {
            canvas.FillRect(new Box(X(caret), textTop, 1 / _scale, textHeight), Theme.Text);
        }
    }

    // ---------------------------------------------------------------- inside

    private int CaretIndex => _composition.Length > 0 ? _model.SelectionStart + _compositionCursor : _model.Caret;

    private string DisplayText() => _composition.Length == 0
        ? _model.Text
        : string.Concat(_model.Text.AsSpan(0, _model.SelectionStart), _composition, _model.Text.AsSpan(_model.SelectionEnd));

    private float Edge(int index) => _edges[Math.Clamp(index, 0, _edges.Length - 1)];

    private float X(int index) => _textLeft + Edge(index) - Edge(_first);

    // Every place the caret can stand, measured once per text, face, size and scale: a paint per blink costs nothing then.
    private void Measure(ICanvas canvas, string display)
    {
        var key = (display, _face, _size, canvas.Scale);
        if (_edgesKey == key && _edges.Length == display.Length + 1)
        {
            return;
        }

        var edges = new float[display.Length + 1];
        for (var i = 1; i <= display.Length; i++)
        {
            edges[i] = char.IsLowSurrogate(display[i - 1]) || i == display.Length || !char.IsLowSurrogate(display[i])
                ? canvas.Measure(display[..i], _face, _size)
                : edges[i - 1];
        }

        _edges = edges;
        _edgesKey = key;
    }

    // The first character shown moves so that the caret stays in view, by a third of the width at a time.
    private void Scroll(int length, float inner)
    {
        _first = Math.Clamp(_first, 0, length);
        if (Edge(length) - Edge(0) <= inner)
        {
            _first = 0;
            return;
        }

        var caret = Math.Clamp(CaretIndex, 0, length);
        if (caret < _first)
        {
            var goal = inner / ScrollFraction;
            do
            {
                _first--;
            }
            while (_first > 0 && Edge(caret) - Edge(_first) < goal);
        }
        else if (Edge(caret) - Edge(_first) > inner)
        {
            var goal = inner - inner / ScrollFraction;
            do
            {
                _first++;
            }
            while (_first < caret && Edge(caret) - Edge(_first) > goal);
        }
    }

    private void Segment(ICanvas canvas, string display, int from, int to, uint rgb, float top)
    {
        if (to > from)
        {
            canvas.Text(display[from..to], _face, _size, X(from), top, rgb);
        }
    }

    // The place between two characters nearest to x.
    private int IndexAt(float x)
    {
        var target = x - _textLeft + Edge(_first);
        var best = 0;
        for (var i = 1; i < _edges.Length && i <= _model.Text.Length; i++)
        {
            if (MathF.Abs(_edges[i] - target) < MathF.Abs(_edges[best] - target))
            {
                best = i;
            }
        }

        return best;
    }

    private void Wake()
    {
        CaretOn = true;
        Repaint?.Invoke();
    }

    private static bool Contains(Box box, float x, float y) => x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom;

    private static int CodePointsToIndex(string text, int codePoints)
    {
        if (codePoints < 0)
        {
            return text.Length;
        }

        var index = 0;
        for (var n = 0; n < codePoints && index < text.Length; n++)
        {
            index += char.IsSurrogatePair(text, index) ? 2 : 1;
        }

        return index;
    }
}
