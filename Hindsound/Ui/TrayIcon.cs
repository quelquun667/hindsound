namespace Hindsound.Ui;

/// <summary>What the icon tells at a glance. Each state has its own shape, not only its own colour.</summary>
public enum TrayState
{
    /// <summary>An H in three bars.</summary>
    Active,

    /// <summary>The H loses its crossbar: two bars, the pause sign.</summary>
    Paused,

    /// <summary>The triangle of the error notifications.</summary>
    Error,
}

/// <summary>The commands of the tray menu.</summary>
internal enum TrayCommand : uint
{
    None = 0,
    Save30s = 1,
    Save15s,
    Save1m,
    Save5m,
    CopyLast,
    TogglePause,
    OpenFolder,
    Settings,
    Quit,
}

/// <summary>The shapes of the icon, as coverage from 0 to 1. Pure: tested without any window.</summary>
internal static class TrayIconShape
{
    public static float[] Coverage(TrayState state, int size)
    {
        var coverage = new float[size * size];
        var bar = Math.Max(2, size / 5);
        var middleTop = (size - bar) / 2;
        var scale = size / 24f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var side = x < bar || x >= size - bar;
                var middle = y >= middleTop && y < middleTop + bar;
                coverage[y * size + x] = state switch
                {
                    TrayState.Active => side || middle ? 1 : 0,
                    TrayState.Paused => side ? 1 : 0,
                    _ => Pixels.Clamp(0.5f - NotificationIcons.Distance(NotificationVisual.Triangle, (x + 0.5f) / scale, (y + 0.5f) / scale) * scale),
                };
            }
        }

        return coverage;
    }

    public static uint ColorOf(TrayState state, uint accent) => state switch
    {
        TrayState.Active => accent,
        TrayState.Paused => NotificationPalette.Muted,
        _ => NotificationPalette.Hot,
    };
}

/// <summary>The notification-area icon, drawn by the code, and its native menu.</summary>
internal sealed unsafe class TrayIcon(nint hwnd, uint callbackMessage) : IDisposable
{
    private const uint IconId = 1;

    private nint _icon;
    private bool _added;
    private string _tip = "Hindsound";

    /// <summary>Adds the icon, or adds it again after Explorer restarted.</summary>
    public bool Add(TrayState state, uint accent, string tip)
    {
        Replace(state, accent);
        _tip = tip;
        var data = Data(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP);
        _added = Native.Shell_NotifyIconW(Native.NIM_ADD, &data) != 0;
        data.Version = Native.NOTIFYICON_VERSION_4;
        Native.Shell_NotifyIconW(Native.NIM_SETVERSION, &data);
        return _added;
    }

    public void Update(TrayState state, uint accent, string tip)
    {
        Replace(state, accent);
        _tip = tip;
        var data = Data(Native.NIF_ICON | Native.NIF_TIP);
        Native.Shell_NotifyIconW(Native.NIM_MODIFY, &data);
    }

    /// <summary>Returns the chosen command. Runs only on the user's own right click on the icon.</summary>
    public TrayCommand ShowMenu(int x, int y, bool paused, bool hasLastClip)
    {
        var menu = Native.CreatePopupMenu();
        Item(menu, TrayCommand.Save30s, "Save last 30s");
        Item(menu, TrayCommand.Save15s, "Save last 15s");
        Item(menu, TrayCommand.Save1m, "Save last 1 min");
        Item(menu, TrayCommand.Save5m, "Save last 5 min");
        Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
        Item(menu, TrayCommand.CopyLast, "Copy last clip", enabled: hasLastClip);
        Item(menu, TrayCommand.TogglePause, paused ? "Resume capture" : "Pause capture");
        Item(menu, TrayCommand.OpenFolder, "Open clips folder");
        Item(menu, TrayCommand.Settings, "Settings");
        Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
        Item(menu, TrayCommand.Quit, "Quit");

        // Without being the foreground window, the menu would not close on an outside click.
        Native.SetForegroundWindow(hwnd);
        var command = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_NONOTIFY, x, y, hwnd, 0);
        Native.PostMessageW(hwnd, Native.WM_NULL, 0, 0);
        Native.DestroyMenu(menu);
        return (TrayCommand)command;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            Native.Shell_NotifyIconW(Native.NIM_DELETE, &data);
            _added = false;
        }

        if (_icon != 0)
        {
            Native.DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private static void Item(nint menu, TrayCommand command, string text, bool enabled = true) =>
        Native.AppendMenuW(menu, enabled ? Native.MF_STRING : Native.MF_GRAYED, (nuint)command, text);

    private Native.NOTIFYICONDATAW Data(uint flags)
    {
        Native.NOTIFYICONDATAW data = default;
        data.Size = (uint)sizeof(Native.NOTIFYICONDATAW);
        data.Hwnd = hwnd;
        data.Id = IconId;
        data.Flags = flags;
        data.CallbackMessage = callbackMessage;
        data.Icon = _icon;
        _tip.AsSpan(0, Math.Min(_tip.Length, 127)).CopyTo(new Span<char>(data.Tip, 127));
        return data;
    }

    private void Replace(TrayState state, uint accent)
    {
        var fresh = CreateIcon(state, accent);
        if (_icon != 0)
        {
            Native.DestroyIcon(_icon);
        }

        _icon = fresh;
    }

    private static nint CreateIcon(TrayState state, uint accent)
    {
        var size = Native.GetSystemMetrics(Native.SM_CXSMICON);
        var coverage = TrayIconShape.Coverage(state, size);
        var rgb = TrayIconShape.ColorOf(state, accent);

        Native.BITMAPINFOHEADER info = default;
        info.Size = (uint)sizeof(Native.BITMAPINFOHEADER);
        info.Width = size;
        info.Height = -size;
        info.Planes = 1;
        info.BitCount = 32;
        void* bits;
        var color = Native.CreateDIBSection(0, &info, 0, &bits, 0, 0);
        var pixels = (uint*)bits;
        for (var i = 0; i < coverage.Length; i++)
        {
            // An icon's alpha is not premultiplied.
            pixels[i] = ((uint)(coverage[i] * 255) << 24) | rgb;
        }

        var maskSize = (size + 15) / 16 * 2 * size;
        var maskBytes = stackalloc byte[maskSize];
        new Span<byte>(maskBytes, maskSize).Clear();
        var mask = Native.CreateBitmap(size, size, 1, 1, maskBytes);
        Native.ICONINFO icon = default;
        icon.Icon = 1;
        icon.Color = color;
        icon.Mask = mask;
        var handle = Native.CreateIconIndirect(&icon);
        Native.DeleteObject(color);
        Native.DeleteObject(mask);
        return handle;
    }
}
