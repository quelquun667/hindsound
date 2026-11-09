namespace Hindsound.Ui.Window;

/// <summary>The window's side of the Now page. Kept out of ISettingsBackend, which test doubles implement.</summary>
internal interface INowPanel
{
}

/// <summary>The Now page: what the memory holds, as it comes in.</summary>
internal sealed partial class SettingsScreen
{
    private INowPanel? _now;

    /// <summary>
    /// The Now page draws from this panel, and the window opens on it. Null, the default, leaves the side bar at the pages of
    /// the settings and the window on Hotkeys.
    /// </summary>
    public void SetNow(INowPanel? panel)
    {
        var arrives = _now is null && panel is not null;
        _now = panel;
        if (arrives && !IsCard)
        {
            ShowSection(Section.Now);
        }
        else if (panel is null && Section == Section.Now)
        {
            ShowSection(Section.Hotkeys);
        }
        else
        {
            Refresh();
        }
    }

    private NavRows Rows => _now is null ? NavRows.SettingsOnly : NavRows.WithViews;

    /// <summary>Where the end of the first-launch card leaves the user.</summary>
    private Section FirstSection => _now is null ? Section.Hotkeys : Section.Now;

    private void BuildNow()
    {
        // Nothing on it yet: without this, it would keep the scrolling room of the page before it.
        _contentBottom = 0;
    }
}
