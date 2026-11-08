using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Input;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Updates;

namespace Hindsound.App;

/// <summary>
/// The wiring: the message loop, the tray icon, the shortcuts, the engine and the clip path. Everything here runs on the
/// thread of the message loop, unless it says otherwise, and every other thread comes back through <see cref="UiLoop.Post"/>.
/// </summary>
internal sealed class HindsoundApp : IClipBackend, ISettingsBackend, IUpdateHost, IUpdatesPanel
{
    private const int SelfTestConfirmUpdateMs = 500;
    private const int SelfTestUpdateCheckMs = 3000;
    private const int SnapshotReleaseDelayMs = 500;
    private const int SelfTestExitDelayMs = 3300;
    private const int SelfTestLeaveCheckMs = 500;
    private const int SelfTestLongestWaitMs = 60_000;
    private const int ProbeStepMs = 300;
    private const string ProbeTextField = "export.template";
    private const int ProbeAfterCloseMs = 10_000;
    private const int StartupReadTries = 3;
    private const int StartupReadGapMs = 200;
    private const int SelfTestFailDelayMs = 1500;
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
    private readonly PendingNotices _pending;
    private readonly long _pretendGameUntilMs;
    private bool _pendingPollArmed;
    private bool _canShowNotices;
    private readonly NoticeRetry _noticeRetry = new();
    private bool _startTouchesRunKey;
    private AudioEngineOptions? _engineOptions;
    private SettingsWindow? _window;
    private bool _firstRun;
    private IReadOnlyList<HotkeyResult> _hotkeyResults = [];
    private AudioEngineStatus? _lastStatus;
    private bool _engineFailed;
    private readonly MicLossWatch _micWatch = new();
    private readonly CaptureOnReminder _captureOnReminder = new();
    private bool _micCheckArmed;
    private TrayState _trayState = TrayState.Active;
    private string _trayTip = "Hindsound";
    private readonly TrayAddRetry _trayRetry = new();
    private TrayClick? _pauseClick;
    private TrayClick? _lastMenuClick;
    private bool _quitting;
    private readonly LeaveFlow _leave = new();
    private bool _lastClipHasProblem;
    private readonly SessionEndBlock _sessionEndBlock = new();
    private TimeSpan _clipsLimit = QueuedClipsLimit;
    private int _copying;
    private readonly LatestLoad<AudioChoices> _choices;

    private ClipResult? _lastClip;
    private DateTime _lastClipTime;
    private TimeSpan _lastRequested;
    private NotificationContent? _lastClipNotice;

    private double _cpuBeforeNotification;
    private int _exitCode;

    // The updater. Null for a test run, a copy that was not installed, or a launch with any option but --update-failed.
    private readonly bool _updaterMayRun;
    private UpdateFlow? _updates;
    private string _updateWhy = UpdateWiring.TestRun;
    private bool _quitChosen;
    private string? _updatedTo;
    private readonly SaveThenUpdate _saveThenUpdate = new();
    private bool _firstRunCardFailed;


    /// <param name="updaterMayRun">The raw command line was empty, or held only what Setup passes: see <see cref="AppOptions.UpdaterMayRun"/>.</param>
    public HindsoundApp(AppOptions options, Journal journal, bool updaterMayRun = false)
    {
        _options = options;
        _journal = journal;
        _updaterMayRun = updaterMayRun;
        _store = new SettingsStore(options.SettingsPath);
        _pending = new PendingNotices(journal.Write);
        _pretendGameUntilMs = options.SelfTestGameSeconds is { } game ? NowMs + (long)(game * 1000) : 0;
        _saver = new ClipSaver(openMp3: OpenMp3);
        _choices = new LatestLoad<AudioChoices>(
            work => _loop.Post(work),
            lists => _window?.Screen.SetChoices(lists.Outputs, lists.Mics, lists.Apps),
            ex => _journal.Write("listing the audio choices: " + ex));
    }

    private sealed record AudioChoices(List<AudioDeviceChoice> Outputs, List<AudioDeviceChoice> Mics, List<AudioAppChoice> Apps);

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

    /// <summary>Whatever comes out of the running app, the icon goes and the engine stops before the exception goes on.</summary>
    public int Run()
    {
        try
        {
            RunUntilQuit();
        }
        finally
        {
            Shutdown();
        }

        return _exitCode;
    }

    private void RunUntilQuit()
    {
        UiErrors.Handler = ex => _journal.Write("error: " + ex);
        LoadSettings();
        _journal.Write($"started, settings: {_store.Path} ({_store.LastStatus}), {Journal.Memory()}");
        if (_options.NoClipboard)
        {
            _journal.Write("clipboard: never touched in this run (--no-clipboard), whatever the settings say");
        }

        if (_options.NoHotkeys)
        {
            _journal.Write("hotkeys: none registered in this run (--no-hotkeys), whatever the settings say");
        }

        // After the Mutex, before the icon, the hotkeys and the capture: a Setup waiting on disk may install from here.
        if (StartUpdates())
        {
            return;
        }

        _loop = new UiLoop();
        _notification = new Notification { Clicked = OnNotificationClicked, Finished = OnNotificationFinished };
        _pipeline = new ClipPipeline(this, _loop.Post, _loop.After, ex => _journal.Write("clip error: " + ex))
        {
            SavingSlowly = OnSavingSlowly,
            Finished = OnClipFinished,
            Started = OnClipStarted,
        };

        _tray = new TrayIcon(_loop.Hwnd, UiLoop.TrayCallbackMessage);
        _loop.TrayMessage = OnTrayMessage;
        _loop.TaskbarCreated = () => NoteTray(_tray.Add(_trayState, Accent, _trayTip));
        _loop.ScaleMayHaveChanged = OnScaleMayHaveChanged;
        _loop.SessionEnding = OnSessionEnding;
        _loop.SessionEndAsked = OnSessionEndAsked;
        _loop.SessionKept = TakeBackSessionEndReason;
        _loop.CloseAsked = AskToLeave;
        _loop.ShowAsked = OnShowAsked;
        NoteTray(_tray.Add(_trayState, Accent, _trayTip));

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
            OpenFirstRunCard();
        }

        UpdateWiring.StartAfterLaunch(_updates, _firstRunCardFailed);

        if (_options.SelfTestUpdateCheck && _options.SettingsPath is not null)
        {
            _loop.After(SelfTestUpdateCheckMs, RunUpdateCheckSelfTest);
        }

        if (_options.SettingsProbe)
        {
            _loop.After(ProbeStepMs, RunSettingsProbe);
        }

        if (_options.ExitAfterSeconds is { } seconds)
        {
            _loop.After((int)Math.Min(seconds * 1000, int.MaxValue), AskToLeave);
        }

