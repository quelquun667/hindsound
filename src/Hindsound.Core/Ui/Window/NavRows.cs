namespace Hindsound.Ui.Window;

/// <summary>One line of the side bar. Its words are read at each use, in the language of the moment.</summary>
/// <param name="Section">The page it shows.</param>
/// <param name="Words">Words that stay as they are, in place of the section's own; null for the section's.</param>
internal readonly record struct NavRow(Section Section, string? Words = null)
{
    public string Label => Words ?? Section switch
    {
        Section.Now => Tr.Of("Now", "Maintenant"),
        Section.Library => Tr.Of("Library", "Bibliothèque"),
        Section.Hotkeys => Tr.Of("Hotkeys", "Raccourcis"),
        Section.Durations => Tr.Of("Durations", "Durées"),
        Section.Audio => Tr.Of("Audio", "Audio"),
        Section.Export => Tr.Of("Export", "Export"),
        Section.General => Tr.Of("General", "Général"),
        _ => Section.ToString(),
    };
}

/// <summary>
/// The lines of the side bar and where they sit: the views on top, then a caption, then the pages of the settings. One list
/// for the keyboard: one stop for Tab, and the arrows walk past the caption as if it were not there. Pure: tested without any window.
/// </summary>
internal sealed class NavRows
{
    public const float ItemHeight = 36, Gap = 4;
    public static string Caption => Tr.Of("Settings", "Réglages");

    // From the bottom of the last view: the top of the next line, and the middle of the caption, just high enough to stay
    // out of the focus ring of the line under it.
    private const float CaptionBlock = 28, CaptionMid = 16;

    private static readonly NavRow[] SettingsPages =
    [
        new(Section.Hotkeys),
        new(Section.Durations),
        new(Section.Audio),
        new(Section.Export),
        new(Section.General),
    ];

    /// <summary>The window before it has a Now page.</summary>
    public static readonly NavRows SettingsOnly = new([]);

    /// <summary>The window with Now and without Library.</summary>
    public static readonly NavRows WithViews = new([new(Section.Now)]);

    /// <summary>The window of the app.</summary>
    public static readonly NavRows WithLibrary = new([new(Section.Now), new(Section.Library)]);

    /// <summary>The window with Library and without Now.</summary>
    public static readonly NavRows LibraryOnly = new([new(Section.Library)]);

    private readonly int _views;

    public NavRows(IReadOnlyList<NavRow> views)
    {
        _views = views.Count;
        Rows = [.. views, .. SettingsPages];
    }

    public IReadOnlyList<NavRow> Rows { get; }

    /// <summary>The middle of the caption, from the top of the list. Null when nothing above it needs telling apart.</summary>
    public float? CaptionMidY => _views == 0 ? null : ViewsBottom + CaptionMid;

    /// <summary>All of it, from the top of the first line to the bottom of the last.</summary>
    public float Height => TopOf(Rows.Count - 1) + ItemHeight;

    private float ViewsBottom => _views * (ItemHeight + Gap) - Gap;

    /// <summary>The top of a line, from the top of the list.</summary>
    public float TopOf(int index) => index < _views || _views == 0
        ? index * (ItemHeight + Gap)
        : ViewsBottom + CaptionBlock + (index - _views) * (ItemHeight + Gap);

    /// <summary>The line at this height, from the top of the list. -1 between two lines and on the caption.</summary>
    public int IndexAt(float y)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            var top = TopOf(i);
            if (y >= top && y < top + ItemHeight)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The line of a section, -1 for one with no line, such as a card.</summary>
    public int IndexOf(Section section)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Section == section)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>One of the pages under the caption, as opposed to a view or a card.</summary>
    public static bool IsSettingsPage(Section section) => section is Section.Hotkeys or Section.Durations or Section.Audio or Section.Export or Section.General;
}
