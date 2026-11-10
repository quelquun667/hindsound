using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Hindsound.Input;

namespace Hindsound.Linux.Input;

/// <summary>
/// The global shortcuts of an X11 session: XGrabKey on the root window, one combination at a time, under the variants of
/// Caps Lock and Num Lock. Every Xlib call runs on the shortcuts' own thread; a call from the app waits for that thread
/// and gives up after <see cref="AnswerTimeout"/>, leaving nothing grabbed. <see cref="KeyByKeyHotkeys"/> makes the batch.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class X11Hotkeys : IHotkeyRegistrar
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(2);

    private readonly Action<string> _log;
    private readonly ConcurrentQueue<Job> _jobs = new();
    private readonly Dictionary<int, Grab> _grabs = [];
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private nint _display;
    private nuint _root;
    private uint _numLock;
    private int _wakeRead = -1;
    private int _wakeWrite = -1;
    private volatile bool _stopping;

    private X11Hotkeys(Action<string> log)
    {
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "hindsound-x11-keys" };
    }

    public event Action<int>? HotkeyPressed;

    /// <summary>Opens the X display on the shortcuts' thread; null when there is none to open.</summary>
    public static X11Hotkeys? TryOpen(Action<string> log)
    {
        // A missing libX11 fails on the thread, which logs it and ends.
        var keys = new X11Hotkeys(log);
        keys._thread.Start();
        if (keys._ready.Wait(AnswerTimeout) && keys._display != 0)
        {
            log($"hotkeys: X11, Num Lock mask 0x{keys._numLock:X}");
            return keys;
        }

        log("hotkeys: the X display could not be opened");
        keys.Dispose();
        return null;
    }

    public bool Register(int id, uint modifiers, uint virtualKey) => TryRegister(id, modifiers, virtualKey) == HotkeyRegistration.Registered;

    public HotkeyRegistration TryRegister(int id, uint modifiers, uint virtualKey)
    {
        if (HotkeyKeysyms.ToKeysym(virtualKey) is not { } keysym)
        {
            return HotkeyRegistration.Refused;
        }

        var mask = HotkeyKeysyms.ToX11Modifiers(modifiers);
        var job = new Job(() => GrabOnThread(id, keysym, mask), () => UngrabOnThread(id));
        return Ask(job) switch
        {
            true => HotkeyRegistration.Registered,
            false => HotkeyRegistration.Refused,
            null => HotkeyRegistration.TimedOut,
        };
    }

    public void Unregister(int id) => Ask(new Job(() => UngrabOnThread(id), null));

    public void Dispose()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        Wake();
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
        {
            _thread.Join(AnswerTimeout);
        }
    }

    // Hands a job to the thread and waits for it. Null past the timeout: a job not started never runs, and one that was
    // running is undone when it ends.
    private bool? Ask(Job job)
    {
        if (_stopping || _display == 0)
        {
            return null;
        }

        _jobs.Enqueue(job);
        Wake();
        if (job.Done.Wait(AnswerTimeout))
        {
            return job.Result;
        }

        if (Interlocked.CompareExchange(ref job.State, Job.Abandoned, Job.Queued) == Job.Queued
            || Interlocked.CompareExchange(ref job.State, Job.Abandoned, Job.Running) == Job.Running)
        {
            return null;
        }

        return job.Result;
    }

    private unsafe void Wake()
    {
        if (_wakeWrite < 0)
        {
            return;
        }

        byte one = 1;
        X11Native.write(_wakeWrite, &one, 1);
    }

    private unsafe void Run()
    {
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(X11Native.EventSize);
        try
        {
            if (!Open())
            {
                return;
            }

            var fds = stackalloc X11Native.PollFd[2];
            var drain = stackalloc byte[64];
            while (!_stopping)
            {
                // Xlib may hold events it read already; those never wake the poll.
                while (X11Native.XPending(_display) > 0)
                {
                    X11Native.XNextEvent(_display, buffer);
                    OnEvent(buffer);
                }

                RunJobs();
                if (_stopping)
                {
                    break;
                }

                fds[0] = new X11Native.PollFd { Fd = X11Native.XConnectionNumber(_display), Events = X11Native.PollIn };
                fds[1] = new X11Native.PollFd { Fd = _wakeRead, Events = X11Native.PollIn };
                if (X11Native.poll(fds, 2, -1) < 0 && System.Runtime.InteropServices.Marshal.GetLastPInvokeError() != EIntr)
                {
                    _log("hotkeys: poll failed, the X11 shortcuts stop");
                    break;
                }

                if ((fds[1].Revents & X11Native.PollIn) != 0)
                {
                    X11Native.read(_wakeRead, drain, 64);
                }
            }
        }
        catch (Exception ex)
        {
            _log("hotkeys: the X11 thread failed: " + ex.GetType().Name);
        }
        finally
        {
            Close();
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            _ready.Set();
            while (_jobs.TryDequeue(out var job))
            {
                job.Done.Set();
            }
        }
    }

    private const int EIntr = 4;

    private unsafe bool Open()
    {
        try
        {
            if (!X11Native.WakePipe(out _wakeRead, out _wakeWrite))
            {
                return false;
            }

            X11Native.KeepErrors();
            X11Native.SurviveConnectionLoss(OnConnectionLost);
            _display = X11Native.XOpenDisplay(0);
            if (_display == 0)
            {
                return false;
            }

            _root = X11Native.XDefaultRootWindow(_display);
            _numLock = X11Native.NumLockMask(_display, X11Native.XKeysymToKeycode(_display, HotkeyKeysyms.KeysymNumLock));
            return true;
        }
        finally
        {
            _ready.Set();
        }
    }

    // On the thread of the shortcuts, from Xlib, which never gets it back: the display is gone, and with it every grab.
    // The app goes on without its X11 shortcuts; a call from it now answers at once instead of waiting for this thread.
    private void OnConnectionLost()
    {
        _stopping = true;
        _log("hotkeys: the X connection was lost, the X11 shortcuts stop");
        while (_jobs.TryDequeue(out var job))
        {
            job.Done.Set();
        }
    }

    private void Close()
    {
        if (_display != 0)
        {
            foreach (var id in _grabs.Keys.ToList())
            {
                UngrabOnThread(id);
            }

            X11Native.XCloseDisplay(_display);
        }

        if (_wakeRead >= 0)
        {
            X11Native.close(_wakeRead);
        }

        var wake = _wakeWrite;
        _wakeWrite = -1;
        if (wake >= 0)
        {
            X11Native.close(wake);
        }
    }

    private void RunJobs()
    {
        while (_jobs.TryDequeue(out var job))
        {
            if (Interlocked.CompareExchange(ref job.State, Job.Running, Job.Queued) != Job.Queued)
            {
                continue;
            }

            job.Result = job.Work();
            if (Interlocked.CompareExchange(ref job.State, Job.Finished, Job.Running) != Job.Running && job.Result)
            {
                // The caller gave up meanwhile: what the job did must not stay.
                job.Undo?.Invoke();
            }

            job.Done.Set();
        }
    }

    private unsafe void OnEvent(nint buffer)
    {
        if (*(int*)buffer != X11Native.KeyPress)
        {
            return;
        }

        var state = *(uint*)(buffer + X11Native.KeyStateOffset);
        var keycode = *(uint*)(buffer + X11Native.KeyCodeOffset);
        foreach (var (id, grab) in _grabs)
        {
            if (grab.Keycode == keycode && HotkeyKeysyms.StateMatches(state, grab.Modifiers, _numLock))
            {
                HotkeyPressed?.Invoke(id);
                return;
            }
        }
    }

    private bool GrabOnThread(int id, uint keysym, uint modifiers)
    {
        var keycode = X11Native.XKeysymToKeycode(_display, keysym);
        if (keycode == 0)
        {
            // No key of this keyboard gives that keysym.
            return false;
        }

        X11Native.XSync(_display, 0);
        X11Native.TakeError();
        foreach (var variant in HotkeyKeysyms.LockVariants(_numLock))
        {
            X11Native.XGrabKey(_display, keycode, modifiers | variant, _root, 0, X11Native.GrabModeAsync, X11Native.GrabModeAsync);
        }

        // The refusal of a grab comes back as an error, once the server has seen the requests.
        X11Native.XSync(_display, 0);
        if (X11Native.TakeError() != 0)
        {
            Ungrab(keycode, modifiers);
            return false;
        }

        _grabs[id] = new Grab(keycode, modifiers);
        return true;
    }

    private bool UngrabOnThread(int id)
    {
        if (!_grabs.Remove(id, out var grab))
        {
            return false;
        }

        Ungrab(grab.Keycode, grab.Modifiers);
        return true;
    }

    private void Ungrab(byte keycode, uint modifiers)
    {
        foreach (var variant in HotkeyKeysyms.LockVariants(_numLock))
        {
            X11Native.XUngrabKey(_display, keycode, modifiers | variant, _root);
        }

        X11Native.XSync(_display, 0);
        X11Native.TakeError();
    }

    private readonly record struct Grab(byte Keycode, uint Modifiers);

    private sealed class Job(Func<bool> work, Func<bool>? undo)
    {
        public const int Queued = 0;
        public const int Running = 1;
        public const int Finished = 2;
        public const int Abandoned = 3;

        public int State = Queued;

        public bool Result;

        public Func<bool> Work { get; } = work;

        public Func<bool>? Undo { get; } = undo;

        public ManualResetEventSlim Done { get; } = new();
    }
}
