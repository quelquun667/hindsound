using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.App;

/// <summary>
/// The wiring: the message loop, the tray icon, the shortcuts, the engine and the clip path. Everything here runs on the
/// thread of the message loop, unless it says otherwise, and every other thread comes back through <see cref="UiLoop.Post"/>.
/// </summary>
internal sealed class HindsoundApp : IClipBackend, ISettingsBackend
{
    private const int CatchUpPollMs = 500;
    private const int CatchUpTries = 60; // 30 s
    private const int SnapshotReleaseDelayMs = 500;
    private const int SelfTestExitDelayMs = 3300;
    private const int ProbeStepMs = 300;
    private const int ProbeAfterCloseMs = 10_000;
    private const int StartupReadTries = 3;
    private const int StartupReadGapMs = 200;
    private static readonly TimeSpan MeterWindow = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan QueuedClipsLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EngineStopLimit = TimeSpan.FromSeconds(5);

    private readonly AppOptions _options;
    private readonly Journal _journal;
    private readonly SettingsStore _store;
    private readonly ClipSaver _saver;

    private UiLoop _loop = null!;
    private TrayIcon _tray = null!;
    private Notification _notification = null!;
    private ClipPipeline _pipeline = null!;
    private HotkeyManager? _hotkeys;
    private volatile AudioEngine? _engine;

    private AppSettings _settings = new();
    private readonly SettingsFileState _settingsFile = new();
    private readonly NoticeQueue _notices = new();
    private bool _canShowNotices;
    private bool _noticeRetrying;
    private int _noticeRetries;
    private bool _startTouchesRunKey;
    private AudioEngineOptions? _engineOptions;
    private SettingsWindow? _window;
    private bool _firstRun;
    private IReadOnlyList<HotkeyResult> _hotkeyResults = [];
    private AudioEngineStatus? _lastStatus;
    private TrayState _trayState = TrayState.Active;
    private string _trayTip = "Hindsound";
    private bool _quitting;

    private ClipResult? _lastClip;
    private DateTime _lastClipTime;
    private TimeSpan _lastRequested;
    private int _catchUpGeneration;

    private double _cpuBeforeNotification;
    private int _exitCode;

    public HindsoundApp(AppOptions options, Journal journal)
    {
        _options = options;
        _journal = journal;
        _store = new SettingsStore(options.SettingsPath);
        _saver = new ClipSaver(openMp3: OpenMp3);
    }

    // The saver falls back to WAV without saying why: the reason is kept here.
    private IAudioFileWriter OpenMp3(string path, int channels, int bitrateKbps)
    {
        try
        {
            return Encoders.OpenMp3(path, channels, bitrateKbps);
        }
        catch (Exception ex)
        {
            _journal.Write("the MP3 encoder failed: " + ex);
            throw;
        }
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

        _canShowNotices = true;
        ShowNextNotice();

        if (_startTouchesRunKey)
        {
            ApplyStartupRegistration();
        }

        StartHotkeys();
        _journal.Write($"ready, before the capture: {Journal.Memory()}");
        StartEngine();

        // Only when there was no settings file: the file now exists, so this happens once.
        if ((_firstRun || _options.ForceFirstRun) && !_options.SelfTestSave)
        {
            OpenSettings(firstRun: true);
        }

        if (_options.SettingsProbe)
        {
            _loop.After(ProbeStepMs, RunSettingsProbe);
        }

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
        _settings = _store.LoadPatiently(StartupReadTries, () => Thread.Sleep(StartupReadGapMs));
        var start = _settingsFile.Started(_store.LastStatus, _store.LastBackupPath);
        _firstRun = start.FirstRun;
        _startTouchesRunKey = start.TouchRunKey;
        if (start.MakeSafe)
        {
            SettingsFileState.MakeSafe(_settings, ReadRunKey());
            _journal.Write($"settings not usable ({_store.LastStatus}): defaults with the mic off, kept as {_store.LastBackupPath ?? "(not moved)"}");
        }

        if (start.Write)
        {
            TrySaveSettings();
        }

        _settingsFile.Note(SettingsFileTime());
        TellAboutSettings(start.Notice);
    }

