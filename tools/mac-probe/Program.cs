// Throwaway probe of the macOS port. Each point it establishes is printed as one line "PROBE <tag>.<name> <result>".
//
//   HindsoundProbe ui <tag> <hold-seconds>
//       Objective-C from C# (NSString, NSURL), the SDL3 window filled with lime #D4F53C (read back, pixel density, tray),
//       the pasteboard with a file URL, Carbon global hotkeys, SMAppService, CoreText font fallback; then keeps the window
//       on screen for <hold-seconds> so that the workflow can take a screenshot of it.
//   HindsoundProbe audio <tag> <tone-pid> <silent-pid> <seconds>
//       The Core Audio part, in the Objective-C library libprobeaudio.dylib (probe_audio.m): taps and input.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace HindsoundProbe;

internal static unsafe class Program
{
    private static string tag = "run";

    private static string Frameworks =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Frameworks"));

    internal static void P(string name, string value) => Console.WriteLine($"PROBE {tag}.{name} {value}");

    private static void Try(string name, Action action)
    {
        try { action(); }
        catch (Exception e) { P(name + ".exception", e.GetType().Name + ": " + e.Message.Replace('\n', ' ')); }
    }

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: ui <tag> <hold-seconds> | audio <tag> <tone-pid> <silent-pid> <seconds>");
            return 2;
        }
        tag = args[1];
        string build = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ProbeBuild")?.Value ?? "?";
        P("start", $"build={build} pid={Environment.ProcessId} exe={Environment.ProcessPath}");
        int rc = 0;
        try
        {
            ObjC.Init();
            ObjC.Send(ObjC.Class("NSAutoreleasePool"), "new");
            IntPtr bundle = ObjC.Send(ObjC.Class("NSBundle"), "mainBundle");
            P("bundle", $"id={ObjC.Str(ObjC.Send(bundle, "bundleIdentifier")) ?? "null"} path={ObjC.Str(ObjC.Send(bundle, "bundlePath"))}");
            rc = args[0] switch
            {
                "ui" => Ui(double.Parse(args[2], CultureInfo.InvariantCulture)),
                "audio" => Audio(int.Parse(args[2], CultureInfo.InvariantCulture), int.Parse(args[3], CultureInfo.InvariantCulture),
                    double.Parse(args[4], CultureInfo.InvariantCulture)),
                _ => 2,
            };
        }
        catch (Exception e)
        {
            P("exception", e.GetType().Name + ": " + e.Message.Replace('\n', ' '));
            rc = 1;
        }
        P("done", "rc=" + rc);
        Console.Out.Flush();
        return rc;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Audio: everything is in the Objective-C library, which prints its own PROBE lines.

    private static int Audio(int tonePid, int silentPid, double seconds)
    {
        IntPtr lib = NativeLibrary.Load(Path.Combine(Frameworks, "libprobeaudio.dylib"));
        var run = (delegate* unmanaged<IntPtr, int, int, double, int>)NativeLibrary.GetExport(lib, "probe_audio_run");
        IntPtr t = Marshal.StringToCoTaskMemUTF8(tag);
        Console.Out.Flush();
        int rc = run(t, tonePid, silentPid, seconds);
        Marshal.FreeCoTaskMem(t);
        return rc;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // UI and the rest.

    private static int Ui(double holdSeconds)
    {
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        Try("objc", ObjcBasics);
        IntPtr window = IntPtr.Zero, renderer = IntPtr.Zero;
        Try("sdl", () => (window, renderer) = SdlWindow());
        P("window_ready", window != IntPtr.Zero ? "1" : "0");
        Try("pasteboard", Pasteboard);
        Try("hotkey", Hotkeys);
        Try("login_item", LoginItem);
        Try("coretext", CoreTextFallback);
        Console.Out.Flush();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < holdSeconds)
        {
            if (renderer != IntPtr.Zero)
            {
                Sdl.SetRenderDrawColor(renderer, 0xD4, 0xF5, 0x3C, 0xFF);
                Sdl.RenderClear(renderer);
                Sdl.RenderPresent(renderer);
            }
            if (Sdl.PumpEvents != null) Sdl.PumpEvents();
            Thread.Sleep(16);
        }
        return 0;
    }

    private static void ObjcBasics()
    {
        const string text = "Hindsound 日本語 é ♪";
        IntPtr ns = ObjC.NSString(text);
        long length = ObjC.SendLong(ns, "length");
        string? back = ObjC.Str(ns);
        P("objc.nsstring", $"roundtrip={(back == text ? "ok" : "MISMATCH")} length={length} expected_utf16={text.Length}");
        IntPtr url = ObjC.Send(ObjC.Class("NSURL"), "fileURLWithPath:", ObjC.NSString("/tmp/Hindsound clip é.wav"));
        P("objc.nsurl", ObjC.Str(ObjC.Send(url, "absoluteString")) ?? "null");
    }

    private static (IntPtr window, IntPtr renderer) SdlWindow()
    {
        Sdl.Load(Path.Combine(Frameworks, "libSDL3.0.dylib"));
        int version = Sdl.GetVersion();
        if (Sdl.Init(0x20) == 0) // SDL_INIT_VIDEO
        {
            P("sdl.init", "FAIL " + Sdl.Error());
            return (IntPtr.Zero, IntPtr.Zero);
        }
        P("sdl.init", $"ok version={version} video_driver={Marshal.PtrToStringUTF8(Sdl.GetCurrentVideoDriver())}");
        // SDL_WINDOW_HIGH_PIXEL_DENSITY | SDL_WINDOW_ALWAYS_ON_TOP
        IntPtr title = Marshal.StringToCoTaskMemUTF8("Hindsound probe");
        IntPtr window = Sdl.CreateWindow(title, 480, 320, 0x2000UL | 0x10000UL);
        if (window == IntPtr.Zero)
        {
            P("sdl.window", "FAIL " + Sdl.Error());
            return (IntPtr.Zero, IntPtr.Zero);
        }
        IntPtr renderer = Sdl.CreateRenderer(window, IntPtr.Zero);
        if (renderer == IntPtr.Zero)
        {
            P("sdl.renderer", "FAIL " + Sdl.Error());
            return (window, IntPtr.Zero);
        }
        P("sdl.renderer", "ok " + Marshal.PtrToStringUTF8(Sdl.GetRendererName(renderer)));
        Sdl.SetRenderDrawColor(renderer, 0xD4, 0xF5, 0x3C, 0xFF);
        Sdl.RenderClear(renderer);
        IntPtr read = Sdl.RenderReadPixels(renderer, IntPtr.Zero);
        if (read == IntPtr.Zero)
        {
            P("sdl.readback", "FAIL " + Sdl.Error());
        }
        else
        {
            IntPtr rgba = Sdl.ConvertSurface(read, 0x16762004u); // SDL_PIXELFORMAT_ABGR8888 = RGBA32: bytes R, G, B, A
            if (rgba == IntPtr.Zero)
            {
                P("sdl.readback", "FAIL convert " + Sdl.Error());
            }
            else
            {
                int w = *(int*)(rgba + 8), h = *(int*)(rgba + 12), pitch = *(int*)(rgba + 16);
                byte* pixels = *(byte**)(rgba + 24);
                long lime = 0;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        byte* p = pixels + (long)y * pitch + x * 4;
                        if (p[0] == 0xD4 && p[1] == 0xF5 && p[2] == 0x3C) lime++;
                    }
                }
                P("sdl.readback", $"{w}x{h} lime={lime} of {(long)w * h}");
                Sdl.DestroySurface(rgba);
            }
            Sdl.DestroySurface(read);
        }
        Sdl.RenderPresent(renderer);
        Sdl.RaiseWindow(window);
        for (int i = 0; i < 30; i++)
        {
            Sdl.PumpEvents();
            Sdl.SetRenderDrawColor(renderer, 0xD4, 0xF5, 0x3C, 0xFF);
            Sdl.RenderClear(renderer);
            Sdl.RenderPresent(renderer);
            Thread.Sleep(16);
        }
        int pw, ph;
        Sdl.GetWindowSizeInPixels(window, &pw, &ph);
        P("sdl.density", $"pixel_density={Sdl.GetWindowPixelDensity(window).ToString(CultureInfo.InvariantCulture)} " +
            $"display_scale={Sdl.GetWindowDisplayScale(window).ToString(CultureInfo.InvariantCulture)} size_in_pixels={pw}x{ph} size_in_points=480x320");
        IntPtr tooltip = Marshal.StringToCoTaskMemUTF8("Hindsound probe");
        IntPtr tray = Sdl.CreateTray(IntPtr.Zero, tooltip);
        P("sdl.tray", tray != IntPtr.Zero ? "object" : "NULL " + Sdl.Error());
        return (window, renderer);
    }

    private static void Pasteboard()
    {
        string path = "/tmp/hindsound-probe-clip.wav";
        File.WriteAllBytes(path, new byte[44]);
        IntPtr url = ObjC.Send(ObjC.Class("NSURL"), "fileURLWithPath:", ObjC.NSString(path));
        string? expected = ObjC.Str(ObjC.Send(url, "absoluteString"));
        IntPtr board = ObjC.Send(ObjC.Class("NSPasteboard"), "generalPasteboard");
        long change = ObjC.SendLong(board, "clearContents");
        IntPtr array = ObjC.Send(ObjC.Class("NSArray"), "arrayWithObject:", url);
        bool written = ObjC.SendBool(board, "writeObjects:", array);
        string? back = ObjC.Str(ObjC.Send(board, "stringForType:", ObjC.NSString("public.file-url")));
        IntPtr types = ObjC.Send(board, "types");
        long count = types == IntPtr.Zero ? 0 : ObjC.SendLong(types, "count");
        var names = new string[count];
        for (long i = 0; i < count; i++) names[i] = ObjC.Str(ObjC.Send(types, "objectAtIndex:", (IntPtr)i)) ?? "?";
        P("pasteboard.write", $"writeObjects={written} changeCount={change}");
        P("pasteboard.read", $"public.file-url={back ?? "null"} match={(back != null && back == expected ? "yes" : "NO")} types={string.Join(",", names)}");
    }

    private static void Hotkeys()
    {
        IntPtr target = Carbon.GetApplicationEventTarget();
        P("hotkey.target", target != IntPtr.Zero ? "ok" : "NULL");
        // Carbon modifiers: cmdKey 0x100, shiftKey 0x200, optionKey 0x800, controlKey 0x1000. Key codes: F9 0x65, S 0x01.
        var cases = new (string name, uint key, uint mods)[]
        {
            ("ctrl_opt_f9", 0x65, 0x1000 | 0x800),
            ("cmd_shift_f9", 0x65, 0x100 | 0x200),
            ("opt_s", 0x01, 0x800),
            ("opt_shift_s", 0x01, 0x800 | 0x200),
        };
        uint id = 1;
        foreach (var c in cases)
        {
            var hotKeyId = new Carbon.EventHotKeyID { Signature = 0x48534e44, Id = id++ }; // 'HSND'
            int status = Carbon.RegisterEventHotKey(c.key, c.mods, hotKeyId, target, 0, out IntPtr reference);
            P("hotkey." + c.name, "status=" + status);
            if (status == 0 && reference != IntPtr.Zero) Carbon.UnregisterEventHotKey(reference);
        }
    }

    private static void LoginItem()
    {
        NativeLibrary.Load("/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement");
        IntPtr cls = ObjC.Class("SMAppService");
        if (cls == IntPtr.Zero)
        {
            P("login_item.status", "NO_CLASS");
            return;
        }
        IntPtr service = ObjC.Send(cls, "mainAppService");
        long status = ObjC.SendLong(service, "status");
        string name = status switch { 0 => "notRegistered", 1 => "enabled", 2 => "requiresApproval", 3 => "notFound", _ => "?" };
        P("login_item.status", $"{status} {name}");
    }

    private static void CoreTextFallback()
    {
        IntPtr baseFont = CoreText.CTFontCreateWithName(ObjC.NSString("Helvetica"), 14, IntPtr.Zero);
        P("coretext.base", ObjC.Str(CoreText.CTFontCopyPostScriptName(baseFont)) ?? "null");
        const string text = "日本語";
        IntPtr fallback = CoreText.CTFontCreateForString(baseFont, ObjC.NSString(text),
            new CoreText.CFRange { Location = 0, Length = text.Length });
        P("coretext.fallback", fallback == IntPtr.Zero ? "NULL"
            : $"postscript={ObjC.Str(CoreText.CTFontCopyPostScriptName(fallback))} family={ObjC.Str(CoreText.CTFontCopyFamilyName(fallback))}");
    }
}

