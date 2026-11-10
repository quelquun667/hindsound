namespace Hindsound.Ui;

/// <summary>What the icon tells at a glance. Each state has its own shape, not only its own colour.</summary>
public enum TrayState
{
    /// <summary>The H of the logo: two stems and the wave between them.</summary>
    Active,

    /// <summary>The H loses its wave: two stems, the pause sign.</summary>
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

    // Last, so the numbers above never move: the --offscreen tests post them.
    UpdateNow,
    OpenHindsound,
    Library,

    // The menu of an app without a window.
    SaveDefault,
    StartWithSession,
}

/// <summary>One line of the tray menu. <see cref="TrayCommand.None"/> is a separator.</summary>
/// <param name="Checked">A line that is on or off, such as Start with the session; null for a plain command.</param>
internal readonly record struct TrayMenuItem(TrayCommand Command, string Text = "", bool Enabled = true, bool? Checked = null);

/// <summary>What a gesture on the icon asks for.</summary>
internal enum TrayGesture
{
    None,

    /// <summary>A right click, or the desktop about to show the menu.</summary>
    Menu,

    /// <summary>Opens the app, on the Now page.</summary>
    OpenSettings,
}

/// <summary>What was true at the right click on the icon. Showing the menu changes both: the hidden window becomes the active one.</summary>
/// <param name="AppName">For <c>{app}</c>; null if the active window says nothing about what the user was doing.</param>
/// <param name="Screen">The screen of the click, where the answer to a command goes.</param>
/// <param name="AtMs">When it happened.</param>
internal readonly record struct TrayClick(string? AppName, ScreenInfo Screen, long AtMs)
{
    /// <summary>How long a command without a result of its own, like Pause, still counts as the reason for the next notification.</summary>
    public const int ScreenKeptMs = 5000;

    /// <summary>The screen of the click, if what happens now can still be the answer to it.</summary>
    public ScreenInfo? ScreenAt(long nowMs) => nowMs - AtMs <= ScreenKeptMs ? Screen : null;

    /// <summary>
    /// A menu closed without a command leaves our hidden window the active one, with no size and no title, until the user
    /// goes elsewhere. As long as it does, the last click says more about what the user is doing than the active window can.
    /// </summary>
    /// <param name="hiddenWindowActive">Our hidden window is the active one right now.</param>
    /// <param name="last">The last right click on the icon, if any.</param>
    public static TrayClick? StandingIn(bool hiddenWindowActive, TrayClick? last) => hiddenWindowActive ? last : null;
}

/// <summary>What the tray menu offers. Pure: tested without any window.</summary>
internal static class TrayMenu
{
    /// <summary>What was true at the click comes first: <paramref name="show"/> makes our own hidden window the active one.</summary>
    public static (TrayClick Click, TrayCommand Command) ReadThenShow(Func<TrayClick> read, Func<TrayCommand> show)
    {
        var click = read();
        return (click, show());
    }

    /// <param name="paused">The capture is paused.</param>
    /// <param name="hasLastClip">There is a clip to copy.</param>
    /// <param name="closingAfterClip">Quit was asked for and a clip is still being written: a second Quit is all that is left to do.</param>
    /// <param name="updateText">The line that offers a new version, such as "Update to 0.2.0..."; null when there is none to offer.</param>
    /// <param name="buffered">What the memory holds; null when nothing has said yet.</param>
    public static IReadOnlyList<TrayMenuItem> Items(bool paused, bool hasLastClip, bool closingAfterClip, string? updateText = null, TimeSpan? buffered = null)
    {
        if (closingAfterClip)
        {
            return [new(TrayCommand.Quit, "Quit")];
        }

        List<TrayMenuItem> items =
        [
            new(TrayCommand.OpenHindsound, "Open Hindsound"),
            new(TrayCommand.Library, "Library"),
            SaveItem(TrayCommand.Save30s, "Save up to 30s", 30, buffered),
            SaveItem(TrayCommand.Save15s, "Save up to 15s", 15, buffered),
            SaveItem(TrayCommand.Save1m, "Save up to 1 min", 60, buffered),
            SaveItem(TrayCommand.Save5m, "Save up to 5 min", 300, buffered),
            new(TrayCommand.None),
            new(TrayCommand.CopyLast, "Copy last clip", hasLastClip),
            new(TrayCommand.TogglePause, paused ? "Resume capture" : "Pause capture"),
            new(TrayCommand.OpenFolder, "Open clips folder"),
            new(TrayCommand.Settings, "Settings"),
            new(TrayCommand.None),
        ];

        if (updateText is not null)
        {
            items.Add(new(TrayCommand.UpdateNow, updateText));
        }

        items.Add(new(TrayCommand.Quit, "Quit"));
        return items;
    }