    // A test run with its own settings file never touches the registry, for reading either.
    private bool? ReadRunKey()
    {
        if (_options.SettingsPath is not null)
        {
            return null;
        }

        try
        {
            return new StartupRegistration(new RegistryRunKey()).IsEnabled();
        }
        catch (Exception ex)
        {
            _journal.Write("startup registration: " + ex.Message);
            return null;
        }
    }

    private void TrySaveSettings()
    {
        var saved = false;
        switch (_settingsFile.NextWrite())
        {
            case WriteStep.Refuse:
                _journal.Write("settings not written: the file could not be read yet");
                break;
            case WriteStep.KeepBrokenFirst when !KeepBrokenFile():
                _journal.Write("settings not written: no copy of the broken file could be made");
                break;
            default:
                try
                {
                    _store.Save(_settings);
                    saved = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _journal.Write("settings not written: " + ex.Message);
                }

                break;
        }

        TellAboutSettings(_settingsFile.WriteEnded(saved, SettingsFileTime()));
    }

    private bool KeepBrokenFile()
    {
        if (_store.KeepCopyOfBrokenFile() is not { } copy)
        {
            return false;
        }

        _journal.Write("the broken settings file was kept as " + copy);
        _settingsFile.BrokenKept();
        return true;
    }

    private void TellAboutSettings(SettingsNotice notice)
    {
        if (SettingsFileState.ContentOf(notice, copyKept: !_settingsFile.BrokenOnDisk) is { } content)
        {
            _notices.Add(content);
            ShowNextNotice();
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

    /// <summary>The file can still be edited by hand: a change counts at the next press or menu.</summary>
    private void ReloadSettingsIfChanged()
    {
        var written = SettingsFileTime();
        if (!_settingsFile.ShouldRead(written))
        {
            return;
        }

        _settingsFile.Note(written);
        _settings = _store.Load();
        _journal.Write($"settings read again ({_store.LastStatus})");
        ApplyStartupRegistration();
        ApplyHotkeys();
        ApplyEngineOptions();
        _tray.Update(_trayState, Accent, _trayTip);
        _window?.Screen.ReplaceSettings(_settings);
    }

    /// <summary>Tells the engine what changed since it was last told, and nothing else.</summary>
    private void ApplyEngineOptions()
    {
        var next = SettingsMapping.ToEngineOptions(_settings);
        if (_engine is not { } engine || _engineOptions is not { } previous)
        {
            return;
        }

        _engineOptions = next;
        if (previous with { BufferDuration = next.BufferDuration, SourceApp = next.SourceApp } != next)
        {
            Watch(engine.SetDevicesAsync(next.SystemDeviceId, next.MicDeviceId, next.MicEnabled), "changing the devices");
        }

        if (!string.Equals(previous.SourceApp, next.SourceApp, StringComparison.OrdinalIgnoreCase))
        {
            Watch(engine.SetSourceAppAsync(next.SourceApp), "changing the recorded app");
        }

        if (previous.BufferDuration != next.BufferDuration)
        {
            Watch(engine.ResizeBufferAsync(next.BufferDuration), "resizing the buffer");
        }
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

        NoteHotkeys(_hotkeys.Apply(_settings.Hotkeys));
    }

    private void NoteHotkeys(IReadOnlyList<HotkeyResult> results)
    {
        _hotkeyResults = results;
        var problem = string.Empty;
        foreach (var result in results)
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
        _engineOptions = options;
        _journal.Write($"engine: buffer {options.BufferDuration}, system {options.SystemDeviceId ?? "(default)"}, app {options.SourceApp ?? "(whole PC)"}, mic enabled {options.MicEnabled}, mic {options.MicDeviceId ?? "(default)"}");

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

        // The window may have changed something while the engine was starting.
        ApplyEngineOptions();
        _journal.Write($"capture started: {status.Activity}, system {status.SystemDeviceName ?? "(none)"}, mic capturing {status.MicCapturing}, own sounds excluded {status.OwnSoundsExcluded}, {Journal.Memory()}");
        if (_options.SelfTestSave)
        {
            _loop.After((int)(_options.SelfTestDelaySeconds * 1000), RunSelfTestPress);
        }
    }

    private void OnStatus(AudioEngineStatus status)
    {
        if (status.SourceApp is { } app && (_lastStatus?.SourceApp != app || _lastStatus.SourceAppMissing != status.SourceAppMissing))
        {
            _journal.Write($"recorded app {app}: {(status.SourceAppMissing ? "not running" : "running")}, captured as {status.SystemDeviceName ?? "(nothing)"}");
        }

        var notice = StatusTransitions.Decide(_lastStatus, status);
        _lastStatus = status;
        _window?.Screen.UpdateStatus(status);
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

        Watch(engine.TogglePauseAsync(), "pause or resume");
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

    // A snapshot is one large array, and only a full collection gives it back. An app that sits in the background for
    // hours should not keep the last clip in memory; off this thread, and only once no other clip is on its way.
    private void ReleaseSnapshotMemory() => _ = Task.Run(() =>
    {
        if (_pipeline.WaitIdle(TimeSpan.Zero))
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        }
    });

    private void OnClipFinished(ClipOutcome outcome)
    {
        _loop.After(SnapshotReleaseDelayMs, ReleaseSnapshotMemory);
        if (outcome.Result is not { } result)
        {
            var content = outcome.Failure == ClipFailure.NothingCaptured ? NotificationStates.NothingToSave() : NotificationStates.SaveFailed();
            _journal.Write($"clip not saved: {outcome.Failure}");
            ShowClip(content, saved: false, important: outcome.Failure == ClipFailure.Failed);
            FinishSelfTest(outcome, false);
            return;
        }

        _lastClip = result;
        _lastClipTime = DateTime.Now;
        _lastRequested = outcome.Request.Requested;
        _window?.Screen.SetLastClip(LastClipShown());
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
            _journal.Write($"confirmation sound started: {ConfirmSound.Play()}");
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
        if (_notices.ShowsNow(content))
        {
            PresentNow(content, clickThrough);
        }
    }

    /// <summary>Shows what is next in line, if nothing on screen must stay. A lasting notice stays in line until it can be shown.</summary>
    private void ShowNextNotice()
    {
        if (!_canShowNotices)
        {
            return;
        }

        while (_notices.Next() is { } next)
        {
            var (state, covers) = Surface();
            var lasting = NoticeQueue.RankOf(next.Kind) == NoticeRank.Lasting;
            var decision = lasting
                ? FullscreenPolicy.ForClip(state, covers, _settings.General, saved: false, important: true)
                : FullscreenPolicy.ForStatus(state, covers, _settings.General, important: next.Kind == NotificationKind.NoDevice);
            if (decision.ShowOverlay)
            {
                _notices.Remove(next);
                _noticeRetries = 0;
                PresentNow(next, decision.ClickThrough);
                return;
            }

            if (lasting)
            {
                RetryNoticeLater();
                return;
            }

            _notices.Remove(next);
        }
    }

    // The same pace and the same limit as a clip waiting for the desktop; past it, the next press or menu looks again.
    private void RetryNoticeLater()
    {
        if (_noticeRetrying || _noticeRetries >= CatchUpTries)
        {
            return;
        }

        _noticeRetrying = true;
        _noticeRetries++;
        _loop.After(CatchUpPollMs, () =>
        {
            _noticeRetrying = false;
            ShowNextNotice();
        });
    }

    private void PresentNow(NotificationContent content, bool clickThrough)
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

        _journal.Write($"notification {content.Kind}: {content.Title} / {content.Body}");
        _cpuBeforeNotification = Journal.CpuMilliseconds();
        var leftEdge = string.Equals(_settings.General.NotificationPosition, "left", StringComparison.OrdinalIgnoreCase);
        _notification.Present(content, new NotificationPlacement(area, dpi, clickThrough, leftEdge), Accent);
        _notices.Shown(content);
    }

    private void OnNotificationFinished()
    {
        if (_options.SelfTestSave)
        {
            _journal.Write($"notification left the screen: {Journal.CpuMilliseconds() - _cpuBeforeNotification:F0} ms of process CPU while it was up (capture included), {Journal.Memory()}");
        }

        _notices.Finished();
        ShowNextNotice();
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
                OpenSettings(firstRun: false);
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

    // ---------------------------------------------------------------- the settings window

    /// <summary>Only one at a time, and only while it is open: closing destroys it, and the memory goes back.</summary>
    private void OpenSettings(bool firstRun)
    {
        if (_window is { } open)
        {
            open.BringBack();
            return;
        }

        var window = new SettingsWindow(_settings, this, ReadMicPeak, firstRun, _options.Offscreen, OnSettingsClosed);
        _window = window;
        if (_lastStatus is { } status)
        {
            window.Screen.UpdateStatus(status);
        }

        window.Screen.SetLastClip(LastClipShown());
        window.Screen.SetHotkeyResults(_hotkeyResults);
        window.SetIcon(Accent);
        window.Show();
        LoadChoices();

        // An app started while the window was in the background gets offered on the way back.
        window.Activated = LoadChoices;
        _journal.Write($"settings window opened, first launch {firstRun}, {Journal.Memory()}");
        if (_options.Offscreen && firstRun)
        {
            _loop.After(ProbeStepMs, () => _window?.ProbePicture(Path.ChangeExtension(_journal.Path, ".firstrun.png")));
        }
    }

    // Listing devices touches audio objects, which belong off the loop's thread. It opens nothing: no microphone starts.
    private void LoadChoices() => _ = Task.Run(() =>
    {
        var outputs = WasapiCaptureSourceFactory.ListDevices(AudioFlow.System).Select(d => new AudioDeviceChoice(d.Id, d.Name)).ToList();
        var mics = WasapiCaptureSourceFactory.ListDevices(AudioFlow.Mic).Select(d => new AudioDeviceChoice(d.Id, d.Name)).ToList();
        var apps = WasapiCaptureSourceFactory.ListApps().Select(a => new AudioAppChoice(a.ExeName, a.Name)).ToList();
        _journal.Write($"choices listed: {outputs.Count} outputs, {mics.Count} mics, {apps.Count} apps");
        _loop.Post(() => _window?.Screen.SetChoices(outputs, mics, apps));
    });

    private LastClipInfo? LastClipShown() =>
        _lastClip is { } clip ? new LastClipInfo(clip.Duration, _lastClipTime, clip.SizeBytes) : null;

    private void OnSettingsClosed()
    {
        _window = null;
        _journal.Write("settings window closed, " + Journal.Memory());

        // The window's objects are small and many: a full collection a moment later gives them back.
        _loop.After(SnapshotReleaseDelayMs, ReleaseWindowMemory);
    }

    private static void ReleaseWindowMemory() => _ = Task.Run(() =>
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    });

    // The level comes from what the capture already holds. If the microphone is off this reads nothing and opens nothing.
    private float ReadMicPeak()
    {
        var engine = _engine;
        if (engine is null || !_settings.Audio.MicEnabled)
        {
            return 0;
        }

        return MicMeter.Peak(engine.Snapshot(MeterWindow).Mic);
    }

    void ISettingsBackend.Applied(SettingsChange change)
    {
        TrySaveSettings();
        if ((change & SettingsChange.Startup) != 0)
        {
            ApplyStartupRegistration();
        }

        if ((change & SettingsChange.Accent) != 0)
        {
            _tray.Update(_trayState, Accent, _trayTip);
            _window?.SetIcon(Accent);
        }

        if ((change & (SettingsChange.Buffer | SettingsChange.Devices | SettingsChange.SourceApp)) != 0)
        {
            ApplyEngineOptions();
        }
    }

    void ISettingsBackend.SaveNow() => SaveClip(TimeSpan.FromSeconds(_settings.ClipSeconds));

    void ISettingsBackend.TogglePause() => TogglePause();

    void ISettingsBackend.CopyLast() => CopyLastClip();

    void ISettingsBackend.OpenFolder() => OpenLastClipFolder();

    void ISettingsBackend.ShowTestNotification() => Present(SampleClip.Notification(Accent), clickThrough: false);

    void ISettingsBackend.SuspendHotkeys() => _hotkeys?.Suspend();

    IReadOnlyList<HotkeyResult> ISettingsBackend.ApplyHotkeys(HotkeySettings hotkeys)
    {
        var results = _hotkeys?.Apply(hotkeys) ?? [];
        NoteHotkeys(results);
        return results;
    }

    IReadOnlyList<HotkeyResult> ISettingsBackend.ResumeHotkeys()
    {
        var results = _hotkeys?.Resume() ?? [];
        NoteHotkeys(results);
        return results;
    }

    // --settings-probe: opens the window off screen, sends it real messages, and writes what it measures. Never takes the focus.
    private void RunSettingsProbe()
    {
        var gdi = WindowNative.GetGuiResources(Native.GetCurrentProcess(), 0);
        var user = WindowNative.GetGuiResources(Native.GetCurrentProcess(), 1);
        _journal.Write($"probe: before opening, {Journal.Memory()}, GDI objects {gdi}, USER objects {user}");
        OpenSettings(firstRun: false);
        var window = _window!;
        var hwnd = window.Hwnd;
        var seen = new List<string>();

        void Key(nint target, uint vk) => Native.PostMessageW(target, WindowNative.WM_KEYDOWN, vk, 0);

        var steps = new List<Action>();
        steps.AddRange(Enumerable.Repeat<Action>(() => Key(hwnd, Vk.Down), 4));
        steps.Add(() => Key(hwnd, Vk.Up));
        steps.AddRange(Enumerable.Repeat<Action>(() => Key(hwnd, Vk.Tab), 5));
        steps.Add(() => WindowNative.SetWindowTextW(window.EditHwnd, "probe {date}"));
        steps.Add(() => seen.Add("template=" + _settings.Export.FileNameTemplate));
        steps.Add(() => window.ProbePicture(Path.ChangeExtension(_journal.Path, ".export.png")));
        steps.Add(() => Key(window.EditHwnd, Vk.Tab));
        steps.Add(() => window.ProbeDpiChange(144));
        steps.Add(() => seen.Add("client at 144 dpi=" + window.ClientSizeText()));
        steps.Add(() => window.ProbeDpiChange(96));
        steps.Add(() => seen.Add("client at 96 dpi=" + window.ClientSizeText()));
        var at = 0;
        _loop.After(ProbeStepMs, Next);

        void Next()
        {
            seen.Add($"{window.Screen.Section}/{window.Screen.FocusId}");
            if (at < steps.Count)
            {
                steps[at++]();
                _loop.After(ProbeStepMs, Next);
                return;
            }

            _journal.Write($"probe: reached {string.Join(" -> ", seen)}");
            _journal.Write($"probe: open, {Journal.Memory()}, GDI objects {WindowNative.GetGuiResources(Native.GetCurrentProcess(), 0)}, {Journal.CpuMilliseconds():F0} ms of CPU so far");
            window.Close();
            _loop.After(ProbeAfterCloseMs, AfterClosing);
        }

        void AfterClosing()
        {
            _journal.Write($"probe: 10 s after closing, {Journal.Memory()}, window gone {_window is null}, GDI objects {WindowNative.GetGuiResources(Native.GetCurrentProcess(), 0)}, USER objects {WindowNative.GetGuiResources(Native.GetCurrentProcess(), 1)}");
            var cycles = 0;
            Cycle();

            void Cycle()
            {
                if (_window is { } open)
                {
                    open.Close();
                    _loop.After(ProbeStepMs, Cycle);
                }
                else if (cycles++ < 3)
                {
                    OpenSettings(firstRun: false);
                    _loop.After(ProbeStepMs, Cycle);
                }
                else
                {
                    _journal.Write($"probe: after 3 more openings, {Journal.Memory()}, GDI objects {WindowNative.GetGuiResources(Native.GetCurrentProcess(), 0)}, USER objects {WindowNative.GetGuiResources(Native.GetCurrentProcess(), 1)}");
                    _journal.Write($"probe: folder dialog usable {FolderPicker.SelfCheck()}");
                    _loop.Quit();
                }
            }
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

        // An exe whose MP3 quietly became WAV is the failure this test exists to catch.
        var ok = outcome.Result is { } result && !result.Rescued && !result.FellBackToWav && (!outcome.Request.CopyWanted || copied);
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
        Guard(() => _window?.Dispose(), "settings window");
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
        _journal.Write($"left, {Journal.Memory()}, {Journal.CpuMilliseconds():F0} ms of CPU in all");
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