internal static unsafe class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";
    private static IntPtr msgSend;

    [DllImport(Lib)] private static extern IntPtr objc_getClass(string name);
    [DllImport(Lib)] private static extern IntPtr sel_registerName(string name);

    public static void Init() => msgSend = NativeLibrary.GetExport(NativeLibrary.Load(Lib), "objc_msgSend");
    public static IntPtr Class(string name) => objc_getClass(name);

    public static IntPtr Send(IntPtr o, string sel) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)msgSend)(o, sel_registerName(sel));
    public static IntPtr Send(IntPtr o, string sel, IntPtr a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)msgSend)(o, sel_registerName(sel), a);
    public static long SendLong(IntPtr o, string sel) =>
        ((delegate* unmanaged<IntPtr, IntPtr, long>)msgSend)(o, sel_registerName(sel));
    public static bool SendBool(IntPtr o, string sel, IntPtr a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)msgSend)(o, sel_registerName(sel), a) != 0;

    public static IntPtr NSString(string s)
    {
        IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(s);
        try { return Send(Class("NSString"), "stringWithUTF8String:", utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    public static string? Str(IntPtr ns) => ns == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(ns, "UTF8String"));
}

internal static class Carbon
{
    private const string Lib = "/System/Library/Frameworks/Carbon.framework/Carbon";

    [StructLayout(LayoutKind.Sequential)]
    public struct EventHotKeyID
    {
        public uint Signature;
        public uint Id;
    }

    [DllImport(Lib)] public static extern IntPtr GetApplicationEventTarget();
    [DllImport(Lib)]
    public static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID id, IntPtr target, uint options,
        out IntPtr reference);
    [DllImport(Lib)] public static extern int UnregisterEventHotKey(IntPtr reference);
}

