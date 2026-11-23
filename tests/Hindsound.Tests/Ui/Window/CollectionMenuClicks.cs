using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The way a user reaches Rename... and Delete... of a collection: the "..." button, then the line of the menu it opens.</summary>
internal static class CollectionMenuClicks
{
    /// <summary>A click on the button of the collection. The menu is open when this returns.</summary>
    public static CollectionMenu OpenFrom(SettingsScreen screen)
    {
        var box = screen.WidgetById(CollectionActions.MoreId)!.Bounds;
        screen.MouseDown(box.MidX, box.MidY);
        screen.MouseUp(box.MidX, box.MidY);
        return screen.OpenCollectionMenu ?? throw new InvalidOperationException("The button did not open the menu of the collection.");
    }

    /// <summary>A click on a line of the open menu.</summary>
    public static void Click(SettingsScreen screen, CollectionMenuItem item)
    {
        var menu = screen.OpenCollectionMenu!;
        var row = menu.RowBox(menu.Items.ToList().IndexOf(item));
        screen.MouseDown(row.MidX, row.MidY);
        screen.MouseUp(row.MidX, row.MidY);
    }

    /// <summary>The button, then the line: what Rename or Delete did when they were two buttons.</summary>
    public static void Pick(SettingsScreen screen, CollectionMenuItem item)
    {
        OpenFrom(screen);
        Click(screen, item);
    }
}
