using Hindsound.Input;
using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>The actions on one clip of the Library: its menu, renaming it, and sending it to the Recycle Bin.</summary>
internal sealed partial class SettingsScreen
{
    /// <summary>The menu over the page, the one of a clip or the one of a collection. Null when it is closed.</summary>
    private ActionMenu? _menu;

    /// <summary>A key is being handed to the focused widget: what it opens was opened by the keyboard.</summary>
    private bool _byKey;

    /// <summary>The paths of the latest reading, and the reading they were taken from: a name of the dialog is checked against them at each key.</summary>
    private (ClipLines Lines, HashSet<string> Paths)? _listedPaths;

    /// <summary>The menu of a clip on screen. Null when it is closed.</summary>
    internal ClipMenu? OpenClipMenu => _menu as ClipMenu;

    /// <summary>The menu of the collection on screen. Null when it is closed.</summary>
    internal CollectionMenu? OpenCollectionMenu => _menu as CollectionMenu;

    /// <summary>The right button went down here. On a clip of the list, its menu opens; anywhere else, nothing happens.</summary>
    public void RightClick(float x, float y)
    {
        if (Section != Section.Library || EditorOpen || _box is not null || _capture.Listening)
        {
            return;
        }

        // A list already open closes, as any press outside it does, and the press does nothing else.
        if (_popup is not null || _moreList is not null || _menu is not null)
        {
            _popup = null;
            _moreList?.Close();
            _menu = null;
            _host.Invalidate();
            return;
        }

        if (HitTest(x, y) is not RowList list || list.Id != ClipListId)
        {
            return;
        }

        var row = list.RowAtPoint(x, y);
        if (row == RowListState.None)
        {
            return;
        }

        // The row is chosen first, as a left press would: the menu acts on what the foot shows.
        _wantedClip = null;
        SetFocus(ClipListId);
        _clips.Select(row);
        Refresh();
        if (ActionableClip is { } line)
        {
            _menu = ClipMenu.AtPoint(line, ClipMenu.ItemsFor(_editorPanel is not null), x, y);
            _host.Invalidate();
        }
    }

    /// <summary>The answer to <see cref="ILibraryPanel.Rename"/>, on the loop. A new reading follows.</summary>
    /// <param name="line">The clip as it was asked for.</param>
    /// <param name="outcome">What the renaming did.</param>
    public void ClipRenameAnswered(ClipGroup line, ClipRenameOutcome outcome)
    {
        var target = _renameTarget;
        _renameTarget = null;
        if (BoxOf(NameBoxKind.RenameClip, line) is { } box)
        {
            if (!box.AnsweredClip(outcome))
            {
                Refresh();
                return;
            }

            CloseBox(null);
        }

        switch (outcome)
        {
            case ClipRenameOutcome.Done:
                // The reading that follows lists the clip at its new path: it is chosen there.
                if (target is not null)
                {
                    _wantedClip = target;
                }

                break;
            case ClipRenameOutcome.Gone:
                SayGone(line);
                break;
            default:
                SayOnLine(line.Main.Path, NameBoxState.ClipRefusalText(outcome, Features) ?? ClipFailedShown);
                break;
        }

        Refresh();
    }

    /// <summary>The answer to <see cref="ILibraryPanel.Delete"/>, on the loop. A new reading follows.</summary>
    public void ClipDeleteAnswered(ClipGroup line, ClipDeleteOutcome outcome)
    {
        if (BoxOf(NameBoxKind.DeleteClip, line) is not null)
        {
            CloseBox(null);
        }

        switch (outcome)
        {
            case ClipDeleteOutcome.Recycled:
                ChooseNextAfter(line);
                break;
            case ClipDeleteOutcome.Gone:
                SayGone(line);
                break;
            case ClipDeleteOutcome.CannotRecycle:
                SayOnLine(line.Main.Path, Features.Words(NameBoxState.CannotRecycleShown));
                break;
            default:
                SayOnLine(line.Main.Path, ClipFailedShown);
                break;
        }

        Refresh();
    }

