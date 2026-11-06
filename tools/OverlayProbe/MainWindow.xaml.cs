using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static OverlayProbe.NativeMethods;

namespace OverlayProbe;

public partial class MainWindow : Window
{
    private const int HotkeyAuto = 1;
    private const int HotkeyForce = 2;

    private readonly ProbeLog _log = new();
    private readonly NotificationWindow _notification = new(softwareOnly: true);
    private readonly byte[] _confirmation = Sounds.ConfirmationWav();
    private readonly string _clipPath = Path.Combine(ProbeLog.Folder, "Hindsound_test_clip.mp3");
    private readonly DispatcherTimer _catchUp = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DateTime _catchUpUntil;
    private IntPtr _handle;
    private IntPtr _shownOver;
    private TimeSpan _cpuAtShow;

    private readonly bool _copyClip;

    public MainWindow(bool copyClip)
    {
        _copyClip = copyClip;
        InitializeComponent();
        LogPath.Text = "Log: " + ProbeLog.FilePath;
        _log.LineWritten += line =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        };
        _notification.Finished += OnNotificationFinished;
        _catchUp.Tick += OnCatchUpTick;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_handle)!.AddHook(WndProc);
        Register(HotkeyAuto, VkF10, "Ctrl+Shift+F10");
        Register(HotkeyForce, VkF11, "Ctrl+Shift+F11");
        PrepareClip();
        _log.Write($"ready, {Memory()}");
    }

    private void Register(int id, uint key, string name)
    {
        var ok = RegisterHotKey(_handle, id, ModControl | ModShift | ModNoRepeat, key);
        _log.Write(ok ? $"hotkey {name} registered" : $"hotkey {name} already in use by another app");
    }

    private void PrepareClip()
    {
        try
        {
            Sounds.WriteTestMp3(_clipPath);
            _log.Write($"test MP3 written with NAudio.Lame: {new FileInfo(_clipPath).Length / 1024} KB");
        }
        catch (Exception ex)
        {
            _log.Write($"NAudio.Lame FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            handled = true;
            OnHotkey(force: wParam.ToInt32() == HotkeyForce);
        }

        return IntPtr.Zero;
    }

    private void OnHotkey(bool force)
    {
        var state = QueryState();
        var foreground = GetForegroundWindow();
        GetWindowRect(foreground, out var window);
        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        GetDpiForMonitor(monitor, 0, out var dpi, out _);
        var coversScreen = window.Left <= info.Monitor.Left && window.Top <= info.Monitor.Top
            && window.Right >= info.Monitor.Right && window.Bottom >= info.Monitor.Bottom;

        var clip = CopyClip();
        _log.Write($"press ({(force ? "force" : "auto")}): \"{WindowTitle(foreground)}\" {window}, screen {info.Monitor}, "
            + $"covers screen: {coversScreen}, state: {state}, dpi: {dpi}, clipboard: {clip}");

        if (state == UserNotificationState.RunningD3DFullScreen && !force)
        {
            _log.Write("exclusive fullscreen likely: sound only, notification held back for the desktop");
            _ = Task.Run(() => Sounds.Play(_confirmation));
            _catchUpUntil = DateTime.Now.AddSeconds(30);
            _catchUp.Start();
            return;
        }

        var clickThrough = coversScreen || state != UserNotificationState.AcceptsNotifications;
        Show(foreground, coversScreen ? info.Monitor : info.Work, dpi, clickThrough);
    }

    private void Show(IntPtr foreground, NativeMethods.Rect area, uint dpi, bool clickThrough)
    {
        _shownOver = foreground;
        _cpuAtShow = Process.GetCurrentProcess().TotalProcessorTime;
        _notification.Present(area, dpi, clickThrough);
        _log.Write($"notification shown, click-through: {clickThrough}");

        var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        check.Tick += (_, _) =>
        {
            check.Stop();
            CheckFocus("400 ms after showing");
        };
        check.Start();
    }

    private void CheckFocus(string when)
    {
        var now = GetForegroundWindow();
        _log.Write($"focus {when}: {(now == _shownOver ? "kept" : $"LOST, now on \"{WindowTitle(now)}\"")}");
    }

    private void OnNotificationFinished()
    {
        var cpu = Process.GetCurrentProcess().TotalProcessorTime - _cpuAtShow;
        CheckFocus("after hiding");
        _log.Write($"notification gone, {cpu.TotalMilliseconds:F0} ms of CPU for its 2.92 s, {Memory()}");
    }

    private void OnCatchUpTick(object? sender, EventArgs e)
    {
        if (DateTime.Now > _catchUpUntil)
        {
            _catchUp.Stop();
            _log.Write("catch-up gave up after 30 s");
            return;
        }

        if (QueryState() != UserNotificationState.AcceptsNotifications)
        {
            return;
        }

        _catchUp.Stop();
        var foreground = GetForegroundWindow();
        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        GetDpiForMonitor(monitor, 0, out var dpi, out _);
        _log.Write("back on the desktop: showing the held notification");
        Show(foreground, info.Work, dpi, clickThrough: false);
    }

    private string CopyClip()
    {
        if (!_copyClip)
        {
            return "skipped";
        }

        if (!File.Exists(_clipPath))
        {
            return "no file";
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Clipboard.SetFileDropList(new StringCollection { _clipPath });
                return attempt == 1 ? "copied" : $"copied on try {attempt}";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(50);
            }
        }

        return "FAILED";
    }

    private static string Memory()
    {
        var process = Process.GetCurrentProcess();
        return $"RAM {process.WorkingSet64 / 1048576} MB in use, {process.PrivateMemorySize64 / 1048576} MB private";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        UnregisterHotKey(_handle, HotkeyAuto);
        UnregisterHotKey(_handle, HotkeyForce);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _notification.Close();
        base.OnClosed(e);
    }
}
