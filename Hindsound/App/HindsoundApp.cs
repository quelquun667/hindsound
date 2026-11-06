using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;

namespace Hindsound.App;

/// <summary>
/// The wiring: the message loop, the tray icon, the shortcuts, the engine and the clip path. Everything here runs on the
/// thread of the message loop, unless it says otherwise, and every other thread comes back through <see cref="UiLoop.Post"/>.
/// </summary>
internal sealed class HindsoundApp : IClipBackend
{
    private const int CatchUpPollMs = 500;
    private const int CatchUpTries = 60; // 30 s
    private const int SelfTestStartDelayMs = 3000;
    private const int SelfTestExitDelayMs = 3300;
    private static readonly TimeSpan QueuedClipsLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EngineStopLimit = TimeSpan.FromSeconds(5);

    private readonly AppOptions _options;
    private readonly Journal _journal;
    private readonly SettingsStore _store;
    private readonly ClipSaver _saver = new();

    private UiLoop _loop = null!;
    private TrayIcon _tray = null!;
    private Notification _notification = null!;
    private ClipPipeline _pipeline = null!;
    private HotkeyManager? _hotkeys;
    private volatile AudioEngine? _engine;

    private AppSettings _settings = new();
    private DateTime _settingsWritten;
    private AudioEngineStatus? _lastStatus;
    private TrayState _trayState = TrayState.Active;
    private string _trayTip = "Hindsound";
    private bool _quitting;

    private ClipResult? _lastClip;
    private TimeSpan _lastRequested;
    private int _catchUpGeneration;

    private double _cpuBeforeNotification;
    private int _exitCode;

    public HindsoundApp(AppOptions options, Journal journal)
    {
        _options = options;
        _journal = journal;
        _store = new SettingsStore(options.SettingsPath);
    }

    public int Run()
    {
        UiErrors.Handler = ex => _journal.Write("error: " + ex);
        LoadSettings();
        _journal.Write($"started, settings: {_store.Path} ({_store.LastStatus}), {Journal.Memory()}");

        _loop = new UiLoop();
        _notification = new Notification { Clicked = OpenLastClipFolder, Finished = OnNotificationFinished };
        _pipeline = new ClipPipeline(this, _loop.Post, _loop.After, ex => _journal.Write("clip error: " + ex))
        {
            SavingSlowly = OnSavingSlowly,
            Finished = OnClipFinished,
        };

        _tray = new TrayIcon(_loop.Hwnd, UiLoop.TrayCallbackMessage);
        _loop.TrayMessage = OnTrayMessage;
        _loop.TaskbarCreated = () => _tray.Add(_trayState, Accent, _trayTip);
        _loop.SessionEnding = Shutdown;
        if (!_tray.Add(_trayState, Accent, _trayTip))
        {
            _journal.Write("the tray icon could not be added");
        }

        ApplyStartupRegistration();
        StartHotkeys();
        StartEngine();

        if (_options.ExitAfterSeconds is { } seconds)
        {
            _loop.After((int)Math.Min(seconds * 1000, int.MaxValue), _loop.Quit);
        }

        _loop.Run();
        Shutdown();
        return _exitCode;
    }

    private uint Accent
    {
        get
        {
            AccentColors.TryParseHex(_settings.General.AccentColor, out var r, out var g, out var b);
            return ((uint)r << 16) | ((uint)g << 8) | b;
        }
    }

    // ---------------------------------------------------------------- settings

    private void LoadSettings()
    {
        _settings = _store.Load();
        if (_store.LastStatus == SettingsLoadStatus.Missing)
        {
            TrySaveSettings();
        }

        _settingsWritten = SettingsFileTime();
    }

