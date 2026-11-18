using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Input;
using Hindsound.Linux.App;
using Hindsound.Linux.Window;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Linux.Tests.Window;

/// <summary>A text of fixed advance that fills a box per character, so a page drawn with it leaves pixels.</summary>
internal sealed class BlockText : ICanvasText
{
    public static int Made;
    public static int Disposed;

    public BlockText() => Interlocked.Increment(ref Made);

    public float Measure(FontSpec font, string text) => text.Length * font.Size * 0.5f;

    public float LineHeight(FontSpec font) => MathF.Round(font.Size * 1.25f);

    public void Draw(Pixels target, FontSpec font, string text, float x, float top, uint rgb)
    {
        var advance = font.Size * 0.5f;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ')
            {
                var left = (int)MathF.Round(x + i * advance);
                target.FillRect(left, (int)top, left + Math.Max(1, (int)advance - 1), (int)top + (int)LineHeight(font), rgb);
            }
        }
    }

    public void Dispose() => Interlocked.Increment(ref Disposed);
}

/// <summary>A backend that only counts what the page asks.</summary>
internal sealed class CountingBackend : ISettingsBackend
{
    public List<SettingsChange> Changes { get; } = [];

    public void Applied(SettingsChange change) => Changes.Add(change);

    public void SaveNow()
    {
    }

    public void TogglePause()
    {
    }

    public void CopyLast()
    {
    }

    public void OpenFolder()
    {
    }

    public void ShowTestNotification()
    {
    }

    public void SuspendHotkeys()
    {
    }

    public IReadOnlyList<HotkeyResult> ApplyHotkeys(HotkeySettings hotkeys) => [];

    public IReadOnlyList<HotkeyResult> ResumeHotkeys() => [];
}

/// <summary>A pump whose clock moves by each wait, and whose queue is the fake video's.</summary>
[SupportedOSPlatform("linux")]
internal sealed class FakePump(FakeVideo video, SdlEventHandler handler) : ILoopPump
{
    public SdlEventHandler? Handler { get; set; } = handler;

    public void Wait(int timeoutMs)
    {
        video.Clock += timeoutMs < 0 ? 1000 : timeoutMs;
        while (video.Queue.TryDequeue(out var e))
        {
            Handler?.Invoke(e);
        }
    }

    public void Wake()
    {
    }
}

/// <summary>SDL as the window sees it, with nothing native: what it was asked is kept for the test to read.</summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class FakeVideo : ISdlVideo
{
    public long Clock;

    public ConcurrentQueue<SdlNative.Event> Queue { get; } = new();

    public bool Started { get; private set; }

    public bool StartedOffscreen { get; private set; }

    public int Closes { get; private set; }

    public (int W, int H)? Created { get; private set; }

    public List<(int X, int Y, int W, int H)> Presents { get; } = [];

    public List<(int X, int Y, int W, int H)> Places { get; } = [];

    public int Icons { get; private set; }

    public bool TextInputOn { get; private set; }

    public bool? Capture { get; private set; }

    public string? Clipboard { get; set; }

    public string? Folder { get; set; }

    public ScreenRect Area { get; set; } = new(0, 0, 1920, 1080);

    public float Scale { get; set; } = 1;

    public float Density { get; set; } = 1;

    public WindowBorders FrameSize { get; set; }

    public bool Focused { get; set; }

    public uint WindowId => 7;

    public float DisplayScale => Scale;

    public float PixelDensity => Density;

    public (int W, int H) PixelSize => ((int)Math.Round(Bounds.W * Density), (int)Math.Round(Bounds.H * Density));

    public WindowBorders Borders => FrameSize;

    public (int X, int Y, int W, int H) Bounds { get; private set; }

    public bool HasFocus => Focused;

    /// <summary>When set, <see cref="Start"/> fails as SDL does when its video cannot start, with this as SDL's error.</summary>
    public string? StartFails { get; set; }

    /// <summary>When set, <see cref="Create"/> fails as SDL does when it makes no window, with this as SDL's error.</summary>
    public string? CreateFails { get; set; }

    public void Start(bool offscreen)
    {
        if (StartFails is { } error)
        {
            throw new InvalidOperationException("SDL video did not start: " + error);
        }

        Started = true;
        StartedOffscreen = offscreen;
    }

    public (ScreenRect Area, float Scale) ScreenUnderMouse() => (Area, Scale);

    public void Create(string title, int width, int height, int x, int y)
    {
        if (CreateFails is { } error)
        {
            throw new InvalidOperationException("SDL made no window: " + error);
        }

        Created = (width, height);
        Bounds = (x, y, width, height);
    }

    public ScreenRect UsableArea() => Area;

    public void Place(int x, int y, int width, int height)
    {
        Places.Add((x, y, width, height));
        Bounds = (x, y, width, height);
    }

    public void Show()
    {
    }

    public void BringBack()
    {
    }

    public void SetIcon(uint[] argb, int side)
    {
        Assert.Equal(side * side, argb.Length);
        Icons++;
    }

    public void Present(uint* bits, int width, int height, (int X, int Y, int W, int H) area) => Presents.Add(area);

    public void CaptureMouse(bool on) => Capture = on;

    public ushort ModState() => 0;

    public void TextInput(bool wanted, int x, int y, int w, int h, int cursor) => TextInputOn = wanted;

    public string? ClipboardText() => Clipboard;

    public void SetClipboardText(string text) => Clipboard = text;

    public string? PickFolder(string startFolder, Action pumpOnce)
    {
        pumpOnce();
        return Folder;
    }

    public ILoopPump NewPump(SdlEventHandler handler) => new FakePump(this, handler);

    public bool Push(in SdlNative.Event e)
    {
        Queue.Enqueue(e);
        return true;
    }

    public void Close() => Closes++;
}

