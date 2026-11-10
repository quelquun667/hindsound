using Hindsound.App;

namespace Hindsound.Ui;

/// <summary>
/// Which calls keep the icon in the notification area, and whether it is there. The calls are handed in, as a message and
/// its flags: tested without any shell.
/// </summary>
internal sealed class TrayPresence(Func<uint, uint, bool> call)
{
    // With NOTIFYICON_VERSION_4, the shell drops the tooltip of an icon without NIF_SHOWTIP.
    public const uint AddFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP;
    public const uint ModifyFlags = Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP;

    private bool _closed;

    /// <summary>The icon was seen in the shell at least once and not deleted since: leaving has to delete it.</summary>
    public bool Added { get; private set; }

    /// <summary>
    /// NIM_ADD refuses an icon that already exists: TaskbarCreated can come after an add that went through, and a busy
    /// Explorer can time out on one and still do it. NIM_MODIFY then tells whether the icon is there. True if it is.
    /// </summary>
    public bool Add()
    {
        if (_closed || (!call(Native.NIM_ADD, AddFlags) && !call(Native.NIM_MODIFY, AddFlags)))
        {
            return false;
        }

        Added = true;
        call(Native.NIM_SETVERSION, 0);
        return true;
    }

    /// <summary>Tells the shell what changed. An icon it does not know, or no longer knows, gets added. True if it is there.</summary>
    public bool Update() => !_closed && ((Added && call(Native.NIM_MODIFY, ModifyFlags)) || Add());

    /// <summary>The last call: nothing brings the icon back after this.</summary>
    public void Close()
    {
        if (Added)
        {
            call(Native.NIM_DELETE, 0);
            Added = false;
        }

        _closed = true;
    }
}

/// <summary>The notification-area icon, drawn by the code, and its native menu.</summary>
internal sealed unsafe class TrayIcon : ITrayIcon
{
    private const uint IconId = 1;

    private readonly nint _hwnd;
    private readonly uint _callbackMessage;
    private readonly TrayPresence _presence;
    private nint _icon;
    private int _iconSize;
    private string _tip = "Hindsound";

    public TrayIcon(nint hwnd, uint callbackMessage)
    {
        _hwnd = hwnd;
        _callbackMessage = callbackMessage;
        _presence = new TrayPresence(Call);
    }

    /// <summary>A message of the icon, as the loop's window receives it: see <see cref="WindowsShell.NewTray"/>.</summary>
    public Action<uint, nuint>? Message { get; set; }

    /// <summary>Explorer started again: the icon must be added again.</summary>
    public Action? Lost { get; set; }

    /// <summary>Adds the icon, or adds it again after Explorer restarted. True if the icon is there.</summary>
    public bool Add(TrayState state, uint accent, string tip)
    {
        Replace(state, accent);
        _tip = tip;
        return _presence.Add();
    }

    /// <summary>Shows what changed, and adds the icon if it is not there. True if the icon is there.</summary>
    public bool Update(TrayState state, uint accent, string tip)
    {
        Replace(state, accent);
        _tip = tip;
        return _presence.Update();
    }

    /// <summary>The size of the icon now in the tray, in pixels.</summary>
    public int IconSize => _iconSize;

    /// <summary>The scale of the main screen changed since the icon was drawn. The notification area lives on that screen.</summary>
    public bool IconIsStale => IsStale(_iconSize, MainScreenDpi());

    /// <summary>Whether an icon of that size is the wrong one at that DPI.</summary>
    public static bool IsStale(int iconSize, uint dpi) => iconSize != SmallSizeAt(dpi);

    // GetSystemMetrics stays at the scale the process started with, and the shell would blur the icon to fit.
    public static int SmallSizeAt(uint dpi) => Native.GetSystemMetricsForDpi(Native.SM_CXSMICON, dpi);

    public static int BigSizeAt(uint dpi) => Native.GetSystemMetricsForDpi(Native.SM_CXICON, dpi);

