using Hindsound.Library;

namespace Hindsound.Ui.Window;

/// <summary>What the menu of a clip offers, in its order.</summary>
internal enum ClipMenuItem
{
    Rename,
    Edit,
    Soundboard,
    Copy,
    ShowInFolder,
    Delete,
}

/// <summary>The keys of the clip actions that <see cref="Vk"/> does not name.</summary>
internal static class ClipKeys
{
    public const uint Delete = 0x2E, F2 = 0x71;
}

/// <summary>
/// The menu of one clip, over the page: from a right click on its row, at the mouse, or from the "..." button of the foot, above it.
/// It only remembers where it is and which line is lit; the screen does what is picked.
/// </summary>
internal sealed class ClipMenu : ActionMenu
{
    public const float Width = 236;
    public static string RenameLabel => Tr.Of("Rename", "Renommer");
    public static string EditLabel => Tr.Of("Edit...", "Modifier...");
    public static string SoundboardLabel => Tr.Of("Send to soundboard...", "Envoyer vers la soundboard...");
    public static string DeleteLabel => Tr.Of("Delete", "Supprimer");

    private static readonly ClipMenuItem[] WithEditor =
        [ClipMenuItem.Rename, ClipMenuItem.Edit, ClipMenuItem.Soundboard, ClipMenuItem.Copy, ClipMenuItem.ShowInFolder, ClipMenuItem.Delete];

    private static readonly ClipMenuItem[] WithoutEditor =
        [ClipMenuItem.Rename, ClipMenuItem.Copy, ClipMenuItem.ShowInFolder, ClipMenuItem.Delete];

    private ClipMenu(ClipGroup clip, IReadOnlyList<ClipMenuItem> items, (float X, float Y) at, int index)
        : base(Width, HeightOf(items.Count, items.Contains(ClipMenuItem.Delete) ? 1 : 0), at.X, at.Y, index)
    {
        Clip = clip;
        Items = items;
    }

    /// <summary>The clip the menu acts on: the one chosen when it opened, whatever is chosen since.</summary>
    public ClipGroup Clip { get; }

    public IReadOnlyList<ClipMenuItem> Items { get; }

    public override int Count => Items.Count;

    /// <summary>The line lit, if any.</summary>
    public ClipMenuItem? Current => Index >= 0 && Index < Items.Count ? Items[Index] : null;

    /// <summary>The lines: Edit... and Send to soundboard... only when there is an editor to open.</summary>
    public static IReadOnlyList<ClipMenuItem> ItemsFor(bool editor) => editor ? WithEditor : WithoutEditor;

    /// <summary>At the point of a right click, turned to the left or upwards where it would leave the window.</summary>
    public static ClipMenu AtPoint(ClipGroup clip, IReadOnlyList<ClipMenuItem> items, float x, float y) =>
        new(clip, items, PlaceAtPoint(Width, HeightOf(items.Count, items.Contains(ClipMenuItem.Delete) ? 1 : 0), x, y), -1);

    /// <summary>Over the button that opened it, its right edge on the button's. The keyboard lights the first line; the mouse none.</summary>
    public static ClipMenu Above(ClipGroup clip, IReadOnlyList<ClipMenuItem> items, Box button, bool byKey) =>
        new(clip, items, PlaceAbove(Width, HeightOf(items.Count, items.Contains(ClipMenuItem.Delete) ? 1 : 0), button), byKey ? 0 : -1);

    public static string LabelOf(ClipMenuItem item) => item switch
    {
        ClipMenuItem.Rename => RenameLabel,
        ClipMenuItem.Edit => EditLabel,
        ClipMenuItem.Soundboard => SoundboardLabel,
        ClipMenuItem.Copy => Tr.Of("Copy", "Copier"),
        ClipMenuItem.ShowInFolder => SettingsScreen.ShowInFolderLabel,
        _ => DeleteLabel,
    };

    /// <summary>The key that does the same from the list, shown at the right of the line.</summary>
    public static string? KeyOf(ClipMenuItem item) => item switch
    {
        ClipMenuItem.Rename => "F2",
        // French keyboards print "Suppr" on that key.
        ClipMenuItem.Delete => Tr.Of("Del", "Suppr"),
        _ => null,
    };

    protected override string LabelAt(int index) => LabelOf(Items[index]);

    protected override string? KeyAt(int index) => KeyOf(Items[index]);

    protected override bool IsDangerAt(int index) => Items[index] == ClipMenuItem.Delete;

    /// <summary>A thin rule comes before Delete.</summary>
    protected override bool RuleBefore(int index) => Items[index] == ClipMenuItem.Delete;
}