internal static class CoreText
{
    private const string Lib = "/System/Library/Frameworks/CoreText.framework/CoreText";

    [StructLayout(LayoutKind.Sequential)]
    public struct CFRange
    {
        public nint Location;
        public nint Length;
    }

    [DllImport(Lib)] public static extern IntPtr CTFontCreateWithName(IntPtr name, double size, IntPtr matrix);
    [DllImport(Lib)] public static extern IntPtr CTFontCreateForString(IntPtr font, IntPtr text, CFRange range);
    [DllImport(Lib)] public static extern IntPtr CTFontCopyPostScriptName(IntPtr font);
    [DllImport(Lib)] public static extern IntPtr CTFontCopyFamilyName(IntPtr font);
}

internal static unsafe class Sdl
{
    public static delegate* unmanaged<uint, byte> Init;
    public static delegate* unmanaged<IntPtr> GetError;
    public static delegate* unmanaged<int> GetVersion;
    public static delegate* unmanaged<IntPtr> GetCurrentVideoDriver;
    public static delegate* unmanaged<IntPtr, int, int, ulong, IntPtr> CreateWindow;
    public static delegate* unmanaged<IntPtr, IntPtr, IntPtr> CreateRenderer;
    public static delegate* unmanaged<IntPtr, IntPtr> GetRendererName;
    public static delegate* unmanaged<IntPtr, byte, byte, byte, byte, byte> SetRenderDrawColor;
    public static delegate* unmanaged<IntPtr, byte> RenderClear;
    public static delegate* unmanaged<IntPtr, byte> RenderPresent;
    public static delegate* unmanaged<IntPtr, IntPtr, IntPtr> RenderReadPixels;
    public static delegate* unmanaged<IntPtr, uint, IntPtr> ConvertSurface;
    public static delegate* unmanaged<IntPtr, void> DestroySurface;
    public static delegate* unmanaged<void> PumpEvents;
    public static delegate* unmanaged<IntPtr, float> GetWindowPixelDensity;
    public static delegate* unmanaged<IntPtr, float> GetWindowDisplayScale;
    public static delegate* unmanaged<IntPtr, int*, int*, byte> GetWindowSizeInPixels;
    public static delegate* unmanaged<IntPtr, byte> RaiseWindow;
    public static delegate* unmanaged<IntPtr, IntPtr, IntPtr> CreateTray;