    /// <summary>Returns the chosen command. Runs only on the user's own right click on the icon.</summary>
    public TrayCommand ShowMenu(int x, int y, IReadOnlyList<TrayMenuItem> items)
    {
        var menu = Native.CreatePopupMenu();
        foreach (var item in items)
        {
            if (item.Command == TrayCommand.None)
            {
                Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
            }
            else
            {
                Native.AppendMenuW(menu, item.Enabled ? Native.MF_STRING : Native.MF_GRAYED, (nuint)item.Command, item.Text);
            }
        }

        var bold = TrayMenu.DefaultOf(items);
        if (bold != TrayCommand.None)
        {
            Native.SetMenuDefaultItem(menu, (uint)bold, 0);
        }

        // Without being the foreground window, the menu would not close on an outside click.
        Native.SetForegroundWindow(_hwnd);
        var command = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_NONOTIFY, x, y, _hwnd, 0);
        Native.PostMessageW(_hwnd, Native.WM_NULL, 0, 0);
        Native.DestroyMenu(menu);
        return (TrayCommand)command;
    }

    public void Dispose()
    {
        _presence.Close();
        if (_icon != 0)
        {
            Native.DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private bool Call(uint message, uint flags)
    {
        Native.NOTIFYICONDATAW data = default;
        data.Size = (uint)sizeof(Native.NOTIFYICONDATAW);
        data.Hwnd = _hwnd;
        data.Id = IconId;
        data.Flags = flags;
        data.CallbackMessage = _callbackMessage;
        data.Icon = _icon;
        data.Version = Native.NOTIFYICON_VERSION_4;
        _tip.AsSpan(0, Math.Min(_tip.Length, 127)).CopyTo(new Span<char>(data.Tip, 127));
        return Native.Shell_NotifyIconW(message, &data) != 0;
    }

    private void Replace(TrayState state, uint accent)
    {
        var size = SmallSizeAt(MainScreenDpi());
        var fresh = CreateIcon(state, accent, size);
        if (_icon != 0)
        {
            Native.DestroyIcon(_icon);
        }

        _icon = fresh;
        _iconSize = size;
    }

    private static uint MainScreenDpi()
    {
        var main = Native.MonitorFromPoint(default, Native.MONITOR_DEFAULTTOPRIMARY);
        uint dpi = 96, dpiY;
        Native.GetDpiForMonitor(main, 0, &dpi, &dpiY);
        return dpi == 0 ? 96 : dpi;
    }

    /// <summary>The width of an icon in pixels, as Windows has it.</summary>
    internal static int WidthOf(nint icon)
    {
        Native.ICONINFO info;
        if (Native.GetIconInfo(icon, &info) == 0)
        {
            return 0;
        }

        Native.BITMAP bitmap = default;
        Native.GetObjectW(info.Color, sizeof(Native.BITMAP), &bitmap);
        Native.DeleteObject(info.Color);
        Native.DeleteObject(info.Mask);
        return bitmap.Width;
    }

    /// <summary>The pixels of a square icon, as Windows has them, in ARGB.</summary>
    internal static uint[] PixelsOf(nint icon)
    {
        var size = WidthOf(icon);
        Native.ICONINFO info;
        if (size == 0 || Native.GetIconInfo(icon, &info) == 0)
        {
            return [];
        }

        Native.BITMAPINFOHEADER header = default;
        header.Size = (uint)sizeof(Native.BITMAPINFOHEADER);
        header.Width = size;
        header.Height = -size;
        header.Planes = 1;
        header.BitCount = 32;
        var pixels = new uint[size * size];
        var dc = Native.CreateCompatibleDC(0);
        fixed (uint* bits = pixels)
        {
            Native.GetDIBits(dc, info.Color, 0, (uint)size, bits, &header, 0);
        }

        Native.DeleteDC(dc);
        Native.DeleteObject(info.Color);
        Native.DeleteObject(info.Mask);
        return pixels;
    }

    internal static nint CreateIcon(TrayState state, uint accent, int size)
    {
        var composed = TrayIconShape.Compose(state, size, accent);

        Native.BITMAPINFOHEADER info = default;
        info.Size = (uint)sizeof(Native.BITMAPINFOHEADER);
        info.Width = size;
        info.Height = -size;
        info.Planes = 1;
        info.BitCount = 32;
        void* bits;
        var color = Native.CreateDIBSection(0, &info, 0, &bits, 0, 0);
        var pixels = (uint*)bits;
        for (var i = 0; i < composed.Length; i++)
        {
            // An icon's alpha is not premultiplied.
            pixels[i] = composed[i];
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
