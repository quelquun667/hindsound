using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Mac.App;
using Hindsound.Mac.Interop;
using Hindsound.Ui;

namespace Hindsound.Mac.Tray;

/// <summary>
/// The icon of the app in the menu bar of macOS: an NSStatusItem with a template image (<see cref="TemplateIcon"/>) and an
/// NSMenu. A click on it opens the menu, as every icon of the menu bar does: just before it shows, the menu asks the app for
/// its lines (<see cref="TrayGesture.Menu"/>, answered by <see cref="ShowMenu"/> on the same thread), and the line chosen
/// comes back through <see cref="Chosen"/> on the loop.
/// </summary>
/// <remarks>
/// NSStatusItem rather than SDL_CreateTray: SDL builds its menu ahead of time and tells nothing before it opens, while the
/// lines of the app change with what the memory holds; NSMenu asks its delegate (<c>menuNeedsUpdate:</c>) at the right
/// moment. It also needs no SDL at all between windows, and the image is a template that macOS tints for a light or a dark
/// bar. AppKit runs on the main thread only, which is the loop's (<see cref="MacRunLoopPump"/>).
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacStatusItem : ITrayIcon
{
    // NSVariableStatusItemLength.
    private const double VariableLength = -1;

    private const string TargetClassName = "HindsoundStatusItemTarget";

    // The one icon of the process, which the Objective-C target calls back.
    private static MacStatusItem? s_current;

    private readonly IAppLoop _loop;
    private readonly Action<string> _log;
    private readonly nint _target;
    private readonly nint _menu;
    private nint _item;
    // Each opening of the menu has its number; a line chosen keeps the number of the opening it was chosen in.
    private long _opening;
    private long _chosenIn;
    private bool _disposed;

    private MacStatusItem(IAppLoop loop, Action<string> log)
    {
        _loop = loop;
        _log = log;
        using var pool = new ObjC.Pool();
        var targetClass = Cocoa.DefineClass(TargetClassName,
        [
            ("menuNeedsUpdate:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMenuNeedsUpdate),
            ("menuDidClose:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMenuDidClose),
            ("choose:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnChoose),
        ]);
        _target = ObjC.Send(ObjC.Send(targetClass, "alloc"), "init");
        _menu = ObjC.Send(ObjC.Send(Cocoa.Class("NSMenu"), "alloc"), "init");
        Cocoa.SendFlag(_menu, "setAutoenablesItems:", false);
        ObjC.Send(_menu, "setDelegate:", _target);
    }

    public Action<TrayGesture>? Gesture { get; set; }

    public Action<TrayCommand>? Chosen { get; set; }

    // The menu bar of macOS never goes away under a running app.
    public Action? Lost { get; set; }

    public int IconSize => TemplateIcon.Pixels;

    // A template image of twice the size of its points: macOS scales it for every screen.
    public bool IconIsStale => false;

    /// <summary>The icon of the app, or null off the main thread, where AppKit cannot run (the log says why).</summary>
    public static MacStatusItem? TryCreate(IAppLoop loop, Action<string> log)
    {
        if (!Cocoa.OnMainThread)
        {
            log("menu bar: no icon off the main thread");
            return null;
        }

        if (s_current is not null)
        {
            throw new InvalidOperationException("the app has one icon in the menu bar");
        }

        return s_current = new MacStatusItem(loop, log);
    }

    public bool Add(TrayState state, uint accent, string tip) => Update(state, accent, tip);

    public bool Update(TrayState state, uint accent, string tip)
    {
        if (_disposed)
        {
            return false;
        }

        using var pool = new ObjC.Pool();
        if (_item == 0)
        {
            var bar = ObjC.Send(Cocoa.Class("NSStatusBar"), "systemStatusBar");
            _item = bar == 0 ? 0 : ObjC.Send(Cocoa.Send(bar, "statusItemWithLength:", VariableLength), "retain");
            if (_item == 0)
            {
                _log("menu bar: no status item");
                return false;
            }

            ObjC.Send(_item, "setMenu:", _menu);
        }

        var button = ObjC.Send(_item, "button");
        if (button == 0)
        {
            _log("menu bar: the status item has no button");
            return false;
        }

        var image = NewImage(state);
        ObjC.Send(button, "setImage:", image);
        ObjC.Release(image);
        var text = ObjC.NSString(tip);
        ObjC.Send(button, "setToolTip:", text);
        ObjC.Release(text);
        return true;
    }

    public void ShowMenu(IReadOnlyList<TrayMenuItem> items)
    {
        if (_disposed)
        {
            return;
        }

        using var pool = new ObjC.Pool();
        ObjC.Send(_menu, "removeAllItems");
        var empty = ObjC.NSString(string.Empty);
        foreach (var line in items)
        {
            if (line.Command == TrayCommand.None)
            {
                ObjC.Send(_menu, "addItem:", ObjC.Send(Cocoa.Class("NSMenuItem"), "separatorItem"));
                continue;
            }

            var title = ObjC.NSString(line.Text);
            var item = Cocoa.Send(ObjC.Send(Cocoa.Class("NSMenuItem"), "alloc"), "initWithTitle:action:keyEquivalent:", title, ObjC.Selector("choose:"), empty);
            ObjC.Release(title);
            ObjC.Send(item, "setTarget:", _target);
            Cocoa.SendInt(item, "setTag:", (nint)line.Command);
            Cocoa.SendFlag(item, "setEnabled:", line.Enabled);
            if (line.Checked is { } on)
            {
                // NSControlStateValueOn and Off.
                Cocoa.SendInt(item, "setState:", on ? 1 : 0);
            }

            ObjC.Send(_menu, "addItem:", item);
            ObjC.Release(item);
        }

        ObjC.Release(empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        using var pool = new ObjC.Pool();
        if (_item != 0)
        {
            ObjC.Send(ObjC.Send(Cocoa.Class("NSStatusBar"), "systemStatusBar"), "removeStatusItem:", _item);
            ObjC.Release(_item);
            _item = 0;
        }

        ObjC.Send(_menu, "setDelegate:", 0);
        ObjC.Release(_menu);
        ObjC.Release(_target);
        s_current = null;
    }

    /// <summary>What the menu bar holds now, read back from AppKit: for the proof of the CI and the tests.</summary>
    internal StatusItemFacts Read()
    {
        using var pool = new ObjC.Pool();
        var lines = new List<MenuLine>();
        var count = Cocoa.ReadInt(_menu, "numberOfItems");
        for (nint i = 0; i < count; i++)
        {
            var item = Cocoa.SendInt(_menu, "itemAtIndex:", i);
            lines.Add(new MenuLine(
                ObjC.String(ObjC.Send(item, "title")) ?? string.Empty,
                (TrayCommand)Cocoa.ReadInt(item, "tag"),
                ObjC.SendBool(item, "isEnabled"),
                ObjC.SendBool(item, "isSeparatorItem"),
                Cocoa.ReadInt(item, "state") != 0,
                ObjC.Send(item, "target") == _target));
        }

        var button = _item == 0 ? 0 : ObjC.Send(_item, "button");
        var image = button == 0 ? 0 : ObjC.Send(button, "image");
        var representations = image == 0 ? 0 : ObjC.Send(image, "representations");
        var first = representations == 0 || ObjC.SendUInt(representations, "count") == 0 ? 0 : Cocoa.SendInt(representations, "objectAtIndex:", 0);
        var size = image == 0 ? default : Cocoa.ReadSize(image, "size");
        return new StatusItemFacts(
            _item != 0,
            ObjC.Send(_menu, "delegate") == _target && (_item == 0 || ObjC.Send(_item, "menu") == _menu),
            image != 0 && ObjC.SendBool(image, "isTemplate"),
            size.Width,
            first == 0 ? 0 : (int)Cocoa.ReadInt(first, "pixelsWide"),
            button == 0 ? null : ObjC.String(ObjC.Send(button, "toolTip")),
            lines);
    }

    /// <summary>Runs what AppKit runs as the menu opens: the delegate is asked for the lines (<c>menuNeedsUpdate:</c>).</summary>
    internal void Opening()
    {
        using var pool = new ObjC.Pool();
        ObjC.Send(ObjC.Send(_menu, "delegate"), "menuNeedsUpdate:", _menu);
    }

    /// <summary>Chooses the line at <paramref name="index"/> as a click would (<c>performActionForItemAtIndex:</c>), then closes the menu.</summary>
    internal void Choose(int index)
    {
        using var pool = new ObjC.Pool();
        Cocoa.SendInt(_menu, "performActionForItemAtIndex:", index);
        ObjC.Send(ObjC.Send(_menu, "delegate"), "menuDidClose:", _menu);
    }

    /// <summary>Closes the menu with no line chosen.</summary>
    internal void Close()
    {
        using var pool = new ObjC.Pool();
        ObjC.Send(ObjC.Send(_menu, "delegate"), "menuDidClose:", _menu);
    }

    private static nint NewImage(TrayState state)
    {
        var pixels = TemplateIcon.Rgba(state);
        var space = ObjC.NSString("NSDeviceRGBColorSpace");
        var bitmap = Cocoa.NewBitmap(TemplateIcon.Pixels, TemplateIcon.Pixels, space);
        ObjC.Release(space);
        Marshal.Copy(pixels, 0, Cocoa.ReadInt(bitmap, "bitmapData"), pixels.Length);
        Cocoa.SendSize(bitmap, "setSize:", TemplateIcon.Points, TemplateIcon.Points);
        var image = Cocoa.SendSize(ObjC.Send(Cocoa.Class("NSImage"), "alloc"), "initWithSize:", TemplateIcon.Points, TemplateIcon.Points);
        ObjC.Send(image, "addRepresentation:", bitmap);
        ObjC.Release(bitmap);
        Cocoa.SendFlag(image, "setTemplate:", true);
        return image;
    }

    // On the main thread, just before the menu shows: the app gives its lines now, through ShowMenu.
    [UnmanagedCallersOnly]
    private static void OnMenuNeedsUpdate(nint self, nint command, nint menu)
    {
        if (s_current is not { _disposed: false } icon)
        {
            return;
        }

        icon._opening++;
        try
        {
            icon.Gesture?.Invoke(TrayGesture.Menu);
        }
        catch (Exception ex)
        {
            // An exception must not cross into Objective-C: the process would end there.
            icon._log("menu bar: the menu could not be filled: " + ex);
        }
    }

    // AppKit sends the action of a chosen line after the menu closes: the posted check finds it already chosen.
    [UnmanagedCallersOnly]
    private static void OnMenuDidClose(nint self, nint command, nint menu)
    {
        if (s_current is not { _disposed: false } icon)
        {
            return;
        }

        // Read when the post runs, not now: AppKit sends the action after this. The number of the opening is taken now, so a
        // menu opened again before the post runs does not hide that this one closed with nothing chosen.
        var opening = icon._opening;
        icon._loop.Post(() =>
        {
            if (icon._chosenIn != opening)
            {
                icon.Chosen?.Invoke(TrayCommand.None);
            }
        });
    }

    [UnmanagedCallersOnly]
    private static void OnChoose(nint self, nint command, nint sender)
    {
        if (s_current is not { _disposed: false } icon)
        {
            return;
        }

        icon._chosenIn = icon._opening;
        var chosen = (TrayCommand)Cocoa.ReadInt(sender, "tag");
        icon._loop.Post(() => icon.Chosen?.Invoke(chosen));
    }
}

/// <summary>One line of the menu, as AppKit holds it.</summary>
internal readonly record struct MenuLine(string Title, TrayCommand Command, bool Enabled, bool Separator, bool On, bool Targeted);

/// <summary>The icon as AppKit holds it.</summary>
/// <param name="Present">The status item is in the menu bar.</param>
/// <param name="Wired">The menu is the item's and its delegate is the app's.</param>
/// <param name="Template">The image is a template, tinted by macOS.</param>
/// <param name="Points">The width of the image, in points.</param>
/// <param name="PixelsWide">The width of its bitmap, in pixels.</param>
internal sealed record StatusItemFacts(bool Present, bool Wired, bool Template, double Points, int PixelsWide, string? Tip, IReadOnlyList<MenuLine> Lines);
