using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Hindsound.Input;

/// <summary>
/// RegisterHotKey without any window: a dedicated background thread registers with a null window handle, so WM_HOTKEY
/// lands in that thread's own message queue, read by a GetMessage loop. Register and Unregister must run on that same
/// thread, so requests are queued, announced with a WM_APP thread message, and the caller waits for the result.
/// </summary>
internal sealed class Win32HotkeyNative : IHotkeyNative
{
    private const uint WmHotkey = 0x0312;
    private const uint WmApp = 0x8000;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0;
    private const uint ModNoRepeat = 0x4000;

    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly TimeSpan _requestTimeout;
    private uint _threadId;
    private volatile bool _stopped;

    public Win32HotkeyNative()
        : this(DefaultRequestTimeout)
    {
    }

    /// <param name="requestTimeout">How long a caller waits for the hotkey thread before giving up (tests shorten it).</param>
    internal Win32HotkeyNative(TimeSpan requestTimeout)
    {
        _requestTimeout = requestTimeout;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Hindsound.Hotkeys" };
        _thread.Start();
        _ready.Wait();
    }

    public event Action<int>? HotkeyPressed;

    public bool Register(int id, uint modifiers, uint virtualKey) =>
        TryRegister(id, modifiers, virtualKey) == HotkeyRegistration.Registered;

    public HotkeyRegistration TryRegister(int id, uint modifiers, uint virtualKey) =>
        Invoke(() => RegisterHotKey(IntPtr.Zero, id, modifiers | ModNoRepeat, virtualKey), () => UnregisterHotKey(IntPtr.Zero, id));

    public void Unregister(int id) =>
        Invoke(() => UnregisterHotKey(IntPtr.Zero, id), undo: null, abandonable: false);

    public void Dispose()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(2000);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the hotkey thread and waits for it. Each request is claimed with an Interlocked
    /// by exactly one side. If the caller gives up first, a request not started yet never runs; one already running
    /// has its success undone with <paramref name="undo"/> when it ends, so a late registration cannot linger under
    /// an id the caller has forgotten. A request that must run regardless (a release) is not abandonable.
    /// </summary>
    internal HotkeyRegistration Invoke(Func<bool> work, Action? undo, bool abandonable = true)
    {
        if (_stopped)
        {
            return HotkeyRegistration.Refused;
        }

        var request = new Request(work, undo);
        _requests.Enqueue(request);
        if (!PostThreadMessage(_threadId, WmApp, UIntPtr.Zero, IntPtr.Zero))
        {
            Abandon(request);
            return HotkeyRegistration.Refused;
        }

        if (!request.Done.Wait(_requestTimeout))
        {
            // A release is left in the queue: it will still run, the caller just stops waiting.
            if (!abandonable || Abandon(request))
            {
                return HotkeyRegistration.TimedOut;
            }
        }

        return request.Result ? HotkeyRegistration.Registered : HotkeyRegistration.Refused;
    }

    /// <summary>The caller's claim. False if the request had already finished: its result stands.</summary>
    private static bool Abandon(Request request)
    {
        if (Interlocked.CompareExchange(ref request.State, RequestState.Abandoned, RequestState.Pending) == RequestState.Pending)
        {
            return true; // never started, never will
        }

        // Running: the worker sees Abandoned when it ends and undoes the result. Finished: too late to abandon.
        return Interlocked.CompareExchange(ref request.State, RequestState.Abandoned, RequestState.Running) == RequestState.Running;
    }

    private void Loop()
    {
        // Touching the queue creates it, so messages posted from now on are kept.
        PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
        _threadId = GetCurrentThreadId();
        _ready.Set();

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.Message == WmHotkey)
            {
                RaiseHotkeyPressed((int)message.WParam);
            }
            else if (message.Message == WmApp)
            {
                while (_requests.TryDequeue(out var request))
                {
                    Execute(request);
                }
            }
        }

        // Shortcuts die with the thread's queue anyway; release the waiting callers.
        while (_requests.TryDequeue(out var pending))
        {
            pending.Done.Set();
        }
    }

    private static void Execute(Request request)
    {
        // The caller may have abandoned it while it waited: then it must not run at all.
        if (Interlocked.CompareExchange(ref request.State, RequestState.Running, RequestState.Pending) != RequestState.Pending)
        {
            request.Done.Set();
            return;
        }

        try
        {
            request.Result = request.Work();
        }
        catch (Exception ex)
        {
            Trace.TraceError("Hindsound hotkey request failed: " + ex);
            request.Result = false;
        }
        finally
        {
            // Abandoned while running: the caller reported a timeout, so a success must not stay in force.
            if (Interlocked.CompareExchange(ref request.State, RequestState.Finished, RequestState.Running) == RequestState.Abandoned
                && request.Result)
            {
                try { request.Undo?.Invoke(); } catch (Exception) { /* nothing more to do */ }
                request.Result = false;
            }

            request.Done.Set();
        }
    }

    /// <summary>One failing subscriber must not stop the others, nor take the process down with the hotkey thread.</summary>
    private void RaiseHotkeyPressed(int id)
    {
        var handlers = HotkeyPressed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<int>>())
        {
            try
            {
                handler(id);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Hindsound hotkey handler failed: " + ex);
            }
        }
    }

    private static class RequestState
    {
        public const int Pending = 0;
        public const int Running = 1;
        public const int Finished = 2;
        public const int Abandoned = 3;
    }

    private sealed class Request(Func<bool> work, Action? undo)
    {
        public Func<bool> Work { get; } = work;

        public Action? Undo { get; } = undo;

        public int State;

        public bool Result { get; set; }

        public ManualResetEventSlim Done { get; } = new();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Msg msg, IntPtr hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