    /// <summary>A key on the Library page, before the widgets have it: Ctrl+F, F2 and Delete. Null when this does not want it.</summary>
    private bool? LibraryKey(uint vk, uint modifiers)
    {
        // Under a box nothing behind it takes a key; a menu has already kept its own.
        if (modifiers == HotkeyGesture.ModControl && vk == SearchFindKey && _library is not null && _box is null)
        {
            FocusSearch();
            return true;
        }

        if (modifiers != 0 || vk is not (ClipKeys.F2 or ClipKeys.Delete))
        {
            return null;
        }

        // Only where the clip is what the keyboard is on: the list, or a button of the foot that acts on it.
        if (_focusId is not (ClipListId or ClipMoreId or ClipCopyId or ClipShowId or ClipPlayId) || _focusId is not { } from || ActionableClip is not { } line)
        {
            return null;
        }

        Act(vk == ClipKeys.F2 ? ClipMenuItem.Rename : ClipMenuItem.Delete, line, from);
        return true;
    }

    /// <summary>The chosen clip, when something can be done with it: a clip found gone has its row dimmed until the next reading.</summary>
    private ClipGroup? ActionableClip =>
        _library is not null && ChosenClip is { } line && !_goneClips.Contains(line.Main.Path) ? line : null;

    /// <summary>The path the renaming asked for gives the first file of the clip: the reading after its answer chooses it.</summary>
    private string? _renameTarget;

    /// <summary>The "..." of the foot: the same menu, over the button.</summary>
    private void OpenMenuFromFoot(Box button)
    {
        if (ActionableClip is not { } line)
        {
            return;
        }

        _focusId = ClipMoreId;
        _menu = ClipMenu.Above(line, ClipMenu.ItemsFor(_editorPanel is not null), button, _byKey);
        Refresh();
    }

    /// <summary>A key while a menu is open: it keeps them all, but those it does not know go on to Windows.</summary>
    private bool MenuKey(ActionMenu menu, uint vk, uint modifiers)
    {
        if ((modifiers & (HotkeyGesture.ModControl | HotkeyGesture.ModAlt | HotkeyGesture.ModWin)) != 0)
        {
            return false;
        }

        switch (vk)
        {
            case Vk.Escape:
                CloseMenu();
                return true;
            case Vk.Tab:
                // The keyboard stays in the menu, as in a menu of Windows: Tab goes round its lines.
                menu.Move((modifiers & HotkeyGesture.ModShift) == 0 ? 1 : -1);
                break;
            case Vk.Up:
                menu.Move(-1);
                break;
            case Vk.Down:
                menu.Move(1);
                break;
            case Vk.Home:
            case Vk.End:
                menu.MoveTo(vk == Vk.Home);
                break;
            case Vk.Enter:
            case Vk.Space:
                if (menu.Index >= 0)
                {
                    PickFromMenu(menu, menu.Index);
                }

                return true;
            case ClipKeys.F2 when menu is ClipMenu clipMenu:
                PickFromClipMenu(clipMenu, ClipMenuItem.Rename);
                return true;
            case ClipKeys.Delete when menu is ClipMenu clipMenu:
                PickFromClipMenu(clipMenu, ClipMenuItem.Delete);
                return true;
            default:
                return false;
        }

        _host.Invalidate();
        return true;
    }

    /// <summary>A press while a menu is open: on a line it acts, anywhere else it only closes the menu.</summary>
    private void MenuPress(ActionMenu menu, float x, float y)
    {
        var index = menu.ItemAt(x, y);
        if (index < 0)
        {
            CloseMenu();
            return;
        }

        PickFromMenu(menu, index);
    }

    private void CloseMenu()
    {
        _menu = null;
        _host.Invalidate();
    }

    private void PickFromMenu(ActionMenu menu, int index)
    {
        switch (menu)
        {
            case ClipMenu clipMenu:
                PickFromClipMenu(clipMenu, clipMenu.Items[index]);
                break;
            case CollectionMenu collectionMenu:
                PickFromCollectionMenu(collectionMenu, collectionMenu.Items[index]);
                break;
        }
    }

    private void PickFromClipMenu(ClipMenu menu, ClipMenuItem item)
    {
        _menu = null;
        Act(item, menu.Clip, _focusId ?? ClipListId);
    }

