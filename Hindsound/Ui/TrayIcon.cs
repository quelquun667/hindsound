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

/// <summary>One line of the tray menu. <see cref="TrayCommand.None"/> is a separator.</summary>
internal readonly record struct TrayMenuItem(TrayCommand Command, string Text = "", bool Enabled = true);

/// <summary>What the tray menu offers. Pure: tested without any window.</summary>
internal static class TrayMenu
{
    /// <param name="paused">The capture is paused.</param>
    /// <param name="hasLastClip">There is a clip to copy.</param>
    /// <param name="closingAfterClip">Quit was asked for and a clip is still being written: a second Quit is all that is left to do.</param>
    public static IReadOnlyList<TrayMenuItem> Items(bool paused, bool hasLastClip, bool closingAfterClip)
    {
        if (closingAfterClip)
        {
            return [new(TrayCommand.Quit, "Quit")];
        }

        return
        [
            new(TrayCommand.Save30s, "Save last 30s"),
            new(TrayCommand.Save15s, "Save last 15s"),
            new(TrayCommand.Save1m, "Save last 1 min"),
            new(TrayCommand.Save5m, "Save last 5 min"),
            new(TrayCommand.None),
            new(TrayCommand.CopyLast, "Copy last clip", hasLastClip),
            new(TrayCommand.TogglePause, paused ? "Resume capture" : "Pause capture"),
            new(TrayCommand.OpenFolder, "Open clips folder"),
            new(TrayCommand.Settings, "Settings"),
            new(TrayCommand.None),
            new(TrayCommand.Quit, "Quit"),
        ];
    }
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

    /// <summary>The dark plate under the shape. Without it, a lime or white icon vanishes on a light taskbar.</summary>
    public const uint PlateColor = NotificationPalette.Fill;

    /// <summary>The finished icon: the shape, inset on its rounded plate, as ARGB with straight alpha.</summary>
    public static uint[] Compose(TrayState state, int size, uint accent)
    {
        var inset = Math.Max(2, size / 8);
        var inner = size - 2 * inset;
        var shape = Coverage(state, inner);
        var rgb = ColorOf(state, accent);
        var radius = Math.Max(2, size / 6);
        var pixels = new uint[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var plate = PlateCoverage(x, y, size, radius);
                if (plate <= 0)
                {
                    continue;
                }

                var inShape = x >= inset && x < inset + inner && y >= inset && y < inset + inner;
                var amount = inShape ? shape[(y - inset) * inner + x - inset] : 0;
                pixels[y * size + x] = ((uint)(plate * 255) << 24) | Blend(PlateColor, rgb, amount);
            }
        }

        return pixels;
    }

    private static float PlateCoverage(int x, int y, int size, int radius)
    {
        var cornerX = Math.Max(radius - (x + 0.5f), x + 0.5f - (size - radius));
        var cornerY = Math.Max(radius - (y + 0.5f), y + 0.5f - (size - radius));
        if (cornerX <= 0 || cornerY <= 0)
        {
            return 1;
        }

        return Pixels.Clamp(radius + 0.5f - MathF.Sqrt(cornerX * cornerX + cornerY * cornerY));
    }

    private static uint Blend(uint from, uint to, float amount)
    {
        uint Channel(int shift) =>
            (uint)MathF.Round(((from >> shift) & 0xFF) * (1 - amount) + ((to >> shift) & 0xFF) * amount) << shift;
        return Channel(16) | Channel(8) | Channel(0);
    }
}

/// <summary>
/// Which calls keep the icon in the notification area, and whether it is there. The calls are handed in, as a message and
/// its flags: tested without any shell.
/// </summary>
internal sealed class TrayPresence(Func<uint, uint, bool> call)
{
    public const uint AddFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP;
    public const uint ModifyFlags = Native.NIF_ICON | Native.NIF_TIP;

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

/// <summary>
/// An icon refused by the shell, as at the opening of a session, when Explorer is not ready yet: when to try again and what
/// to write about it. Without an icon there is no way to quit with the mouse. Pure: tested without any window.
/// </summary>
internal sealed class TrayAddRetry
{
    public const int DelayMs = 2000;
    public const int MaxTries = 60; // 2 min

    private bool _failureWritten;
    private bool _armed;
    private int _tries;

    /// <summary>Takes the result of a call. Returns what the journal gets: one line at the failure, one at the success, nothing else.</summary>
    public string? Note(bool there)
    {
        if (there)
        {
            _tries = 0;
            if (!_failureWritten)
            {
                return null;
            }

            _failureWritten = false;
            return "the tray icon is there now";
        }

        if (_failureWritten)
        {
            return null;
        }

        _failureWritten = true;
        return $"the tray icon could not be added: trying again every {DelayMs / 1000} s, {MaxTries} times";
    }

    /// <summary>Whether to arm the next try now. The caller has <see cref="Fired"/> called when it runs.</summary>
    public bool ShouldArm(bool there)
    {
        if (there || _armed || _tries >= MaxTries)
        {
            return false;
        }

        _armed = true;
        _tries++;
        return true;
    }

    public void Fired() => _armed = false;
}

/// <summary>The notification-area icon, drawn by the code, and its native menu.</summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    private readonly nint _hwnd;
    private readonly uint _callbackMessage;
    private readonly TrayPresence _presence;
    private nint _icon;
    private string _tip = "Hindsound";

    public TrayIcon(nint hwnd, uint callbackMessage)
    {
        _hwnd = hwnd;
        _callbackMessage = callbackMessage;
        _presence = new TrayPresence(Call);
    }

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
        var fresh = CreateIcon(state, accent);
        if (_icon != 0)
        {
            Native.DestroyIcon(_icon);
        }

        _icon = fresh;
    }

    internal static nint CreateIcon(TrayState state, uint accent)
    {
        var size = Native.GetSystemMetrics(Native.SM_CXSMICON);
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
