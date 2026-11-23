namespace Hindsound.Ui.Window;

/// <summary>What the menu of a collection offers, in its order.</summary>
internal enum CollectionMenuItem
{
    Rename,
    Delete,
}

/// <summary>
/// The menu of the chosen collection, under the "..." button beside the menu of the Library: Rename... and Delete..., which open the
/// boxes that ask for the name and for the confirmation. It only remembers where it is and which line is lit.
/// </summary>
internal sealed class CollectionMenu : ActionMenu
{
    public const float Width = 160;
    public const string RenameLabel = "Rename...", DeleteLabel = "Delete...";

    private static readonly CollectionMenuItem[] All = [CollectionMenuItem.Rename, CollectionMenuItem.Delete];

    private CollectionMenu(string collectionId, (float X, float Y) at, int index)
        : base(Width, HeightOf(All.Length, 1), at.X, at.Y, index)
    {
        CollectionId = collectionId;
    }

    /// <summary>The collection the menu acts on: the one chosen when it opened.</summary>
    public string CollectionId { get; }

    public IReadOnlyList<CollectionMenuItem> Items => All;

    public override int Count => All.Length;

    /// <summary>The line lit, if any.</summary>
    public CollectionMenuItem? Current => Index >= 0 && Index < All.Length ? All[Index] : null;

    /// <summary>Under the button that opened it. The keyboard lights the first line; the mouse none.</summary>
    public static CollectionMenu Below(string collectionId, Box button, bool byKey) =>
        new(collectionId, PlaceBelow(Width, HeightOf(All.Length, 1), button), byKey ? 0 : -1);

    public static string LabelOf(CollectionMenuItem item) => item == CollectionMenuItem.Rename ? RenameLabel : DeleteLabel;

    protected override string LabelAt(int index) => LabelOf(All[index]);

    protected override bool IsDangerAt(int index) => All[index] == CollectionMenuItem.Delete;

    /// <summary>A thin rule comes before Delete..., as in the menu of a clip.</summary>
    protected override bool RuleBefore(int index) => All[index] == CollectionMenuItem.Delete;
}
