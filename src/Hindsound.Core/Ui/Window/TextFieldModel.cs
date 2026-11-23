using Hindsound.Input;

namespace Hindsound.Ui.Window;

/// <summary>What a key did to a <see cref="TextFieldModel"/>.</summary>
internal enum TextKeyResult
{
    /// <summary>The field does not take this key: it goes to the screen (Tab, Enter, Escape).</summary>
    NotMine,

    /// <summary>The field took the key and its text stayed the same: the caret or the selection may have moved.</summary>
    Handled,

    /// <summary>The field took the key and its text changed.</summary>
    Changed,
}

/// <summary>
/// The state of one single-line text field, as the native EDIT of Windows keeps it for the settings window: the text, the
/// caret and the selection, and what each key does to them. A window without a native box (the Linux one) draws its field
/// from this. The text is at most <see cref="MaxLength"/> characters when typed or pasted, as the Windows box is
/// (EM_SETLIMITTEXT 256); text set by the program is not cut, as WM_SETTEXT is not.
/// The keys the box gives back to the screen are Tab, Enter and Escape, with any modifiers. Every other key is the box's,
/// even one it does nothing with, as a focused EDIT never passes keys to its parent.
/// </summary>
internal sealed class TextFieldModel
{
    /// <summary>The limit of the Windows box.</summary>
    public const int MaxLength = 256;

    private readonly Func<string?> _readClipboard;
    private readonly Action<string> _writeClipboard;

    // One level of undo, as the EDIT keeps: Ctrl+Z goes back to it, and a second Ctrl+Z comes back again.
    private string? _undoText;
    private int _undoAnchor;
    private int _undoCaret;

    /// <param name="readClipboard">The text on the clipboard, or null when it holds none.</param>
    /// <param name="writeClipboard">Puts text on the clipboard.</param>
    public TextFieldModel(Func<string?> readClipboard, Action<string> writeClipboard)
    {
        _readClipboard = readClipboard;
        _writeClipboard = writeClipboard;
    }

    public string Text { get; private set; } = string.Empty;

    /// <summary>Where the caret is: the moving end of the selection.</summary>
    public int Caret { get; private set; }

    /// <summary>The fixed end of the selection. Equal to <see cref="Caret"/> when nothing is selected.</summary>
    public int Anchor { get; private set; }

    public int SelectionStart => Math.Min(Anchor, Caret);

    public int SelectionEnd => Math.Max(Anchor, Caret);

    public bool HasSelection => Anchor != Caret;

    public string SelectedText => Text[SelectionStart..SelectionEnd];

    /// <summary>The program sets the text, as SetWindowText does: no limit, no undo kept, and not a change of the user.</summary>
    /// <param name="selectAll">Select it all, as the window does with EM_SETSEL 0, -1 after a reset; else the caret goes to the end.</param>
    public void SetText(string text, bool selectAll)
    {
        Text = text;
        _undoText = null;
        Caret = text.Length;
        Anchor = selectAll ? 0 : text.Length;
    }

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="caret"/>, each kept inside the text.</summary>
    public void Select(int anchor, int caret)
    {
        Anchor = Math.Clamp(anchor, 0, Text.Length);
        Caret = Math.Clamp(caret, 0, Text.Length);
    }

    public void SelectAll() => Select(0, Text.Length);

    /// <summary>
    /// Characters typed. A control character is dropped, as the box drops it, and the text stops at a line break: the box
    /// has one line. What would pass the limit is cut. Returns true when the text changed.
    /// </summary>
    public bool Type(string typed) => Replace(Clean(typed));

    /// <summary>A key went down while the field has the keyboard.</summary>
    /// <param name="vk">The Windows virtual key.</param>
    /// <param name="modifiers">The modifiers held, as <see cref="HotkeyGesture"/> flags.</param>
    public TextKeyResult Key(uint vk, uint modifiers)
    {
        if (vk is Vk.Tab or Vk.Enter or Vk.Escape)
        {
            return TextKeyResult.NotMine;
        }

        var ctrl = (modifiers & HotkeyGesture.ModControl) != 0;
        var shift = (modifiers & HotkeyGesture.ModShift) != 0;
        var alt = (modifiers & HotkeyGesture.ModAlt) != 0;
        if (alt || (modifiers & HotkeyGesture.ModWin) != 0)
        {
            // The box has no Alt or Win keys of its own.
            return TextKeyResult.Handled;
        }

        switch (vk)
        {
            case Vk.Left or Vk.Up:
                Move(ctrl ? WordBefore(Caret) : !shift && HasSelection ? SelectionStart : Caret - 1, shift);
                return TextKeyResult.Handled;
            case Vk.Right or Vk.Down:
                Move(ctrl ? WordAfter(Caret) : !shift && HasSelection ? SelectionEnd : Caret + 1, shift);
                return TextKeyResult.Handled;
            case Vk.Home:
                Move(0, shift);
                return TextKeyResult.Handled;
            case Vk.End:
                Move(Text.Length, shift);
                return TextKeyResult.Handled;
            case Vk.Back:
                return Result(HasSelection ? Replace(string.Empty) : DeleteTo(ctrl ? WordBefore(Caret) : Caret - 1));
            case VkDelete when shift && !ctrl:
                return Result(Cut());
            case VkDelete:
                return Result(HasSelection ? Replace(string.Empty) : DeleteTo(ctrl ? WordAfter(Caret) : Caret + 1));
            case VkInsert when ctrl && !shift:
                Copy();
                return TextKeyResult.Handled;
            case VkInsert when shift && !ctrl:
                return Result(Paste());
        }

        if (ctrl && !shift)
        {
            switch (vk)
            {
                case 'A':
                    SelectAll();
                    return TextKeyResult.Handled;
                case 'C':
                    Copy();
                    return TextKeyResult.Handled;
                case 'X':
                    return Result(Cut());
                case 'V':
                    return Result(Paste());
                case 'Z':
                    return Result(Undo());
            }
        }

        return TextKeyResult.Handled;
    }

