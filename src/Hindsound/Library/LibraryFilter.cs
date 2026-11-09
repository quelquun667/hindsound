namespace Hindsound.Library;

public enum LibraryFilterKind
{
    All,
    Favorites,
    Collection,
}

/// <summary>What the menu above the list shows. It only hides lines: no file and no entry of the index changes with it.</summary>
public readonly record struct LibraryChoice(LibraryFilterKind Kind, string? CollectionId = null)
{
    // A hand edit can name a collection anything, "all" included: the id alone could not tell it from the other two choices.
    private const string AllValue = "all", FavoritesValue = "favorites", CollectionMark = "collection:";

    public static LibraryChoice All => new(LibraryFilterKind.All);

    public static LibraryChoice Favorites => new(LibraryFilterKind.Favorites);

    public static LibraryChoice Of(string collectionId) => new(LibraryFilterKind.Collection, collectionId);

    /// <summary>What stands for this choice in a list of values.</summary>
    public string Value => Kind switch
    {
        LibraryFilterKind.Favorites => FavoritesValue,
        LibraryFilterKind.Collection => CollectionMark + CollectionId,
        _ => AllValue,
    };

    /// <summary>The choice of a value. Anything else is every clip: a view must never come up empty for no reason.</summary>
    public static LibraryChoice FromValue(string? value)
    {
        if (value == FavoritesValue)
        {
            return Favorites;
        }

        return value is not null && value.StartsWith(CollectionMark, StringComparison.Ordinal) && value.Length > CollectionMark.Length
            ? Of(value[CollectionMark.Length..])
            : All;
    }
}

/// <summary>One line of the menu: the value of its <see cref="LibraryChoice"/> and what the user reads.</summary>
public readonly record struct LibraryMenuItem(string Value, string Label);

/// <summary>What a view with no line says.</summary>
public readonly record struct LibraryEmptyText(string Title, string? Body = null);

/// <summary>
/// All clips, Favorites or one collection: which lines of the list remain. Pure, and looks at no disk. It works on the capped lines,
/// which lose nothing here: the cap keeps every clip with a label, whatever its age.
/// </summary>
public static class LibraryFilter
{
    public const string AllClips = "All clips", Favorites = "Favorites";

    private const string NoClipsYet = "No clips yet", SaveAClip = "Save a clip with your shortcut, and it shows up here.",
        NoFavorites = "No favorite clips", NoClipsInCollection = "No clips in this collection";

    /// <summary>The two names the menu holds already: a collection called so could not be told from them.</summary>
    public static bool IsReservedName(string name) =>
        string.Equals(name, AllClips, StringComparison.OrdinalIgnoreCase) || string.Equals(name, Favorites, StringComparison.OrdinalIgnoreCase);

    /// <summary>The menu: every clip, the favorites, then the collections in the order of the file.</summary>
    public static IReadOnlyList<LibraryMenuItem> Menu(LibraryData data)
    {
        var items = new List<LibraryMenuItem>(data.Collections.Count + 2)
        {
            new(LibraryChoice.All.Value, AllClips),
            new(LibraryChoice.Favorites.Value, Favorites),
        };
        foreach (var collection in data.Collections)
        {
            items.Add(new LibraryMenuItem(LibraryChoice.Of(collection.Id).Value, collection.Name));
        }

        return items;
    }

    /// <summary>The choice as of this library: a collection that is gone, deleted here or in the file by hand, leaves every clip.</summary>
    public static LibraryChoice StillThere(LibraryChoice choice, LibraryData data) =>
        choice.Kind == LibraryFilterKind.Collection && NameOf(choice, data) is null ? LibraryChoice.All : choice;

    /// <summary>The name of the collection shown, or null if the choice is not a collection of this library.</summary>
    public static string? NameOf(LibraryChoice choice, LibraryData data) =>
        choice.Kind == LibraryFilterKind.Collection ? data.Collections.Find(c => c.Id == choice.CollectionId)?.Name : null;

    public static bool Keeps(LibraryChoice choice, ClipLabels labels) => choice.Kind switch
    {
        LibraryFilterKind.Favorites => labels.Favorite,
        LibraryFilterKind.Collection => labels.CollectionIds.Contains(choice.CollectionId ?? string.Empty),
        _ => true,
    };

    /// <summary>The lines left, in the order they came. Every clip costs no copy.</summary>
    public static IReadOnlyList<ClipGroup> Rows(IReadOnlyList<ClipGroup> lines, IndexMatch match, LibraryChoice choice)
    {
        if (choice.Kind == LibraryFilterKind.All)
        {
            return lines;
        }

        var kept = new List<ClipGroup>();
        foreach (var line in lines)
        {
            if (Keeps(choice, match.LabelsOf(line)))
            {
                kept.Add(line);
            }
        }

        return kept;
    }

    public static LibraryEmptyText EmptyText(LibraryChoice choice) => choice.Kind switch
    {
        LibraryFilterKind.Favorites => new LibraryEmptyText(NoFavorites),
        LibraryFilterKind.Collection => new LibraryEmptyText(NoClipsInCollection),
        _ => new LibraryEmptyText(NoClipsYet, SaveAClip),
    };
}
