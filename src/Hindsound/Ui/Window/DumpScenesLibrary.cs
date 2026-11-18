using Hindsound.Export;
using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>
/// The pictures of the clip tools on the Library page, for --dump-settings: the menu of a clip, the box that renames it and the one
/// that sends it to the Recycle Bin. Each scene starts from a screen that shows the Library with the sample clips of the dump, the
/// clip of its second row chosen, and is drawn with the focus.
/// </summary>
internal static class DumpScenesLibrary
{
    /// <summary>The two-file clip of the sample, as the dump names its first file.</summary>
    public const string TwoFilesClip = @"C:\Clips\Hindsound_2026-11-07_22-10-08_system.mp3";

    /// <summary>The name of each picture, and what brings the screen there.</summary>
    public static IReadOnlyList<(string Name, Action<SettingsScreen> Act)> Scenes { get; } =
    [
        Scene("library-menu", s => RightClickRow(s, 1)),
        Scene("library-menu-pied", s => Press(s, SettingsScreen.ClipMoreId)),
        Scene("library-renommer", s => Key(s, ClipKeys.F2)),
        Scene("library-renommer-vide", s =>
        {
            Key(s, ClipKeys.F2);
            s.EditChanged(string.Empty);
        }),
        Scene("library-renommer-signe", s =>
        {
            Key(s, ClipKeys.F2);
            s.EditChanged("best: moment");
        }),
        Scene("library-supprimer-clip", s => Key(s, ClipKeys.Delete)),
        Scene("library-supprimer-2-fichiers", s =>
        {
            s.SelectClip(TwoFilesClip);
            Key(s, ClipKeys.Delete);
        }),
    ];

    /// <summary>A scene with an editor behind the page, as the window has: the menu then lists Edit... and Send to soundboard....</summary>
    private static (string Name, Action<SettingsScreen> Act) Scene(string name, Action<SettingsScreen> act) =>
        (name, s =>
        {
            s.SetEditor(new DumpScenesEditor.SampleEditor());
            act(s);
        });

    /// <summary>A right click on a row on screen, towards its middle, where a user would make it.</summary>
    private static void RightClickRow(SettingsScreen s, int slot)
    {
        var list = s.WidgetById(SettingsScreen.ClipListId)!.Bounds;
        s.RightClick(list.X + 230, list.Y + (slot + 0.5f) * RowListState.RowHeight);
    }

    /// <summary>A click of the mouse on a widget, as the user makes it.</summary>
    private static void Press(SettingsScreen s, string id)
    {
        var box = s.WidgetById(id)!.Bounds;
        s.MouseDown(box.MidX, box.MidY);
        s.MouseUp(box.MidX, box.MidY);
    }

    /// <summary>A key with the list in hand, the way the keyboard reaches the clip.</summary>
    private static void Key(SettingsScreen s, uint vk)
    {
        s.TabTo(SettingsScreen.ClipListId);
        s.Key(vk, 0);
    }
}