    /// <summary>What an item of the menu, or its key, does with the clip.</summary>
    /// <param name="item">The action.</param>
    /// <param name="line">The clip.</param>
    /// <param name="from">The stop the focus goes back to when a box closes.</param>
    private void Act(ClipMenuItem item, ClipGroup line, string from)
    {
        switch (item)
        {
            case ClipMenuItem.Rename:
                OpenBox(NameBoxState.ForClipRename(line, from, name => ClipNames.Refusal(line, name, IsListed)));
                break;
            case ClipMenuItem.Delete:
                OpenBox(NameBoxState.ForClipDelete(line, from));
                break;
            case ClipMenuItem.Edit:
                OpenEditor(line, EditorMode.Normal, from);
                break;
            case ClipMenuItem.Soundboard:
                OpenEditor(line, EditorMode.Soundboard, from);
                break;
            case ClipMenuItem.Copy:
                ClipGestureOn(line, p => p.Copy);
                break;
            default:
                ClipGestureOn(line, p => p.ShowInFolder);
                break;
        }

        _host.Invalidate();
    }

    /// <summary>The box of a clip asks its action, once.</summary>
    private void SendClipBox(ILibraryPanel panel, NameBoxState box)
    {
        var line = box.Clip!;
        if (box.Kind == NameBoxKind.DeleteClip)
        {
            panel.Delete(line);
            return;
        }

        var name = box.Field.Draft;
        if (string.Equals(name, box.Name, StringComparison.Ordinal))
        {
            // The same name, to the letter: nothing to ask of the disk.
            CloseBox(null);
            return;
        }

        _renameTarget = ClipNames.TargetPaths(line, name)[0];
        panel.Rename(line, name);
    }

    /// <summary>The box open for this clip and this action. Null when it went with the page, or another is open.</summary>
    private NameBoxState? BoxOf(NameBoxKind kind, ClipGroup line) =>
        _box is { } box && box.Kind == kind && box.Clip is { } clip && string.Equals(clip.Main.Path, line.Main.Path, Library.PathRules.Comparison) ? box : null;

    /// <summary>Whether a path is a file of the latest reading, whichever view the menu shows.</summary>
    private bool IsListed(string path)
    {
        if (_clipLines is not { } lines)
        {
            return false;
        }

        if (_listedPaths is not { } listed || !ReferenceEquals(listed.Lines, lines))
        {
            var paths = new HashSet<string>(Library.PathRules.Comparer);
            foreach (var group in lines.Lines)
            {
                foreach (var file in group.Files)
                {
                    paths.Add(file.Path);
                }
            }

            listed = (lines, paths);
            _listedPaths = listed;
        }

        return listed.Paths.Contains(path);
    }

    /// <summary>The clip went to the Recycle Bin: the row under it is chosen, or the one above when it was the last.</summary>
    private void ChooseNextAfter(ClipGroup line)
    {
        var index = -1;
        for (var i = 0; i < _clipRows.Count; i++)
        {
            if (string.Equals(_clipRows[i].Main.Path, line.Main.Path, Library.PathRules.Comparison))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        var next = index + 1 < _clipRows.Count ? index + 1 : index - 1;
        if (next >= 0)
        {
            _wantedClip = null;
            _clips.Select(next);
        }
        else
        {
            _clips.Deselect();
        }
    }

    /// <summary>The clip was not there any more: nothing stays chosen, and the line says why, through the reading that follows.</summary>
    private void SayGone(ClipGroup line)
    {
        // Said under the clip only while it is the one chosen: under another, the sentence would be about the wrong clip.
        if (ChosenClip is { } chosen && string.Equals(chosen.Main.Path, line.Main.Path, Library.PathRules.Comparison))
        {
            _clips.Deselect();
            SayOnLine(null, ClipGoneMessage);
        }
    }

    /// <summary>The sentence of an answer on the amber line, for this clip: the reading that the answer brings leaves it be.</summary>
    private void SayOnLine(string? clip, string text)
    {
        _lineNotice = (clip, text);
        _noticeUnseen = Section != Section.Library;
        _noticeKeptOnce = true;
    }
}