        if (_options.SelfTestFail == AppOptions.FailRun)
        {
            // Long enough for the capture to be running, which is the case this is about; and a thread that never ends,
            // like a capture stuck in the driver: only leaving through Environment.Exit gets past it.
            new Thread(() => Thread.Sleep(Timeout.Infinite)) { IsBackground = false }.Start();
            Thread.Sleep(SelfTestFailDelayMs);
            throw new InvalidOperationException("--selftest-fail " + AppOptions.FailRun);
        }

        SettingsRetry.Watching(_settingsFile, () => NowMs, _loop.After, ReloadSettingsIfChanged).Arm();
        _loop.Run();
    }

    // The capture is already running: a card that can't open is no reason to lose it.
    private void OpenFirstRunCard()
    {
        try
        {
            if (_options.SelfTestFail == AppOptions.FailCard)
            {
                throw new InvalidOperationException("--selftest-fail " + AppOptions.FailCard);
            }

            OpenSettings(firstRun: true);
        }
        catch (Exception ex)
        {
            _journal.Write("the first-launch card could not open: " + ex);
            _firstRunCardFailed = true;
            Guard(() => _window?.Dispose(), "first-launch card");
            _window = null;
        }
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
        (_settings, var start) = _settingsFile.Load(_store, StartupReadTries, () => Thread.Sleep(StartupReadGapMs), ReadRunKey);
        _firstRun = start.FirstRun;
        _startTouchesRunKey = start.TouchRunKey;
        if (start.MakeSafe)
        {
            _journal.Write($"settings not usable ({_store.LastStatus}): defaults with the mic off, kept as {_store.LastBackupPath ?? "(not moved)"}");
        }

        if (start.Write)
        {
            TrySaveSettings();
        }

        _settingsFile.Note(SettingsFileTime());
        TellAboutSettings(start.Notice);
    }

    private bool? ReadRunKey()
    {
        if (_options.RunKey() is not { } runKey)
        {
            return null;
        }

        try
        {
            return new StartupRegistration(runKey).IsEnabled();
        }
        catch (Exception ex)
        {
            _journal.Write("startup registration: " + ex.Message);
            return null;
        }
    }

    private void TrySaveSettings() => TellAboutSettings(_settingsFile.TrySave(KeepBrokenFile, WriteSettings, SettingsFileTime, _journal.Write));

    private bool WriteSettings()
    {
        try
        {
            _store.Save(_settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _journal.Write("settings not written: " + ex.Message);
            return false;
        }
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

    /// <summary>
    /// The file can still be edited by hand: a change counts at the next press or menu. A file the start could not read
    /// also comes here by itself, see <see cref="SettingsRetry"/>. Never throws: whatever happens here, the press that
    /// called it goes on.
    /// </summary>
    private void ReloadSettingsIfChanged()
    {
        try
        {
            ReadSettingsAgain();
            ShowNextNotice();
            RefreshTray();
        }
        catch (Exception ex)
        {
            _journal.Write("reading the settings again: " + ex);
        }
    }

    private void ReadSettingsAgain()
    {
        if (_options.SelfTestFail == AppOptions.FailReload)
        {
            throw new InvalidOperationException("--selftest-fail " + AppOptions.FailReload);
        }

        var written = SettingsFileTime();
        if (!_settingsFile.ShouldRead(written))
        {
            return;
        }

        var read = _store.Read();
        var decision = _settingsFile.Reread(read.Status, written);
        if (decision.JournalLine is { } line)
        {
            _journal.Write(line);
        }

        _notices.Withdraw(SettingsFileState.CeasedBy(read.Status));
        TellAboutSettings(decision.Notice);
        if (!decision.Apply || read.Settings is not { } settings)
        {
            return;
        }

        _settings = settings;
        ApplyStartupRegistration();
        ApplyHotkeys();
        ApplyEngineOptions();
        UpdateTray();
        _window?.Screen.ReplaceSettings(_settings);
        _window?.Screen.SetHotkeyResults(_hotkeyResults);
        _window?.SetIcon(Accent);

        // The mode may have changed by hand, or the real one replaces the Off of a file that could not be read at first.
        _updates?.SettingsChanged();
    }

    /// <summary>Tells the engine what changed since it was last told, and nothing else.</summary>
    private void ApplyEngineOptions()
    {
        var next = SettingsMapping.ToEngineOptions(_settings, _options);
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

    // A refusal changes the setting back, so the switch in the window, drawn from that same object, shows the truth at its next refresh.
    private void ApplyStartupRegistration()
    {
        if (_options.RunKey() is not { } runKey)
        {
            return;
        }

        try
        {
            if (!StartupSwitch.Apply(_settings.General, runKey))
            {
                _journal.Write($"startup registration: Windows refused the change, it now says {_settings.General.StartWithWindows}");
                TellAboutSettings(SettingsNotice.StartupNotChanged);
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
            _hotkeys = new HotkeyManager(_options.NewHotkeyLayer());

            // The hotkey thread only asks for a turn on the loop.
            _hotkeys.Pressed += action => _loop.Post(() => OnHotkey(action));
            ApplyHotkeys();
        }
        catch (Exception ex)
        {
            _journal.Write("hotkeys: " + ex);
            NoteHotkeys(HotkeyManager.WithoutManager(_settings.Hotkeys));
        }
    }

    private void ApplyHotkeys()
    {
        // On the way out they are gone on purpose. Otherwise the manager never came up, and the settings may name others now.
        if (_hotkeys is null && _leave.Waiting)
        {
            return;
        }

        NoteHotkeys(_hotkeys?.Apply(_settings.Hotkeys) ?? HotkeyManager.WithoutManager(_settings.Hotkeys));
    }

    private void NoteHotkeys(IReadOnlyList<HotkeyResult> results)
    {
        _hotkeyResults = results;
        foreach (var result in results)
        {
            if (result.Status != HotkeyStatus.NotSet)
            {
                _journal.Write($"hotkey {result.Action} {result.Gesture}: {result.Status}");
            }
        }

        RefreshTray();
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
        var options = SettingsMapping.ToEngineOptions(_settings, _options);
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
                _loop.Post(() =>
                {
                    _engineFailed = true;
                    RefreshTray();
                    ShowStatus(NotificationStates.NoDevice());
                });
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

        var notice = _captureOnReminder.Adjust(StatusTransitions.Decide(_lastStatus, status), status);
        var micNotice = _micWatch.Note(status, NowMs);
        _lastStatus = status;
        _engineFailed = false;
        var withdrawn = _pending.WithdrawCeased(status);
        _notices.WithdrawCeased(status);
        _window?.Screen.UpdateStatus(status);
        switch (notice)
        {
            case StatusNotice.CaptureOn:
                ShowStatus(StatusTransitions.CaptureOnNow(_settings, _hotkeyResults, _updatedTo, KeptSecondsNow), TakePauseScreen());
                break;
            case StatusNotice.Paused:
                ShowStatus(NotificationStates.Paused(KeptSecondsNow ?? 0), TakePauseScreen());
                break;
            case StatusNotice.NoDevice:
                ShowStatus(NotificationStates.NoDevice());
                break;
            case StatusNotice.DeviceChanged:
                ShowStatus(NotificationStates.DeviceChanged(status.SystemDeviceName ?? "the default output"));
                break;
        }

        if (MicLossWatch.WorthTelling(micNotice, notice, lossNeverShown: withdrawn?.Kind == NotificationKind.MicLost))
        {
            TellAboutMic(micNotice);
        }

        ArmMicCheck();
        RefreshTray();
    }

    private void TellAboutMic(StatusNotice notice)
    {
        if (notice == StatusNotice.MicLost)
        {
            ShowStatus(NotificationStates.MicLost());
        }
        else if (notice == StatusNotice.MicBack)
        {
            ShowStatus(NotificationStates.MicBack());
        }
    }

    // The status only changes on a change: if the microphone stays away, no new one comes to end the wait.
    private void ArmMicCheck()
    {
        if (_micCheckArmed || !_micWatch.Waiting)
        {
            return;
        }

        _micCheckArmed = true;
        _loop.After(MicLossWatch.SettleMs, () =>
        {
            _micCheckArmed = false;
            TellAboutMic(_micWatch.Check(NowMs));
            ArmMicCheck();
        });
    }

    private void TogglePause()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        Watch(engine.TogglePauseAsync(), "pause or resume");
    }

    // The answer to Pause in the menu comes back later, as a new status.
    private ScreenInfo? TakePauseScreen()
    {
        var screen = _pauseClick?.ScreenAt(NowMs);
        _pauseClick = null;
        return screen;
    }

    private void Watch(Task task, string what) =>
        _ = task.ContinueWith(t => _journal.Write($"{what}: {t.Exception}"), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    // ---------------------------------------------------------------- the clip path

    private TrayClick? ClickStandingIn => TrayClick.StandingIn(Native.GetForegroundWindow() == _loop.Hwnd, _lastMenuClick);

    private void SaveClip(TimeSpan requested)
    {
        var click = ClickStandingIn;
        SaveClip(requested, click is { } standing ? standing.AppName : ForegroundWindow.Read().AppName, click?.Screen);
    }

    private void SaveClip(TimeSpan requested, string? appName, ScreenInfo? screen) =>
        _pipeline.Press(new ClipRequest(requested, SettingsMapping.ToExportOptions(_settings), appName, SettingsMapping.CopyWanted(_settings, _options)) { Screen = screen });

    AudioSnapshot IClipBackend.Snapshot(TimeSpan duration) =>
        _engine?.Snapshot(duration) ?? new AudioSnapshot([], []);

    ClipResult IClipBackend.Save(AudioSnapshot snapshot, ExportOptions options, string? appName)
    {
        if (_options.SelfTestSlowSaveSeconds is { } slow)
        {
            Thread.Sleep(TimeSpan.FromSeconds(slow));
        }

        return _saver.Save(snapshot, options, appName);
    }

    // The update card greys "Save it, then update" and "Update" while a clip is being written: from the press on, not
    // only once the first clip is over. Other pages draw nothing from it.
    private void OnClipStarted()
    {
        if (_window?.Screen is { Section: Section.UpdateCard } screen)
        {
            screen.RefreshUpdates();
        }
    }

    private void OnSavingSlowly(ClipRequest request) =>
        ShowStatus(NotificationStates.Saving(request.Requested), request.Screen);

    // A clip is not over until it has been copied too: that goes on after the pipeline is done with it.
    private bool ClipsInProgress => _pipeline.Busy || _copying > 0;

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
            try
            {
                var content = outcome.Failure switch
                {
                    ClipFailure.NothingCaptured => NotificationStates.NothingToSave(),
                    ClipFailure.Busy => NotificationStates.StillSaving(),
                    _ => NotificationStates.SaveFailed(),
                };
                _journal.Write($"clip not saved: {outcome.Failure}");
                ShowClip(content, saved: false, important: outcome.Failure == ClipFailure.Failed, screen: outcome.Request.Screen);
                FinishSelfTest(outcome, false);
            }
            finally
            {
                LeaveIfClipsDone();
                UpdateIfClipsDone(saved: false);
            }

            return;
        }

        _lastClip = result;
        _lastClipTime = DateTime.Now;
        _lastRequested = outcome.Request.Requested;

        // What is true until the copy has been tried: the clip is in its folder.
        _lastClipNotice = NotificationStates.ForClip(result, _lastRequested, copyWanted: false, copied: false, Accent);
        _window?.Screen.SetLastClip(LastClipShown());
        _journal.Write($"clip saved: {string.Join(", ", result.Paths)}, {result.Duration.TotalSeconds:F2} s of {outcome.Request.Requested.TotalSeconds:F0} s, {result.SizeBytes} bytes, silent {result.IsSilent}, rescued {result.Rescued}, fell back to WAV {result.FellBackToWav}, {Journal.Memory()}");
        if (outcome.Request.CopyWanted)
        {
            _copying++;
            CopyClip(result, copied =>
            {
                _copying--;
                FinishClip(outcome, result, copied);
            });
        }
        else
        {
            FinishClip(outcome, result, false);
        }
    }

    private void CopyClip(ClipResult result, Action<bool> done) =>
        SettingsMapping.CopyToClipboard(_options, () => ClipboardFiles.TrySet(_loop.Hwnd, result.Paths), _loop.After, done);

    private void FinishClip(ClipOutcome outcome, ClipResult result, bool copied)
    {
        try
        {
            var content = NotificationStates.ForClip(result, outcome.Request.Requested, outcome.Request.CopyWanted, copied, Accent);
            _journal.Write($"clipboard: wanted {outcome.Request.CopyWanted}, copied {copied}; notification {content.Kind}");
            NoteLastClipNotice(result, content);
            var copyFailed = outcome.Request.CopyWanted && !copied;
            ShowClip(content, saved: true, important: result.Rescued || copyFailed, copyFailed, outcome.Request.Screen);
            FinishSelfTest(outcome, copied);
        }
        finally
        {
            LeaveIfClipsDone();
            UpdateIfClipsDone(saved: true);
        }
    }

    // The first-launch card says what the notification says. Only about the last clip: a copy can end after a newer one is written.
    private void NoteLastClipNotice(ClipResult clip, NotificationContent content)
    {
        if (ReferenceEquals(_lastClip, clip))
        {
            _lastClipNotice = content;
            _window?.Screen.SetLastClip(LastClipShown());
        }
    }

    private void CopyLastClip(ScreenInfo? screen = null)
    {
        if (_lastClip is not { } clip)
        {
            ShowStatus(NotificationStates.NothingToSave(), screen);
            return;
        }

        if (!ClipFiles.AllThere(clip.Paths, File.Exists))
        {
            _journal.Write($"last clip gone: {string.Join(", ", clip.Paths)}");
            _lastClip = null;
            _window?.Screen.SetLastClip(null);
            ShowClip(NotificationStates.LastClipGone(), saved: false, important: true, copyFailed: true, screen);
            return;
        }

        // The answer to a press: the sound is only for a new clip, and a copy that failed is said whatever the settings.
        CopyClip(clip, copied =>
        {
            var content = NotificationStates.ForClip(clip, _lastRequested, true, copied, Accent);
            NoteLastClipNotice(clip, content);
            ShowClip(content, saved: false, important: !copied, copyFailed: !copied, screen);
        });
    }

    // ---------------------------------------------------------------- showing

    private static long NowMs => Environment.TickCount64;

    private void ShowClip(NotificationContent content, bool saved, bool important, bool copyFailed = false, ScreenInfo? screen = null)
    {
        _lastClipHasProblem = LeaveFlow.IsProblem(content.Kind, important);
        var (state, covers) = Surface();
        var decision = FullscreenPolicy.ForClip(state, covers, _settings.General, saved, important, copyFailed);
        _journal.Write($"presentation: state {state}, covers screen {covers}: {decision}");
        if (decision.PlaySound)
        {
            _journal.Write($"confirmation sound started: {ConfirmSound.Play()}");
        }

        if (decision.ShowOverlay)
        {
            _pending.NewerClipShown();
            Present(content, decision.ClickThrough, screen);
        }

        if (decision.CatchUpLater)
        {
            _pending.AddClip(content, important, NowMs);
            ArmPendingPoll();
            RefreshTray();
        }
    }

    // Read when a notification is made, which can be long after the status that asked for it: at a launch, that one says 0.
    private int? KeptSecondsNow => _lastStatus is { } status ? KeptDuration.SecondsOf(status.Buffered) : null;

    private void ShowStatus(NotificationContent content, ScreenInfo? screen = null)
    {
        var (state, covers) = Surface();
        var decision = FullscreenPolicy.ForStatus(state, covers, _settings.General, PendingNotices.IsImportantStatus(content.Kind));
        if (decision.ShowOverlay)
        {
            Present(content, decision.ClickThrough, screen);
        }

        if (decision.CatchUpLater)
        {
            _journal.Write($"notification {content.Kind} waits for the desktop: {content.Title} / {content.Body}");
            _pending.AddStatus(content, NowMs);
            ArmPendingPoll();
            RefreshTray();
        }
    }

    // Also read on the update pass's thread: nothing here may send a message to the loop and wait, as reading a window title would.
    private unsafe (UserNotificationState State, bool Covers) Surface()
    {
        if (_options.SelfTestGameSeconds is not null)
        {
            return NowMs < _pretendGameUntilMs ? (UserNotificationState.RunningD3dFullScreen, true) : (UserNotificationState.AcceptsNotifications, false);
        }

        int raw;
        var state = Native.SHQueryUserNotificationState(&raw) == 0 ? (UserNotificationState)raw : UserNotificationState.Unknown;
        return (state, ForegroundWindow.ForegroundCovers());
    }

    private void Present(NotificationContent content, bool clickThrough, ScreenInfo? screen = null)
    {
        if (_notices.ShowsNow(content))
        {
            PresentNow(content, clickThrough, screen);
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
                : FullscreenPolicy.ForStatus(state, covers, _settings.General, PendingNotices.IsImportantStatus(next.Kind));
            if (decision.ShowOverlay)
            {
                _notices.Remove(next);
                _noticeRetry.Shown();
                if (lasting && _notification.Showing is { } onScreen)
                {
                    _notices.PushedAside(onScreen, _lastStatus);
                }

                PresentNow(StatusTransitions.Fresh(next, _settings, _hotkeyResults, _updatedTo, KeptSecondsNow), decision.ClickThrough);
                return;
            }

            if (lasting)
            {
                RetryNoticeLater();
                return;
            }

            _notices.Remove(next);
            if (decision.CatchUpLater)
            {
                _pending.AddStatus(next, NowMs);
                ArmPendingPoll();
            }
        }
    }

    // A long game can keep it from showing for hours: the pace drops, it never stops.
    private void RetryNoticeLater()
    {
        if (_noticeRetry.Arm(NowMs) is not { } delay)
        {
            return;
        }

        _loop.After(delay, () =>
        {
            _noticeRetry.Fired();
            ShowNextNotice();
        });
    }

    private void PresentNow(NotificationContent content, bool clickThrough, ScreenInfo? clicked = null)
    {
        var screen = clicked ?? ClickStandingIn?.Screen ?? ForegroundWindow.ActiveScreen();
        var covers = ForegroundWindow.CoversScreen(screen);
        var area = NotificationPlacement.AreaOn(screen, covers);
        var dpi = screen.Dpi;
        _journal.Write($"notification area: {area.Left},{area.Top},{area.Right},{area.Bottom} at {dpi} dpi, foreground covers the screen {covers}{(_options.Offscreen ? ", drawn off screen instead" : string.Empty)}");
        if (_options.Offscreen)
        {
            var right = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN) - 4000;
            var top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN) - 4000;
            area = new Native.RECT { Left = right - 1920, Top = top, Right = right, Bottom = top + 1080 };
            dpi = 96;
            clickThrough = true;
        }

        _journal.Write($"notification {content.Kind}: {content.Title} / {content.Body}");
        _cpuBeforeNotification = Journal.CpuMilliseconds();
        var leftEdge = string.Equals(_settings.General.NotificationPosition, "left", StringComparison.OrdinalIgnoreCase);
        _notification.Present(content, new NotificationPlacement(area, dpi, clickThrough, leftEdge), Accent);
        _notices.Shown(content);
        _captureOnReminder.Shown(content.Kind);

        // Only on screen does an announcement count as made, and "Updated to" as said.
        if (content.Kind == NotificationKind.UpdateReady)
        {
            _updates?.Shown();
        }
        else if (content.Kind == NotificationKind.CaptureOn)
        {
            _updatedTo = null;
        }
    }

    private void OnNotificationFinished()
    {
        if (_options.SelfTestSave)
        {
            _journal.Write($"notification left the screen: {Journal.CpuMilliseconds() - _cpuBeforeNotification:F0} ms of process CPU while it was up (capture included), {Journal.Memory()}");
        }

        _notices.Finished();
        ShowNextNotice();
        ShowNextPendingOnDesktop();
        LeaveIfNoticeGone();
    }

    /// <summary>The check only runs while something waits for the desktop, and stops by itself once nothing does.</summary>
    private void ArmPendingPoll()
    {
        if (_pendingPollArmed || !_pending.Any)
        {
            return;
        }

        _pendingPollArmed = true;
        _loop.After(_pending.PollDelayMs(NowMs), PollPending);
    }

    private void PollPending()
    {
        _pendingPollArmed = false;
        try
        {
            _pending.GiveUpOld(NowMs);
            ShowNextPendingOnDesktop();
        }
        finally
        {
            ArmPendingPoll();
            LeaveIfNoticeGone();
        }
    }

    // One at a time: what is next waits for this one to leave, and comes back here through OnNotificationFinished.
    private void ShowNextPendingOnDesktop()
    {
        if (_notification.Showing is not null || !_pending.Any)
        {
            return;
        }

        var (state, covers) = Surface();
        if (FullscreenPolicy.BackOnDesktop(state, covers) && _pending.TakeNext(NowMs) is { } next)
        {
            Present(StatusTransitions.Fresh(next, _settings, _hotkeyResults, _updatedTo, KeptSecondsNow), clickThrough: false);
            RefreshTray();
        }
    }

    // ---------------------------------------------------------------- tray

    /// <summary>Called whenever one of the things the icon says has changed.</summary>
    private void RefreshTray()
    {
        var (state, tip) = StatusTransitions.TrayOf(_lastStatus, _hotkeyResults, _pending.ImportantClipWaiting, _engineFailed, _leave.Waiting, _settingsFile.StillOnDefaults);
        if (state != _trayState || tip != _trayTip)
        {
            _journal.Write($"tray: {state}, {tip}");
            _trayState = state;
            _trayTip = tip;
            UpdateTray();
        }
    }

    private void OnScaleMayHaveChanged()
    {
        var stale = _tray.IconIsStale;
        _journal.Write($"the display scale may have changed: the tray icon is {_tray.IconSize} px, a new one is needed {stale}");
        if (stale)
        {
            UpdateTray();
        }
    }

    private void UpdateTray() => NoteTray(_tray.Update(_trayState, Accent, _trayTip));

    // Without its icon the app can't be quit with the mouse, and Explorer may not be ready at the opening of a session.
    private void NoteTray(bool there)
    {
        if (_trayRetry.Note(there) is { } line)
        {
            _journal.Write(line);
        }

        if (_trayRetry.ShouldArm(there))
        {
            _loop.After(TrayAddRetry.DelayMs, () =>
            {
                _trayRetry.Fired();
                UpdateTray();
            });
        }
    }

    private void OnTrayMessage(uint code, nuint wParam)
    {
        switch (TrayMenu.GestureOf(code))
        {
            case TrayGesture.Menu:
                OnTrayMenu(wParam);
                break;
            case TrayGesture.OpenSettings:
                ReloadSettingsIfChanged();
                OpenSettings(firstRun: false);
                break;
        }
    }

    private void OnTrayMenu(nuint wParam)
    {
        var (click, command) = TrayMenu.ReadThenShow(
            () => new TrayClick(ForegroundWindow.Read().AppName, ForegroundWindow.CursorScreen(), NowMs),
            () => AskTrayMenu(wParam));
        _lastMenuClick = click;
        _journal.Write($"tray menu: {command}, app name known {click.AppName is not null}, screen of the click {click.Screen.Monitor.Left},{click.Screen.Monitor.Top},{click.Screen.Monitor.Right},{click.Screen.Monitor.Bottom}");
        switch (command)
        {
            case TrayCommand.Save30s:
                SaveClip(TimeSpan.FromSeconds(30), click.AppName, click.Screen);
                break;
            case TrayCommand.Save15s:
                SaveClip(TimeSpan.FromSeconds(15), click.AppName, click.Screen);
                break;
            case TrayCommand.Save1m:
                SaveClip(TimeSpan.FromMinutes(1), click.AppName, click.Screen);
                break;
            case TrayCommand.Save5m:
                SaveClip(TimeSpan.FromMinutes(5), click.AppName, click.Screen);
                break;
            case TrayCommand.CopyLast:
                CopyLastClip(click.Screen);
                break;
            case TrayCommand.TogglePause:
                _pauseClick = click;
                TogglePause();
                break;
            case TrayCommand.OpenFolder:
                OpenClipsFolder();
                break;
            case TrayCommand.Settings:
                OpenSettings(firstRun: false);
                break;
            case TrayCommand.Quit:
                _quitChosen |= UpdateExit.ChoosesQuit(command);
                AskToLeave(click.Screen);
                break;
            case TrayCommand.UpdateNow:
                OpenSettings(firstRun: false);
                _window?.Screen.ShowUpdateCard();

                // Nothing can click the card from outside: the test flow confirms it by itself, as the user would.
                if (_options.Offscreen && _updateWhy == UpdateWiring.TestFolder)
                {
                    _loop.After(SelfTestConfirmUpdateMs, () => ((IUpdatesPanel)this).ConfirmUpdate(saveFirst: false));
                }

                break;
        }
    }

    private TrayCommand AskTrayMenu(nuint wParam)
    {
        ReloadSettingsIfChanged();

        // Off screen, nothing shows and nothing takes the focus: a message posted by a test names the command itself.
        if (_options.Offscreen)
        {
            return (TrayCommand)(wParam & 0xFFFF);
        }

        var paused = _lastStatus?.Activity == CaptureActivity.Paused;
        return _tray.ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF), TrayMenu.Items(paused, _lastClip is not null, _leave.Waiting,
            _updates?.MenuVersion is { } version ? $"Update to {version}..." : null, _lastStatus?.Buffered));
    }

    private void OpenClipsFolder()
    {
        var folder = SettingsMapping.ToExportOptions(_settings).OutputFolder;
        OpenOffLoop(folder, () =>
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                _journal.Write("clips folder: " + ex.Message);
            }
        });
    }

    private void OnNotificationClicked()
    {
        // Before the settings: a failed update is a Problem, which opens the settings too, but at its own section.
        if (_notification.Showing is { ClickShowsUpdates: true })
        {
            OpenSettings(firstRun: false);
            _window?.Screen.ShowUpdates();
        }
        else if (_notification.Showing is { ClickOpensSettings: true })
        {
            OpenSettings(firstRun: false);
        }
        else
        {
            OpenLastClipFolder();
        }
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
            OpenOffLoop(folder);
        }
    }

    // ---------------------------------------------------------------- the settings window

    /// <summary>Only one at a time, and only while it is open: closing destroys it, and the memory goes back.</summary>
    private void OpenSettings(bool firstRun)
    {
        if (!_leave.MayOpenSettings)
        {
            return;
        }

        if (_window is { } open)
        {
            open.BringBack();
            return;
        }

        // The screen the user is looking at, whatever the active window is: a game on the first one, a click on the second.
        var screen = ForegroundWindow.CursorScreen();
        var window = new SettingsWindow(_settings, this, ReadMicPeak, firstRun, _options.Offscreen, screen, OnSettingsClosed);
        _window = window;

        // Always: without an updater, the panel says this copy does not update itself.
        window.Screen.SetUpdates(this);
        if (_lastStatus is { } status)
        {
            window.Screen.UpdateStatus(status);
        }

        window.Screen.SetLastClip(LastClipShown());
        window.Screen.SetHotkeyResults(_hotkeyResults);
        window.SetIcon(Accent);
        window.Show();
        LoadChoices();

        // On the way back: a click activates the window first, so a file edited by hand meanwhile is read before that
        // click changes anything; and an app started while the window was in the background gets offered.
        window.Activated = () =>
        {
            ReloadSettingsIfChanged();
            LoadChoices();
        };
        _journal.Write($"settings window opened, first launch {firstRun}, on the screen of the cursor {screen.Monitor.Left},{screen.Monitor.Top},{screen.Monitor.Right},{screen.Monitor.Bottom}{(_options.Offscreen ? " (drawn off screen instead)" : string.Empty)}, {Journal.Memory()}");
        if (_options.Offscreen && firstRun)
        {
            _loop.After(ProbeStepMs, () => _window?.ProbePicture(Path.ChangeExtension(_journal.Path, ".firstrun.png")));
        }
    }

    // A second launch: the user is looking for the app, and the icon may be hidden.
    private void OnShowAsked()
    {
        _journal.Write("a second launch asks to see the app");
        ReloadSettingsIfChanged();
        OpenSettings(firstRun: false);
    }

    // Listing devices touches audio objects, which belong off the loop's thread. It opens nothing: no microphone starts.
    private void LoadChoices() => _choices.Start(() =>
    {
        var outputs = WasapiCaptureSourceFactory.ListDevices(AudioFlow.System).Select(d => new AudioDeviceChoice(d.Id, d.Name)).ToList();
        var mics = WasapiCaptureSourceFactory.ListDevices(AudioFlow.Mic).Select(d => new AudioDeviceChoice(d.Id, d.Name)).ToList();
        var apps = WasapiCaptureSourceFactory.ListApps().Select(a => new AudioAppChoice(a.ExeName, a.Name)).ToList();
        _journal.Write($"choices listed: {outputs.Count} outputs, {mics.Count} mics, {apps.Count} apps");
        return new AudioChoices(outputs, mics, apps);
    });

    private LastClipInfo? LastClipShown() =>
        _lastClip is { } clip ? new LastClipInfo(clip.Duration, _lastClipTime, clip.SizeBytes) { Notice = _lastClipNotice } : null;

    private void OnSettingsClosed()
    {
        _window = null;
        _choices.Forget();

        // A restart only ever follows a card the user is looking at.
        _updates?.CardClosed();
        _saveThenUpdate.Stop();
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
        // Only the keyboard gets here without the window having come back first.
        if (_settingsFile.ChangedUnread(SettingsFileTime()))
        {
            _journal.Write("the settings file changed and was not read: this change from the window writes over it");
            _settingsFile.SawBeforeWriting(_store.Read().Status);
        }

        StartupSwitch.ApplyThenSave((change & SettingsChange.Startup) != 0, ApplyStartupRegistration, TrySaveSettings);
        RefreshTray();
        if ((change & SettingsChange.Accent) != 0)
        {
            UpdateTray();
            _window?.SetIcon(Accent);
        }

        if ((change & (SettingsChange.Buffer | SettingsChange.Devices | SettingsChange.SourceApp)) != 0)
        {
            ApplyEngineOptions();
        }

        if ((change & SettingsChange.Updates) != 0)
        {
            _updates?.SettingsChanged();
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
        var results = _hotkeys?.Apply(hotkeys) ?? HotkeyManager.WithoutManager(hotkeys);
        NoteHotkeys(results);
        return results;
    }

    IReadOnlyList<HotkeyResult> ISettingsBackend.ResumeHotkeys()
    {
        var results = _hotkeys?.Resume() ?? HotkeyManager.WithoutManager(_settings.Hotkeys);
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
        steps.Add(() =>
        {
            // Not by posted keys: the window reads the modifiers from the real keyboard, and the PC may be in use.
            if (window.Screen.TabTo(ProbeTextField))
            {
                WindowNative.SetWindowTextW(window.EditHwnd, "probe {date}");
            }
            else
            {
                _exitCode = 1;
                _journal.Write($"probe: FAILED, the focus never reached {ProbeTextField}: it is on {window.Screen.Section}/{window.Screen.FocusId}, and nothing was typed");
            }
        });
        steps.Add(() => seen.Add("template=" + _settings.Export.FileNameTemplate));
        steps.Add(() => window.ProbePicture(Path.ChangeExtension(_journal.Path, ".export.png")));
        steps.Add(() => Key(window.EditHwnd, Vk.Tab));
        steps.Add(() => window.ProbeDpiChange(144));
        steps.Add(() => seen.Add("client at 144 dpi=" + window.ClientSizeText()));
        steps.Add(() => seen.Add("icons at 144 dpi=" + window.IconWidths()));
        steps.Add(() => window.ProbeDpiChange(96));
        steps.Add(() => seen.Add("client at 96 dpi=" + window.ClientSizeText()));
        steps.Add(() => seen.Add("icons at 96 dpi=" + window.IconWidths()));
        steps.Add(() => window.Screen.ShowSection(Section.Hotkeys));
        steps.Add(() => window.ProbePicture(Path.ChangeExtension(_journal.Path, ".hotkeys.png")));
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

    // A sleeping network drive or a slow shell can take many seconds: the shortcuts must not wait behind it.
    private void OpenOffLoop(string target, Action? first = null) => OffLoop.Start(() =>
    {
        first?.Invoke();
        OpenWithShell(target);
    }, ex => _journal.Write($"could not open {target}: {ex}"));

    private void OpenWithShell(string target)
    {
        const int largestShellError = 32;

        // Off screen nothing is ever shown, a folder least of all.
        if (_options.Offscreen)
        {
            _journal.Write($"off the loop, on thread {Environment.CurrentManagedThreadId}, not the loop's: {target} would open now");
            return;
        }

        var result = Native.ShellExecuteW(0, null, target, null, null, Native.SW_SHOWNORMAL);
        if (result <= largestShellError)
        {
            _journal.Write($"could not open {target}: shell error {result}");
        }
    }

    // ---------------------------------------------------------------- updates

    private static string? ExeFolder => Environment.ProcessPath is { } path ? Path.GetDirectoryName(path) : null;

    /// <summary>Says in the log which updater this run has, and runs its launch step. True when Setup started: the app leaves at once.</summary>
    private bool StartUpdates()
    {
        _updateWhy = UpdateWiring.Why(_updaterMayRun, _options, ExeFolder);
        _updates = UpdateWiring.Create(_updaterMayRun, _options, ExeFolder, this, DateTimeOffset.Now);
        _journal.Write($"updates: active {_updateWhy == UpdateWiring.Installed} ({_updateWhy})");
        LaunchReport report;
        try
        {
            if (_updates?.AtLaunch(_options.UpdateFailed, UpdateSurface()) is not { } launched)
            {
                return false;
            }

            report = launched;
        }
        catch (Exception ex)
        {
            // The capture is what the user started the app for: an updater that breaks goes, the rest starts.
            _journal.Write("updates: the launch step failed, no updates in this run: " + ex);
            _updates = null;
            return false;
        }

        if (report.SetupStarted)
        {
            _journal.Write("the update installs: leaving before the capture starts");
            return true;
        }

        if (report.Outcome == LaunchOutcome.Updated && report.UpdatedTo is { } updated)
        {
            _updatedTo = updated.ToString();
        }
        else if (report.Outcome == LaunchOutcome.Failed)
        {
            // Lasting: shown once the icon is there, and "Capture on" right after does not push it away.
            _notices.Add(NotificationStates.UpdateFailed(UpdateWiring.InstalledVersion().ToString(), report.WillRetry));
        }

        return false;
    }

    private UpdateSurface UpdateSurface()
    {
        var (state, covers) = Surface();
        return new UpdateSurface(
            UpdateDecider.IsFullScreen(state, covers),
            UpdateDecider.IsPresentation(state),
            _pipeline is { } pipeline && (pipeline.Busy || Volatile.Read(ref _copying) > 0),
            _sessionEndBlock.Ending);
    }

    /// <summary>After each clip: "Save it, then update" goes on once every clip has a result, and only if all were saved.</summary>
    private void UpdateIfClipsDone(bool saved)
    {
        if (_leave.Waiting)
        {
            // Leaving decides alone: Quit from the menu installs, and nothing else does.
            _saveThenUpdate.Stop();
            return;
        }

        switch (_saveThenUpdate.ClipEnded(saved, ClipsInProgress))
        {
            case SaveThenUpdateStep.Update:
                _journal.Write("update: the clip is saved, updating");
                if (_updates is { } updates)
                {
                    TakeClick(updates.UpdateClicked());
                    return;
                }

                break;
            case SaveThenUpdateStep.ClipFailed:
                _journal.Write("update: the clip was not saved, nothing is updated");
                break;
        }

        // The card greys its buttons while a clip is being written.
        _window?.Screen.RefreshUpdates();
    }

    private void TakeClick(ClickAnswer answer)
    {
        _journal.Write(answer.Kind == ClickKind.NotStarted ? $"update clicked: {answer.Kind}, {answer.Status}" : $"update clicked: {answer.Kind}");
        if (answer.Kind == ClickKind.Applied)
        {
            // The decider has already refused while a clip was being written: nothing for the leave flow to wait for.
            _loop.Quit();
            return;
        }

        _window?.Screen.RefreshUpdates();
    }

    // --selftest-update-check: the only run that reaches GitHub, for one GET of the manifest, with the memory around it.
    private void RunUpdateCheckSelfTest()
    {
        _journal.Write($"update check selftest: before, {Journal.Memory()}, handles {HandleCount()}");
        _ = UpdatePass.Run(UpdateWiring.SelfTestCheck).ContinueWith(
            t => _loop.Post(() =>
            {
                _journal.Write(t.IsCompletedSuccessfully
                    ? $"update check: {t.Result.Status} {t.Result.Transport.Status} {t.Result.Transport.Detail}"
                    : $"update check: failed with {t.Exception?.GetBaseException()}");
                _journal.Write($"update check selftest: after, {Journal.Memory()}, handles {HandleCount()}");
                AskToLeave();
            }),
            TaskScheduler.Default);
    }

    private static int HandleCount()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        return self.HandleCount;
    }

    UpdateMode IUpdateHost.Mode => SettingsMapping.ToUpdateMode(_settings.General);

    UpdateVersion? IUpdateHost.Skipped => SettingsMapping.ToSkipped(_settings.General);

    void IUpdateHost.SetSkipped(UpdateVersion? version)
    {
        _settings.General.Updates.SkippedVersion = version?.ToString();
        TrySaveSettings();
    }

    DateTimeOffset IUpdateHost.Now => DateTimeOffset.Now;

    UpdateSurface IUpdateHost.Surface() => UpdateSurface();

    bool IUpdateHost.FirstRunCardOpen => _window?.Screen.Section == Section.FirstRun;

    void IUpdateHost.After(int milliseconds, Action action) => _loop.After(milliseconds, action);

    // The end of a pass always comes back on the loop, also when the work threw: the flow frees itself there.
    void IUpdateHost.RunPass<T>(Func<T> work, Action<T?, Exception?> done) where T : default =>
        _ = UpdatePass.Run(work).ContinueWith(
            t => _loop.Post(() => done(t.IsCompletedSuccessfully ? t.Result : default, t.Exception?.GetBaseException())),
            TaskScheduler.Default);

    // One path in a game and out of it: the announcement waits for the desktop, as a clip does.
    void IUpdateHost.Announce(UpdateVersion version, UpdateMode mode)
    {
        var content = mode == UpdateMode.Auto
            ? NotificationStates.UpdateReady(version.ToString(), Accent)
            : NotificationStates.UpdateAvailable(version.ToString(), Accent);
        _pending.AddUpdate(content, NowMs);
        ArmPendingPoll();
        ShowNextPendingOnDesktop();
    }

    void IUpdateHost.Withdraw() => _pending.WithdrawUpdate();

    void IUpdateHost.Changed() => _window?.Screen.RefreshUpdates();

    void IUpdateHost.LeaveForSetup() => _loop.Quit();

    void IUpdateHost.Journal(string line) => _journal.Write(line);

    UpdatePanelView IUpdatesPanel.View
    {
        get
        {
            var view = _updates?.View ?? UpdatePanel.Inactive(UpdateWiring.InstalledVersion());
            if (_saveThenUpdate.Waiting)
            {
                return view with { CardLine = "Saving your clip, then updating...", CardBusy = true };
            }

            return _saveThenUpdate.ClipFailed ? view with { CardLine = "The clip was not saved. Nothing was updated." } : view;
        }
    }

    bool IUpdatesPanel.ClipBusy => ClipsInProgress;

    int IUpdatesPanel.BufferSeconds => _settings.BufferSeconds;

    void IUpdatesPanel.CheckNow() => _updates?.CheckNow();

    void IUpdatesPanel.ConfirmUpdate(bool saveFirst)
    {
        if (_updates is not { } updates)
        {
            return;
        }

        if (!saveFirst)
        {
            _saveThenUpdate.Stop();
            TakeClick(updates.UpdateClicked());
            return;
        }

        // The whole memory, so what the restart loses is in the clip.
        _journal.Write("update: saving a clip first");
        _saveThenUpdate.Start();
        SaveClip(TimeSpan.FromSeconds(_settings.BufferSeconds));
        _window?.Screen.RefreshUpdates();
    }

    // The window calls it once it is off the card, wherever it went: moving it here would undo a click in the side bar.
    void IUpdatesPanel.CancelUpdate()
    {
        _updates?.CardClosed();
        _saveThenUpdate.Stop();
    }

    void IUpdatesPanel.WhatsNew()
    {
        if (((IUpdatesPanel)this).View.Offered is { } version)
        {
            OpenOffLoop(UpdateUrlPolicy.ReleasePage(version));
        }
    }

    void IUpdatesPanel.SkipThisVersion() => _updates?.SkipThisVersion();

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

        if (_options.SelfTestClipGone && outcome.Result is { } written)
        {
            _lastClip = written with { Paths = [written.PrimaryPath + ".gone"] };
            CopyLastClip();
        }

        // The notification goes through its whole life before leaving, and so does what still waits for the desktop.
        var latest = NowMs + SelfTestLongestWaitMs;
        _loop.After(SelfTestExitDelayMs, Leave);

        void Leave()
        {
            if ((_pending.Any || _notification.Showing is not null) && NowMs < latest)
            {
                _loop.After(SelfTestLeaveCheckMs, Leave);
            }
            else
            {
                _loop.Quit();
            }
        }
    }

    // ---------------------------------------------------------------- leaving

    /// <summary>Quit, from the menu or from WM_CLOSE. A clip still being written keeps the app here, and the icon says so.</summary>
    private void AskToLeave() => AskToLeave(null);

    private void AskToLeave(ScreenInfo? screen)
    {
        switch (_leave.Asked(ClipsInProgress))
        {
            case LeaveStep.LeaveNow:
                LeaveForGood();
                break;

            case LeaveStep.WaitForClips:
                _journal.Write($"asked to quit with clips being written: waiting for them, {LeaveFlow.WaitLimit} at most");

                // The window first: it hands back the shortcuts it may be holding. Gone for good, the shortcuts stay gone
                // even if the settings file is read again.
                Guard(() => _window?.Dispose(), "settings window");
                Guard(() =>
                {
                    _hotkeys?.Dispose();
                    _hotkeys = null;
                }, "hotkeys");
                RefreshTray();
                ShowStatus(NotificationStates.ClosingAfterClip(), screen);
                _loop.After((int)LeaveFlow.WaitLimit.TotalMilliseconds, OnLeaveLimit);
                break;
        }
    }

    private void LeaveIfClipsDone()
    {
        if (!_leave.ClipEnded(ClipsInProgress))
        {
            return;
        }

        var onScreen = _notification.Showing is not null;
        if (_leave.HoldForNotice(_lastClipHasProblem, onScreen, _pending))
        {
            _journal.Write("the last clip is done, with a problem still to be read: leaving after that");
            if (onScreen)
            {
                _loop.After(LeaveFlow.NoticeHoldMs, () =>
                {
                    if (_leave.NoticeLimit(_pending))
                    {
                        _journal.Write("the problem has been on screen long enough: leaving");
                        LeaveForGood();
                    }
                });
            }

            return;
        }

        _journal.Write("the last clip is done: leaving");
        LeaveForGood();
    }

    private void LeaveIfNoticeGone()
    {
        if (_leave.NoticeGone(_notification.Showing is not null, _pending))
        {
            _journal.Write("the problem has been shown or given up on: leaving");
            LeaveForGood();
        }
    }

    /// <summary>
    /// The four ways out once nothing holds the app. The update installs only when the user chose Quit in the menu,
    /// decided now rather than at the click: a clip written meanwhile has made the moment wrong, and the decider says so.
    /// </summary>
    private void LeaveForGood()
    {
        if (UpdateExit.InstallOnLeaving(_quitChosen, _updates is { } updates ? updates.ApplyOnQuit : null))
        {
            _journal.Write("the update will install now that Hindsound leaves");
        }

        _loop.Quit();
    }

    private void OnLeaveLimit()
    {
        if (_leave.TimeIsUp())
        {
            _journal.Write($"asked to quit {LeaveFlow.WaitLimit} ago: leaving with clips still being written");
            _clipsLimit = TimeSpan.Zero;
            _loop.Quit();
        }
    }

    // The answer is yes whatever happens: the reason only tells the user what Windows is waiting for.
    private void OnSessionEndAsked()
    {
        // First, and it cannot throw: from here no Setup at the end of a session, and a running download stops.
        if (_sessionEndBlock.Asked(ClipsInProgress))
        {
            var given = Native.ShutdownBlockReasonCreate(_loop.Hwnd, SessionEndBlock.Reason) != 0;
            _journal.Write($"the session may end, with clips being written: reason given {given}");
        }
    }

    private void TakeBackSessionEndReason()
    {
        if (_sessionEndBlock.Over())
        {
            var taken = Native.ShutdownBlockReasonDestroy(_loop.Hwnd) != 0;
            _journal.Write($"the reason to wait at the end of the session is gone: {taken}");
        }
    }

    private void OnSessionEnding()
    {
        _clipsLimit = SessionEndBlock.ClipsLimit;
        Shutdown();
    }

    /// <summary>Icon first, so the user sees the app gone; then the shortcuts; queued clips get written; the engine last.</summary>
    private void Shutdown()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;

        // Never a Setup from here: every way out passes through, the end of a session and an exception included.
        Guard(() => _updates?.Stop(), "updates");
        Guard(() => _window?.Dispose(), "settings window");
        Guard(() => _tray?.Dispose(), "tray");
        Guard(() => _notification?.Dispose(), "notification");
        Guard(() => _hotkeys?.Dispose(), "hotkeys");
        Guard(() =>
        {
            if (_pipeline?.WaitIdle(_clipsLimit) == false)
            {
                _journal.Write("leaving with clips still being written");
            }
        }, "queued clips");
        Guard(TakeBackSessionEndReason, "reason at the end of the session");
        Guard(() =>
        {
            var stopping = Task.Run(() => _engine?.Dispose());
            if (!stopping.Wait(EngineStopLimit))
            {
                _journal.Write("the engine did not stop in time");
            }
        }, "engine");
        Guard(() => _loop?.Dispose(), "window");
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