    /// <summary>
    /// The menu of an app without a window, where the settings are edited by hand: no page to open, and starting with the
    /// session is a line of the menu.
    /// </summary>
    /// <param name="startsWithSession">The app starts with the user's session.</param>
    /// <param name="clipSeconds">The length of a clip, as the shortcut saves it.</param>
    public static IReadOnlyList<TrayMenuItem> WindowlessItems(bool paused, bool startsWithSession, int clipSeconds, bool closingAfterClip, TimeSpan? buffered = null)
    {
        if (closingAfterClip)
        {
            return [new(TrayCommand.Quit, "Quit")];
        }

        return
        [
            SaveItem(TrayCommand.SaveDefault, "Save clip", clipSeconds, buffered),
            new(TrayCommand.None),
            new(TrayCommand.TogglePause, paused ? "Resume capture" : "Pause capture"),
            new(TrayCommand.OpenFolder, "Open clips folder"),
            new(TrayCommand.StartWithSession, "Start with the session", Checked: startsWithSession),
            new(TrayCommand.None),
            new(TrayCommand.Quit, "Quit"),
        ];
    }

    /// <summary><see cref="WindowlessItems"/> from the last status of the engine, read every time the menu opens.</summary>
    public static IReadOnlyList<TrayMenuItem> WindowlessItemsFor(Audio.AudioEngineStatus? status, bool startsWithSession, int clipSeconds, bool closingAfterClip) =>
        WindowlessItems(status?.Activity == Audio.CaptureActivity.Paused, startsWithSession, clipSeconds, closingAfterClip, status?.Buffered);

    /// <summary>The line in bold, the same gesture as a left click on the icon. None when the menu does not offer it.</summary>
    public static TrayCommand DefaultOf(IReadOnlyList<TrayMenuItem> items) =>
        items.Any(item => item.Command == TrayCommand.OpenHindsound) ? TrayCommand.OpenHindsound : TrayCommand.None;

    // The menu is read in one look, just before the clip: an entry says when it would come out shorter than its words.
    /// <summary><see cref="Items"/> from the last status of the engine: whether it is paused and what the memory holds are read from it, every time the menu opens.</summary>
    public static IReadOnlyList<TrayMenuItem> ItemsFor(Audio.AudioEngineStatus? status, bool hasLastClip, bool closingAfterClip, string? updateText) =>
        Items(status?.Activity == Audio.CaptureActivity.Paused, hasLastClip, closingAfterClip, updateText, status?.Buffered);

    private static TrayMenuItem SaveItem(TrayCommand command, string text, int seconds, TimeSpan? buffered)
    {
        if (buffered is not { } held)
        {
            return new(command, text);
        }

        // A second short is not worth saying: the band calls that memory full, and the two must agree.
        var kept = KeptDuration.SecondsOf(held);
        return new(
            command,
            seconds - kept > 1 && KeptDuration.CanSave(held) ? $"{text} (only {NotificationStates.FormatDuration(kept)})" : text,
            KeptDuration.CanSave(held));
    }
}

/// <summary>The shapes of the icon, as coverage from 0 to 1. Pure: tested without any window.</summary>
internal static class TrayIconShape
{
    public static float[] Coverage(TrayState state, int size) => state switch
    {
        TrayState.Active => TrayIconMark.Coverage(withWave: true, size),
        TrayState.Paused => TrayIconMark.Coverage(withWave: false, size),
        _ => Triangle(size),
    };

    private static float[] Triangle(int size)
    {
        var coverage = new float[size * size];
        var scale = size / 24f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                coverage[y * size + x] = Pixels.Clamp(0.5f - NotificationIcons.Distance(NotificationVisual.Triangle, (x + 0.5f) / scale, (y + 0.5f) / scale) * scale);
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
