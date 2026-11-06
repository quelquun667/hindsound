using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Hindsound.Ui;

/// <summary>
/// The message loop and its one hidden top-level window. Top-level, not message-only: only those receive the
/// TaskbarCreated broadcast (Explorer restarted, the tray icon must come back) and the end of the Windows session.
/// </summary>
internal sealed unsafe class UiLoop : IDisposable
{
    public const uint TrayCallbackMessage = Native.WM_APP + 3;
    private const string ClassName = "HindsoundMessages";
    private const uint PostedWork = Native.WM_APP + 1;

    private static UiLoop? _current;

    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly Dictionary<nuint, Action> _timers = [];
    private readonly uint _taskbarCreated;
    private nuint _nextTimer = 1;

    public UiLoop()
    {
        _current = this;
        var instance = Native.GetModuleHandleW(null);
        fixed (char* name = ClassName)
        {
            Native.WNDCLASSEXW cls = default;
            cls.Size = (uint)sizeof(Native.WNDCLASSEXW);
            cls.WndProc = &WndProc;
            cls.Instance = instance;
            cls.ClassName = name;
            Native.RegisterClassExW(&cls);
        }

        _taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");
        Hwnd = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW, ClassName, "Hindsound", Native.WS_POPUP, 0, 0, 0, 0, 0, 0, instance, 0);
        if (Hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed");
        }
    }

    public nint Hwnd { get; }

    /// <summary>A message of the tray icon: the notify code, then the raw wParam.</summary>
    public Action<uint, nuint>? TrayMessage { get; set; }

    /// <summary>Explorer started again: the tray icon has to be added again.</summary>
    public Action? TaskbarCreated { get; set; }

    /// <summary>
    /// Windows is ending the session, or an installer or the Restart Manager wants this app closed (ENDSESSION_CLOSEAPP).
    /// In that second case Windows goes on and nothing ends the process for us: <see cref="Run"/> ends right after this.
    /// </summary>
    public Action? SessionEnding { get; set; }

    /// <summary>Windows asks if the session may end. The answer is always yes.</summary>
    public Action? SessionEndAsked { get; set; }

    /// <summary>Windows had asked, and the session goes on after all.</summary>
    public Action? SessionKept { get; set; }

    /// <summary>Runs the action on the loop's thread, soon. Safe from any thread.</summary>
    public void Post(Action action)
    {
        _posted.Enqueue(action);
        Native.PostMessageW(Hwnd, PostedWork, 0, 0);
    }

    /// <summary>Runs the action once on the loop's thread, after the delay. Call it on the loop's thread.</summary>
    public void After(int milliseconds, Action action)
    {
        var id = _nextTimer++;
        _timers[id] = action;
        Native.SetTimer(Hwnd, id, (uint)Math.Max(1, milliseconds), 0);
    }

    /// <summary>Ends <see cref="Run"/>. Call it on the loop's thread.</summary>
    public void Quit() => Native.PostQuitMessage(0);

    public void Run()
    {
        Native.MSG msg;
        while (Native.GetMessageW(&msg, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(&msg);
            Native.DispatchMessageW(&msg);
        }
    }

    public void Dispose() => Native.DestroyWindow(Hwnd);

    private nint Handle(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case PostedWork:
                while (_posted.TryDequeue(out var work))
                {
                    RunGuarded(work);
                }

                return 0;

            case Native.WM_TIMER:
                Native.KillTimer(hwnd, wParam);
                if (_timers.Remove(wParam, out var timed))
                {
                    RunGuarded(timed);
                }

                return 0;

            case TrayCallbackMessage:
                TrayMessage?.Invoke((uint)(lParam & 0xFFFF), wParam);
                return 0;

            case Native.WM_QUERYENDSESSION:
                try
                {
                    SessionEndAsked?.Invoke();
                }
                catch (Exception ex)
                {
                    UiErrors.Report(ex);
                }

                return 1;

            case Native.WM_ENDSESSION:
                if (wParam != 0)
                {
                    try
                    {
                        SessionEnding?.Invoke();
                    }
                    finally
                    {
                        Quit();
                    }
                }
                else
                {
                    SessionKept?.Invoke();
                }

                return 0;

            case Native.WM_CLOSE:
                Quit();
                return 0;

            default:
                if (message == _taskbarCreated)
                {
                    TaskbarCreated?.Invoke();
                    return 0;
                }

                return Native.DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    // One failed piece of work must not take the rest of the queue with it.
    private static void RunGuarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            UiErrors.Report(ex);
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        // An exception crossing this boundary would end the process, and the buffer with it.
        try
        {
            return _current is { } loop ? loop.Handle(hwnd, message, wParam, lParam) : Native.DefWindowProcW(hwnd, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            UiErrors.Report(ex);
            return 0;
        }
    }
}