    public static void Load(string path)
    {
        IntPtr lib = NativeLibrary.Load(path);
        IntPtr F(string name) => NativeLibrary.GetExport(lib, "SDL_" + name);
        Init = (delegate* unmanaged<uint, byte>)F("Init");
        GetError = (delegate* unmanaged<IntPtr>)F("GetError");
        GetVersion = (delegate* unmanaged<int>)F("GetVersion");
        GetCurrentVideoDriver = (delegate* unmanaged<IntPtr>)F("GetCurrentVideoDriver");
        CreateWindow = (delegate* unmanaged<IntPtr, int, int, ulong, IntPtr>)F("CreateWindow");
        CreateRenderer = (delegate* unmanaged<IntPtr, IntPtr, IntPtr>)F("CreateRenderer");
        GetRendererName = (delegate* unmanaged<IntPtr, IntPtr>)F("GetRendererName");
        SetRenderDrawColor = (delegate* unmanaged<IntPtr, byte, byte, byte, byte, byte>)F("SetRenderDrawColor");
        RenderClear = (delegate* unmanaged<IntPtr, byte>)F("RenderClear");
        RenderPresent = (delegate* unmanaged<IntPtr, byte>)F("RenderPresent");
        RenderReadPixels = (delegate* unmanaged<IntPtr, IntPtr, IntPtr>)F("RenderReadPixels");
        ConvertSurface = (delegate* unmanaged<IntPtr, uint, IntPtr>)F("ConvertSurface");
        DestroySurface = (delegate* unmanaged<IntPtr, void>)F("DestroySurface");
        PumpEvents = (delegate* unmanaged<void>)F("PumpEvents");
        GetWindowPixelDensity = (delegate* unmanaged<IntPtr, float>)F("GetWindowPixelDensity");
        GetWindowDisplayScale = (delegate* unmanaged<IntPtr, float>)F("GetWindowDisplayScale");
        GetWindowSizeInPixels = (delegate* unmanaged<IntPtr, int*, int*, byte>)F("GetWindowSizeInPixels");
        RaiseWindow = (delegate* unmanaged<IntPtr, byte>)F("RaiseWindow");
        CreateTray = (delegate* unmanaged<IntPtr, IntPtr, IntPtr>)F("CreateTray");
    }

    public static string Error() => Marshal.PtrToStringUTF8(GetError()) ?? "";
}