    /// <summary>Puts the selection on the clipboard. Nothing selected puts nothing there.</summary>
    public void Copy()
    {
        if (HasSelection)
        {
            _writeClipboard(SelectedText);
        }
    }

    public bool Cut()
    {
        if (!HasSelection)
        {
            return false;
        }

        _writeClipboard(SelectedText);
        return Replace(string.Empty);
    }

    public bool Paste() => _readClipboard() is { Length: > 0 } text && Replace(Clean(text));

    /// <summary>Back to the text before the last change, and the change kept to come back to.</summary>
    public bool Undo()
    {
        if (_undoText is null)
        {
            return false;
        }

        var (text, anchor, caret) = (_undoText, _undoAnchor, _undoCaret);
        Remember();
        Text = text;
        Anchor = anchor;
        Caret = caret;
        return true;
    }

    /// <summary>The start of the word the caret is in or after, as Ctrl+Left goes: words are split by white space.</summary>
    public int WordBefore(int index) => WordStartBefore(Text, index);

    /// <summary>
    /// The same for any text: the native box of Windows has no Ctrl+Backspace of its own, and the window deletes from here to the caret.
    /// </summary>
    public static int WordStartBefore(string text, int index)
    {
        var i = Math.Clamp(index, 0, text.Length);
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
        while (i > 0 && !char.IsWhiteSpace(text[i - 1])) i--;
        return i;
    }

    /// <summary>The start of the next word, past the white space after this one, as Ctrl+Right goes.</summary>
    public int WordAfter(int index)
    {
        var i = Math.Clamp(index, 0, Text.Length);
        while (i < Text.Length && !char.IsWhiteSpace(Text[i])) i++;
        while (i < Text.Length && char.IsWhiteSpace(Text[i])) i++;
        return i;
    }

    /// <summary>The word under a double click: the run of characters other than white space around the index.</summary>
    public void SelectWordAt(int index)
    {
        var i = Math.Clamp(index, 0, Text.Length);
        var start = i;
        var end = i;
        while (start > 0 && !char.IsWhiteSpace(Text[start - 1])) start--;
        while (end < Text.Length && !char.IsWhiteSpace(Text[end])) end++;
        Select(start, end);
    }

    private const uint VkInsert = 0x2D, VkDelete = 0x2E;

    private static TextKeyResult Result(bool changed) => changed ? TextKeyResult.Changed : TextKeyResult.Handled;

    // A caret moved without Shift takes the selection away; with Shift the anchor stays.
    private void Move(int to, bool extend)
    {
        Caret = Math.Clamp(to, 0, Text.Length);
        if (!extend)
        {
            Anchor = Caret;
        }
    }

    private bool DeleteTo(int to)
    {
        to = Math.Clamp(to, 0, Text.Length);
        if (to == Caret)
        {
            return false;
        }

        Anchor = to;
        return Replace(string.Empty);
    }

    // The selection becomes the new text, cut to what the limit leaves room for. A key that changes nothing keeps no undo.
    private bool Replace(string inserted)
    {
        var start = SelectionStart;
        var end = SelectionEnd;
        var room = Math.Max(0, MaxLength - (Text.Length - (end - start)));
        if (inserted.Length > room)
        {
            inserted = inserted[..CutAt(inserted, room)];
        }

        if (inserted.Length == 0 && start == end)
        {
            return false;
        }

        Remember();
        Text = string.Concat(Text.AsSpan(0, start), inserted, Text.AsSpan(end));
        Caret = Anchor = start + inserted.Length;
        return true;
    }

    private void Remember()
    {
        _undoText = Text;
        _undoAnchor = Anchor;
        _undoCaret = Caret;
    }

    // Never between the two halves of a surrogate pair.
    private static int CutAt(string text, int length) =>
        length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;

    private static string Clean(string text)
    {
        var line = text.AsSpan();
        var stop = line.IndexOfAny('\r', '\n');
        if (stop >= 0)
        {
            line = line[..stop];
        }

        if (!line.ContainsAnyInRange('\0', '\x1F') && !line.Contains('\x7F'))
        {
            return line.ToString();
        }

        var kept = new System.Text.StringBuilder(line.Length);
        foreach (var c in line)
        {
            if (c >= ' ' && c != '\x7F')
            {
                kept.Append(c);
            }
        }

        return kept.ToString();
    }
}