    private void TrySaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _journal.Write("settings not written: " + ex.Message);
        }
    }

    private DateTime SettingsFileTime()
    {
        try
        {
            return File.GetLastWriteTimeUtc(_store.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return default;
        }
    }

    /// <summary>Until the settings window exists, the file is edited by hand: a change counts at the next press or menu.</summary>
    private void ReloadSettingsIfChanged()
    {
        var written = SettingsFileTime();
        if (written == _settingsWritten)
        {
            return;
        }

        _settingsWritten = written;
        var previous = SettingsMapping.ToEngineOptions(_settings);
        _settings = _store.Load();
        _journal.Write($"settings read again ({_store.LastStatus})");
        ApplyStartupRegistration();
        ApplyHotkeys();
        var next = SettingsMapping.ToEngineOptions(_settings);
        if (_engine is { } engine)
        {
            if (previous with { BufferDuration = next.BufferDuration } != next)
            {
                Watch(engine.SetDevicesAsync(next.SystemDeviceId, next.MicDeviceId, next.MicEnabled), "changing the devices");
            }

            if (previous.BufferDuration != next.BufferDuration)
            {
                Watch(engine.ResizeBufferAsync(next.BufferDuration), "resizing the buffer");
            }
        }

        _tray.Update(_trayState, Accent, _trayTip);
    }

    // A test run with its own settings file never touches the registry; and a setting that is off and was never on
    // needs no write either.
    private void ApplyStartupRegistration()
    {
        if (_options.SettingsPath is not null)
        {
            return;
        }

        try
        {
            var key = new RegistryRunKey();
            if (_settings.General.StartWithWindows || key.GetValue(StartupRegistration.DefaultValueName) is not null)
            {
                new StartupRegistration(key).Apply(_settings.General.StartWithWindows);
            }
        }
        catch (Exception ex)
        {
            _journal.Write("startup registration: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- shortcuts

    private void StartHotkeys()
    {
        try
        {
            _hotkeys = new HotkeyManager();

            // The hotkey thread only asks for a turn on the loop.
            _hotkeys.Pressed += action => _loop.Post(() => OnHotkey(action));
            ApplyHotkeys();
        }
        catch (Exception ex)
        {
            _journal.Write("hotkeys: " + ex);
        }
    }

    private void ApplyHotkeys()
    {
        if (_hotkeys is null)
        {
            return;
        }

        var problem = string.Empty;
        foreach (var result in _hotkeys.Apply(_settings.Hotkeys))
        {
            if (result.Status != HotkeyStatus.NotSet)
            {
                _journal.Write($"hotkey {result.Action} {result.Gesture}: {result.Status}");
            }

            if (!result.Ok)
            {
                problem = $"{result.Gesture}: {result.Message}";
            }
        }

        SetTray(_trayState, problem.Length > 0 ? "Hindsound - " + problem : "Hindsound");
    }

    private void OnHotkey(HotkeyAction action)
    {
        ReloadSettingsIfChanged();
        switch (action)
        {
            case HotkeyAction.SaveDefault:
                SaveClip(TimeSpan.FromSeconds(_settings.ClipSeconds));
                break;
            case HotkeyAction.Save15s:
                SaveClip(TimeSpan.FromSeconds(15));
                break;
            case HotkeyAction.Save1m:
                SaveClip(TimeSpan.FromMinutes(1));
                break;
            case HotkeyAction.Save5m:
                SaveClip(TimeSpan.FromMinutes(5));
                break;
            case HotkeyAction.TogglePause:
                TogglePause();
                break;
            case HotkeyAction.CopyLast:
                CopyLastClip();
                break;
        }
    }

    // ---------------------------------------------------------------- engine

    private void StartEngine()
    {
        var options = SettingsMapping.ToEngineOptions(_settings);
        _journal.Write($"engine: buffer {options.BufferDuration}, system {options.SystemDeviceId ?? "(default)"}, mic enabled {options.MicEnabled}, mic {options.MicDeviceId ?? "(default)"}");

        // Off this thread: the loop is a single-threaded apartment, and the audio objects belong in the thread pool's.
        _ = Task.Run(async () =>
        {
            try
            {
                var engine = new AudioEngine(options);
                engine.StatusChanged += status => _loop.Post(() => OnStatus(status));
                _engine = engine;
                await engine.StartAsync().ConfigureAwait(false);
                var status = engine.Status;
                _loop.Post(() => OnEngineStarted(status));
            }
            catch (Exception ex)
            {
                _journal.Write("engine start: " + ex);
                _loop.Post(() => ShowStatus(NotificationStates.NoDevice(), important: true));
            }
        });
    }

    private void OnEngineStarted(AudioEngineStatus status)
    {
        OnStatus(status);
        _journal.Write($"capture started: {status.Activity}, system {status.SystemDeviceName ?? "(none)"}, mic capturing {status.MicCapturing}, own sounds excluded {status.OwnSoundsExcluded}, {Journal.Memory()}");
        if (_options.SelfTestSave)
        {
            _loop.After(SelfTestStartDelayMs, RunSelfTestPress);
        }
    }

    private void OnStatus(AudioEngineStatus status)
    {
        var notice = StatusTransitions.Decide(_lastStatus, status);
        _lastStatus = status;
        SetTray(StatusTransitions.TrayStateOf(status.Activity), _trayTip);
        switch (notice)
        {
            case StatusNotice.CaptureOn:
                ShowStatus(NotificationStates.CaptureOn(_settings.BufferSeconds, _settings.Hotkeys.SaveDefault), important: false);
                break;
            case StatusNotice.Paused:
                ShowStatus(NotificationStates.Paused(), important: false);
                break;
            case StatusNotice.NoDevice:
                ShowStatus(NotificationStates.NoDevice(), important: true);
                break;
            case StatusNotice.DeviceChanged:
                ShowStatus(NotificationStates.DeviceChanged(status.SystemDeviceName ?? "the default output"), important: false);
                break;
        }
    }

    private void TogglePause()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        Watch(_lastStatus?.Activity == CaptureActivity.Paused ? engine.ResumeAsync() : engine.PauseAsync(), "pause or resume");
    }

    private void Watch(Task task, string what) =>
        _ = task.ContinueWith(t => _journal.Write($"{what}: {t.Exception}"), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    // ---------------------------------------------------------------- the clip path

    private void SaveClip(TimeSpan requested)
    {
        var foreground = ForegroundWindow.Read();
        _pipeline.Press(new ClipRequest(requested, SettingsMapping.ToExportOptions(_settings), foreground.Title, _settings.Export.CopyToClipboard));
    }

    AudioSnapshot IClipBackend.Snapshot(TimeSpan duration) =>
        _engine?.Snapshot(duration) ?? new AudioSnapshot([], []);

    ClipResult IClipBackend.Save(AudioSnapshot snapshot, ExportOptions options, string? appName) =>
        _saver.Save(snapshot, options, appName);

    private void OnSavingSlowly(ClipRequest request) =>
        ShowStatus(NotificationStates.Saving(request.Requested), important: false);

    private void OnClipFinished(ClipOutcome outcome)
    {
        if (outcome.Result is not { } result)
        {
            var content = outcome.Failure == ClipFailure.NothingCaptured ? NotificationStates.NothingToSave() : NotificationStates.SaveFailed();
            _journal.Write($"clip not saved: {outcome.Failure}");
            ShowClip(content, saved: false, important: outcome.Failure == ClipFailure.Failed);
            FinishSelfTest(outcome, false);
            return;
        }

        _lastClip = result;
        _lastRequested = outcome.Request.Requested;
        _journal.Write($"clip saved: {string.Join(", ", result.Paths)}, {result.Duration.TotalSeconds:F2} s of {outcome.Request.Requested.TotalSeconds:F0} s, {result.SizeBytes} bytes, silent {result.IsSilent}, rescued {result.Rescued}, fell back to WAV {result.FellBackToWav}, {Journal.Memory()}");
        if (outcome.Request.CopyWanted)
        {
            CopyClip(result, copied => FinishClip(outcome, result, copied));
        }
        else
        {
            FinishClip(outcome, result, false);
        }
    }

    private void CopyClip(ClipResult result, Action<bool> done) =>
        ClipboardFiles.CopyWithRetries(() => ClipboardFiles.TrySet(_loop.Hwnd, result.Paths), _loop.After, done);

    private void FinishClip(ClipOutcome outcome, ClipResult result, bool copied)
    {
        var content = NotificationStates.ForClip(result, outcome.Request.Requested, outcome.Request.CopyWanted, copied, Accent);
        _journal.Write($"clipboard: wanted {outcome.Request.CopyWanted}, copied {copied}; notification {content.Kind}");
        ShowClip(content, saved: true, important: result.Rescued);
        FinishSelfTest(outcome, copied);
    }

    private void CopyLastClip()
    {
        if (_lastClip is not { } clip)
        {
            ShowStatus(NotificationStates.NothingToSave(), important: false);
            return;
        }

        CopyClip(clip, copied => ShowStatus(NotificationStates.ForClip(clip, _lastRequested, true, copied, Accent), important: false));
    }

    // ---------------------------------------------------------------- showing

    private void ShowClip(NotificationContent content, bool saved, bool important)
    {
        var (state, covers) = Surface();
        var decision = FullscreenPolicy.ForClip(state, covers, _settings.General, saved, important);
        _journal.Write($"presentation: state {state}, covers screen {covers}: {decision}");
        if (decision.PlaySound)
        {
            ConfirmSound.Play();
        }

        if (decision.ShowOverlay)
        {
            Present(content, decision.ClickThrough);
        }

        if (decision.CatchUpLater)
        {
            WaitForDesktop(content);
        }
    }

    private void ShowStatus(NotificationContent content, bool important)
    {
        var (state, covers) = Surface();
        var decision = FullscreenPolicy.ForStatus(state, covers, _settings.General, important);
        if (decision.ShowOverlay)
        {
            Present(content, decision.ClickThrough);
        }
    }

    private static unsafe (UserNotificationState State, bool Covers) Surface()
    {
        int raw;
        var state = Native.SHQueryUserNotificationState(&raw) == 0 ? (UserNotificationState)raw : UserNotificationState.Unknown;
        return (state, ForegroundWindow.Read().CoversMonitor);
    }

    private void Present(NotificationContent content, bool clickThrough)
    {
        Native.RECT area;
        uint dpi;
        if (_options.Offscreen)
        {
            var right = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN) - 4000;
            var top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN) - 4000;
            area = new Native.RECT { Left = right - 1920, Top = top, Right = right, Bottom = top + 1080 };
            dpi = 96;
            clickThrough = true;
        }
        else
        {
            (area, dpi) = ForegroundWindow.ActiveMonitor();
        }

        _cpuBeforeNotification = Journal.CpuMilliseconds();
        var leftEdge = string.Equals(_settings.General.NotificationPosition, "left", StringComparison.OrdinalIgnoreCase);
        _notification.Present(content, new NotificationPlacement(area, dpi, clickThrough, leftEdge), Accent);
    }

    private void OnNotificationFinished()
    {
        if (_options.SelfTestSave)
        {
            _journal.Write($"notification left the screen: {Journal.CpuMilliseconds() - _cpuBeforeNotification:F0} ms of process CPU while it was up (capture included), {Journal.Memory()}");
        }
    }

    /// <summary>The check only runs here, every 500 ms for 30 s at most. A newer clip replaces the one waiting.</summary>
    private void WaitForDesktop(NotificationContent content)
    {
        var generation = ++_catchUpGeneration;
        var tries = 0;
        _loop.After(CatchUpPollMs, Check);

        void Check()
        {
            if (generation != _catchUpGeneration)
            {
                return;
            }

            var (state, covers) = Surface();
            if (FullscreenPolicy.BackOnDesktop(state, covers))
            {
                Present(content, clickThrough: false);
            }
            else if (++tries < CatchUpTries)
            {
                _loop.After(CatchUpPollMs, Check);
            }
        }
    }

    // ---------------------------------------------------------------- tray

    private void SetTray(TrayState state, string tip)
    {
        if (state != _trayState || tip != _trayTip)
        {
            _trayState = state;
            _trayTip = tip;
            _tray.Update(state, Accent, tip);
        }
    }

    private void OnTrayMessage(uint code, nuint wParam)
    {
        if (code != Native.WM_CONTEXTMENU)
        {
            return;
        }

        ReloadSettingsIfChanged();
        var paused = _lastStatus?.Activity == CaptureActivity.Paused;
        var command = _tray.ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF), paused, _lastClip is not null);
        switch (command)
        {
            case TrayCommand.Save30s:
                SaveClip(TimeSpan.FromSeconds(30));
                break;
            case TrayCommand.Save15s:
                SaveClip(TimeSpan.FromSeconds(15));
                break;
            case TrayCommand.Save1m:
                SaveClip(TimeSpan.FromMinutes(1));
                break;
            case TrayCommand.Save5m:
                SaveClip(TimeSpan.FromMinutes(5));
                break;
            case TrayCommand.CopyLast:
                CopyLastClip();
                break;
            case TrayCommand.TogglePause:
                TogglePause();
                break;
            case TrayCommand.OpenFolder:
                OpenClipsFolder();
                break;
            case TrayCommand.Settings:
                OpenSettingsFile();
                break;
            case TrayCommand.Quit:
                _loop.Quit();
                break;
        }
    }

    private void OpenClipsFolder()
    {
        var folder = SettingsMapping.ToExportOptions(_settings).OutputFolder;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _journal.Write("clips folder: " + ex.Message);
        }

        OpenWithShell(folder);
    }

    private void OpenLastClipFolder()
    {
        var folder = _lastClip is { } clip ? Path.GetDirectoryName(clip.PrimaryPath) : null;
        if (folder is null)
        {
            OpenClipsFolder();
        }
        else
        {
            OpenWithShell(folder);
        }
    }

    /// <summary>For now the settings are the file itself: the settings window comes later.</summary>
    private void OpenSettingsFile()
    {
        if (!File.Exists(_store.Path))
        {
            TrySaveSettings();
        }

        if (!OpenWithShell(_store.Path))
        {
            Native.ShellExecuteW(0, null, "notepad.exe", "\"" + _store.Path + "\"", null, Native.SW_SHOWNORMAL);
        }
    }

    private bool OpenWithShell(string target)
    {
        const int largestShellError = 32;
        var result = Native.ShellExecuteW(0, null, target, null, null, Native.SW_SHOWNORMAL);
        if (result > largestShellError)
        {
            return true;
        }

        _journal.Write($"could not open {target}: shell error {result}");
        return false;
    }

    // ---------------------------------------------------------------- self-test

    // The same way in as a key press: from another thread, through the loop's queue.
    private void RunSelfTestPress()
    {
        _journal.Write("selftest: press, " + Journal.Memory());
        _ = Task.Run(() => _loop.Post(() => OnHotkey(HotkeyAction.SaveDefault)));
    }

    private void FinishSelfTest(ClipOutcome outcome, bool copied)
    {
        if (!_options.SelfTestSave)
        {
            return;
        }

        var ok = outcome.Result is { } result && !result.Rescued && (!outcome.Request.CopyWanted || copied);
        _exitCode = ok ? 0 : 1;
        _journal.Write($"selftest: {(ok ? "PASS" : "FAIL")}, format asked {outcome.Request.Options.Format}, {Journal.Memory()}");

        // The notification goes through its whole life before leaving.
        _loop.After(SelfTestExitDelayMs, _loop.Quit);
    }

    // ---------------------------------------------------------------- leaving

    /// <summary>Icon first, so the user sees the app gone; then the shortcuts; queued clips get written; the engine last.</summary>
    private void Shutdown()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        Guard(() => _tray.Dispose(), "tray");
        Guard(() => _notification.Dispose(), "notification");
        Guard(() => _hotkeys?.Dispose(), "hotkeys");
        Guard(() =>
        {
            if (!_pipeline.WaitIdle(QueuedClipsLimit))
            {
                _journal.Write("leaving with clips still being written");
            }
        }, "queued clips");
        Guard(() =>
        {
            var stopping = Task.Run(() => _engine?.Dispose());
            if (!stopping.Wait(EngineStopLimit))
            {
                _journal.Write("the engine did not stop in time");
            }
        }, "engine");
        Guard(() => _loop.Dispose(), "window");
        _journal.Write("left, " + Journal.Memory());
    }

    private void Guard(Action step, string what)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            _journal.Write($"leaving, {what}: {ex}");
        }
    }
}
