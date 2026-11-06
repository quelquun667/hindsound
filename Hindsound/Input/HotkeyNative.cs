using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Hindsound.Input;

/// <summary>The Win32 side of the shortcuts, so tests can replace it.</summary>
public interface IHotkeyNative : IDisposable
{
    /// <summary>Raised with the id of a pressed shortcut, on the native layer's own thread.</summary>
    event Action<int>? HotkeyPressed;

    bool Register(int id, uint modifiers, uint virtualKey);

    void Unregister(int id);
}

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

    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private volatile bool _stopped;

    public Win32HotkeyNative()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Hindsound.Hotkeys" };
        _thread.Start();
        _ready.Wait();
    }

    public event Action<int>? HotkeyPressed;

    public bool Register(int id, uint modifiers, uint virtualKey) =>
        Invoke(() => RegisterHotKey(IntPtr.Zero, id, modifiers | ModNoRepeat, virtualKey));

    public void Unregister(int id) => Invoke(() => UnregisterHotKey(IntPtr.Zero, id));

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

    private bool Invoke(Func<bool> work)
    {
        if (_stopped)
        {
            return false;
        }

        var request = new Request(work);
        _requests.Enqueue(request);
        if (!PostThreadMessage(_threadId, WmApp, UIntPtr.Zero, IntPtr.Zero))
        {
            return false;
        }

        return request.Done.Wait(5000) && request.Result;
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
                HotkeyPressed?.Invoke((int)message.WParam);
            }
            else if (message.Message == WmApp)
            {
                while (_requests.TryDequeue(out var request))
                {
                    try
                    {
                        request.Result = request.Work();
                    }
                    finally
                    {
                        request.Done.Set();
                    }
                }
            }
        }

        // Shortcuts die with the thread's queue anyway; release the waiting callers.
        while (_requests.TryDequeue(out var pending))
        {
            pending.Done.Set();
        }
    }

    private sealed class Request(Func<bool> work)
    {
        public Func<bool> Work { get; } = work;

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