/// <summary>A window on the fake video, its loop, and the events a test sends it.</summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class WindowRig : IDisposable
{
    private readonly List<nint> _strings = [];

    public WindowRig(bool offscreen = true, int zoom = 100, Action<FakeVideo>? setUp = null)
    {
        Video = new FakeVideo();
        setUp?.Invoke(Video);
        Loop = new LinuxLoop(() => Video.Clock);
        Settings = new AppSettings().Normalize();
        Settings.General.WindowZoom = zoom;
        var request = new SettingsWindowRequest(Settings, Backend, () => { MicReads++; return 0.5f; }, false, offscreen, default, () => ClosedCount++);
        Window = new LinuxSettingsWindow(request, Loop, () => new BlockText(), Video);
    }

    public FakeVideo Video { get; }

    public LinuxLoop Loop { get; }

    public AppSettings Settings { get; }

    public CountingBackend Backend { get; } = new();

    public LinuxSettingsWindow Window { get; }

    public int MicReads { get; private set; }

    public int ClosedCount { get; private set; }

    /// <summary>Hands out what is in the queue, runs what that posted, then stops.</summary>
    public void Drain()
    {
        Loop.Pump?.Wait(0);
        Loop.Post(Loop.Quit);
        Loop.Run();
    }

    /// <summary>Runs the loop for that long on its clock.</summary>
    public void RunFor(int milliseconds)
    {
        Loop.After(milliseconds, Loop.Quit);
        Loop.Run();
    }

    public void Send(SdlNative.Event e) => Window.Handle(e);

    public SdlNative.Event Button(uint type, float designX, float designY, byte button = SdlNative.ButtonLeft, byte clicks = 1)
    {
        var e = default(SdlNative.Event);
        e.Button.Type = type;
        e.Button.WindowId = Video.WindowId;
        e.Button.Button = button;
        e.Button.Clicks = clicks;
        e.Button.X = designX * Window.Scale / Video.Density;
        e.Button.Y = designY * Window.Scale / Video.Density;
        return e;
    }

    public void Click(float designX, float designY)
    {
        Send(Button(SdlNative.EventMouseButtonDown, designX, designY));
        Send(Button(SdlNative.EventMouseButtonUp, designX, designY));
    }

    public SdlNative.Event WindowEvent(uint type)
    {
        var e = default(SdlNative.Event);
        e.Window.Type = type;
        e.Window.WindowId = Video.WindowId;
        return e;
    }

    public SdlNative.Event TextInput(string text)
    {
        var e = default(SdlNative.Event);
        e.Text.Type = SdlNative.EventTextInput;
        e.Text.WindowId = Video.WindowId;
        var native = Marshal.StringToCoTaskMemUTF8(text);
        _strings.Add(native);
        e.Text.Text = (byte*)native;
        return e;
    }

    public void Dispose()
    {
        if (!Window.IsClosed)
        {
            Window.Dispose();
        }

        Loop.Dispose();
        foreach (var native in _strings)
        {
            Marshal.FreeCoTaskMem(native);
        }
    }
}
