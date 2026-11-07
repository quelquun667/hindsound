using System.Diagnostics;

namespace Hindsound.Updates;

/// <summary>What the screen and the app are doing, as far as the updater cares. Read on the update thread too.</summary>
internal readonly record struct UpdateSurface(bool FullScreen, bool Presentation, bool ClipInProgress, bool SessionEnding)
{
    public bool Busy => FullScreen || Presentation || ClipInProgress || SessionEnding;
}

/// <summary>The app, as the flow sees it. Everything is called on the loop, except <see cref="Surface"/>.</summary>
internal interface IUpdateHost
{
    UpdateMode Mode { get; }

    UpdateVersion? Skipped { get; }

    void SetSkipped(UpdateVersion? version);

    DateTimeOffset Now { get; }

    /// <summary>Called on the update thread too: reads no window title, which would send a message to the loop and wait.</summary>
    UpdateSurface Surface();

    /// <summary>The first launch must tell about the check before it happens.</summary>
    bool FirstRunCardOpen { get; }

    /// <summary>A one-shot timer that cannot be cancelled: the flow drops an old one by its generation.</summary>
    void After(int milliseconds, Action action);

    /// <summary>Runs <paramref name="work"/> off the loop, then <paramref name="done"/> back on it, with the exception if any.</summary>
    void RunPass<T>(Func<T> work, Action<T?, Exception?> done);

    /// <summary>Waits for the desktop in the pending notices: the same path in a game and out of it.</summary>
    void Announce(UpdateVersion version, UpdateMode mode);

    void Withdraw();

    /// <summary>The view changed: the section and the card are drawn again.</summary>
    void Changed();

    /// <summary>Setup started for a click that stood while its version downloaded: the app quits now, as after <see cref="ClickKind.Applied"/>.</summary>
    void LeaveForSetup();

    void Journal(string line);
}

internal enum ClickKind
{
    /// <summary>Setup started: the app quits right away.</summary>
    Applied,

    /// <summary>The click stands until the download ends, and only while the card stays open.</summary>
    Downloading,
    NotNow,
    Nothing,

    /// <summary>Setup did not start: see <see cref="ClickAnswer.Status"/>.</summary>
    NotStarted,
}

/// <param name="Status">Only meaningful for <see cref="ClickKind.NotStarted"/>.</param>
internal readonly record struct ClickAnswer(ClickKind Kind, RunStatus Status = default);

/// <summary>
/// Between the decider and the app. Lives on the loop: it holds the state in memory, arms one timer at a time, runs one pass
/// at a time, and is the only writer of state.json. A pass never writes it, so a pass still running at Quit changes nothing.
/// </summary>
internal sealed class UpdateFlow(UpdateStateStore store, string folder, UpdateVersion installed, IUpdateTransport transport,
    Action<ProcessStartInfo> start, IUpdateHost host, DateTimeOffset startedAt, TimeSpan minimumDelay)
{
    // The same values as the settings file: a short lock from an antivirus or a backup passes.
    private const int StateReadAttempts = 3;

    /// <summary>How long the timer waits again while the first-run card is open.</summary>
    public static readonly TimeSpan FirstRunWait = TimeSpan.FromMinutes(5);

    private UpdateStateData _state = new();
    private UpdateManifest? _manifest;
    private bool _passRunning;
    private bool _passIsManual;
    private bool _clickStands;
    private int _timerGeneration;
    private volatile bool _stopPass;
    private bool _leaving;
    private bool _stateKnown;
    private bool _started;
    private bool _updateFailedFlag;
    private UpdateVersion? _announcing;
    private UpdatePassKind _passKind;
    private UpdateVersion? _passVersion;
    private UpdateProblem _problem;
    private RunStatus? _runProblem;
    private UpdateCardNote _card;

    /// <summary>The pause between two reads of a locked state.json. The tests make it zero.</summary>
    public TimeSpan StateRetryPause { get; init; } = TimeSpan.FromMilliseconds(200);

    public UpdateVersion? MenuVersion => _stateKnown
        ? UpdatePanel.MenuVersion(host.Mode, installed, Parse(_state.HighestSeen), host.Skipped, _manifest?.Version, UpdateFiles.DownloadedVersion(_state, folder))
        : null;

    public UpdatePanelView View => UpdatePanel.Compute(new UpdatePanelFacts
    {
        Installed = installed,
        Mode = host.Mode,
        Now = host.Now,
        StateKnown = _stateKnown,
        LastCheck = _state.LastCheck,
        HighestSeen = Parse(_state.HighestSeen),
        Manifest = _manifest?.Version,
        Downloaded = UpdateFiles.DownloadedVersion(_state, folder),
        Skipped = host.Skipped,
        Attempts = _state.AttemptsByVersion,
        Pass = _passRunning ? _passKind : UpdatePassKind.None,
        PassVersion = _passVersion,
        Problem = _problem,
        Run = _runProblem,
        ClickStands = _clickStands,
        Card = _card,
    });

    /// <summary>After the Mutex, before the icon and the capture: reads the state once, cleans, and may start a waiting Setup.</summary>
    public LaunchReport AtLaunch(bool updateFailedFlag, UpdateSurface surface)
    {
        _updateFailedFlag = updateFailedFlag;
        if (!TryLoadState(out var read))
        {
            host.Journal("update state: locked, nothing is written until it can be read");

            // Saying it failed touches no file.
            return new LaunchReport(false, updateFailedFlag ? LaunchOutcome.Failed : LaunchOutcome.NoUpdateExpected, null, false);
        }

        // Read before the plan clears it: the decider's guard against chaining attempts needs the version that was expected.
        UpdateVersion? expected = Parse(read.ExpectedVersion);
        var plan = TakeLaunchPlan(read);
        host.Journal($"update launch: {plan.Outcome}");

        if (plan.MayApply
            && UpdateDecider.Decide(Situation(UpdateEvent.Launching, surface) with { Expected = expected }) == UpdateAction.Apply
            && RunSetup(UpdateDecider.RestartAfterApply(UpdateEvent.Launching)) == RunStatus.Started)
        {
            return new LaunchReport(true, plan.Outcome, null, false);
        }

        var pending = Parse(_state.Pending?.Version);
        var willRetry = host.Mode == UpdateMode.Auto && pending is { } version && _state.AttemptsFor(version) < UpdateDecider.MaxAttemptsPerVersion;
        return new LaunchReport(false, plan.Outcome, plan.Outcome == LaunchOutcome.Updated ? installed : null, willRetry);
    }

    /// <summary>Arms the first timer. A state still locked is read again in 30 minutes instead.</summary>
    public void Start()
    {
        if (_started || _leaving)
        {
            return;
        }

        _started = true;
        if (_stateKnown)
        {
            ArmTimer();
        }
        else
        {
            ArmStateRetry();
        }
    }

    /// <summary>The mode or the skipped version may differ.</summary>
    public void SettingsChanged()
    {
        if (_started && !_leaving && _stateKnown)
        {
            if (host.Mode == UpdateMode.Off)
            {
                _timerGeneration++;

                // A check the user asked for is only cut by a game.
                if (_passRunning && !_passIsManual)
                {
                    _stopPass = true;
                }
            }
            else if (!_passRunning)
            {
                // The end of a running pass decides anyway. Otherwise decide again soon, at the floor of the timer.
                Arm(TimeSpan.Zero, Step);
            }
        }

        host.Changed();
    }

    public void CheckNow()
    {
        if (_leaving || _passRunning || !_stateKnown)
        {
            return;
        }

        if (UpdateDecider.Decide(Situation(UpdateEvent.CheckNowClicked, host.Surface())) != UpdateAction.Check)
        {
            _problem = UpdateProblem.Paused;
            host.Changed();
            return;
        }

        StartCheck(manual: true);
    }

    /// <summary>The Update button of the card.</summary>
    public ClickAnswer UpdateClicked()
    {
        if (_leaving || _passRunning || !_stateKnown)
        {
            return new ClickAnswer(ClickKind.Nothing);
        }

        _card = UpdateCardNote.None;
        return Click();
    }

    /// <summary>The card or the window closed: the download goes on, and its Setup waits for the next click.</summary>
    public void CardClosed()
    {
        _clickStands = false;
        _card = UpdateCardNote.None;
    }

    public void SkipThisVersion()
    {
        if (_leaving || _passRunning || !_stateKnown
            || UpdatePanel.Offered(installed, Parse(_state.HighestSeen), _manifest?.Version, UpdateFiles.DownloadedVersion(_state, folder)) is not { } version)
        {
            return;
        }

        host.SetSkipped(version);
        _state = _state with { Pending = null };
        Save("skip");
        UpdateFiles.Clean(folder, null);
        _announcing = null;
        _clickStands = false;
        _card = UpdateCardNote.None;
        host.Withdraw();
        host.Changed();
    }

    /// <summary>Quit from the menu, once nothing holds the app any more. True when Setup started.</summary>
    public bool ApplyOnQuit()
    {
        if (!_stateKnown || _leaving)
        {
            return false;
        }

        // A pass still running is left behind: the Setup already on disk may install, and Run hashes it again.
        _stopPass = true;
        _leaving = true;
        _timerGeneration++;
        return UpdateDecider.Decide(Situation(UpdateEvent.Quitting, host.Surface())) == UpdateAction.Apply
            && RunSetup(UpdateDecider.RestartAfterApply(UpdateEvent.Quitting)) == RunStatus.Started;
    }

    /// <summary>The announcement reached the screen: only now does it count as made.</summary>
    public void Shown()
    {
        if (!_stateKnown || _announcing is not { } version)
        {
            return;
        }

        _state = _state with { AnnouncedVersion = version.ToString() };
        Save("announced");
    }

    /// <summary>The app leaves. Nothing waits for a pass: its thread is a background one, and its piece goes at the next launch.</summary>
    public void Stop()
    {
        _leaving = true;
        _stopPass = true;
        _timerGeneration++;
    }

    private static UpdateVersion? Parse(string? text) => UpdateVersion.TryParse(text, out var version) ? version : null;

    private bool TryLoadState(out UpdateStateData state)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (store.TryLoad(out state))
            {
                return true;
            }

            if (attempt == StateReadAttempts)
            {
                return false;
            }

            Thread.Sleep(StateRetryPause);
        }
    }

    private LaunchPlan TakeLaunchPlan(UpdateStateData read)
    {
        var plan = UpdateLaunch.Plan(installed, read, _updateFailedFlag);
        if (!Equals(plan.Next, read) && !store.Save(plan.Next))
        {
            host.Journal("update state: not saved at launch");
        }

        // A piece left by a pass cut at the last exit goes here, in Ask and Off too.
        UpdateFiles.Clean(folder, plan.Keep);
        _state = plan.Next;
        _stateKnown = true;
        return plan;
    }

    // Still locked: no automatic step, and no Setup or notification when it finally reads. Only the plan of the launch.
    private void ArmStateRetry() => Arm(UpdateDecider.BusyRetry, () =>
    {
        if (!TryLoadState(out var read))
        {
            ArmStateRetry();
            return;
        }

        TakeLaunchPlan(read);
        host.Journal("update state: read at last");
        host.Changed();
        Step();
    });

    private UpdateSituation Situation(UpdateEvent trigger, UpdateSurface surface) => new()
    {
        Event = trigger,
        Mode = host.Mode,
        Now = host.Now,
        AppStartedAt = startedAt,
        LastCheck = _state.LastCheck,
        FullScreen = surface.FullScreen,
        Presentation = surface.Presentation,
        ClipInProgress = surface.ClipInProgress,
        SessionEnding = surface.SessionEnding,
        Installed = installed,
        Manifest = _manifest?.Version,
        HighestSeen = Parse(_state.HighestSeen),
        Skipped = host.Skipped,
        Announced = Parse(_state.AnnouncedVersion),

        // Never Pending alone: a Setup an antivirus took away would pass for downloaded for ever, and fail at every Quit.
        Downloaded = UpdateFiles.DownloadedVersion(_state, folder),
        AttemptsByVersion = _state.AttemptsByVersion,
    };

    // The timer cannot be cancelled: a new generation makes every older one do nothing when it fires.
    private void Arm(TimeSpan delay, Action step)
    {
        var generation = ++_timerGeneration;

        // Even with a clock gone mad, the network is never asked more than once a minute.
        if (delay < minimumDelay)
        {
            delay = minimumDelay;
        }

        host.After((int)Math.Min(delay.TotalMilliseconds, int.MaxValue), () =>
        {
            if (generation == _timerGeneration)
            {
                step();
            }
        });
    }

    private void ArmTimer()
    {
        var now = host.Now;
        if (UpdateDecider.NextCheckAt(host.Mode, startedAt, _state.LastCheck, now) is not { } due)
        {
            // Off: no timer at all, and the one armed before does nothing.
            _timerGeneration++;
            return;
        }

        Arm(due - now, Step);
    }

    // The only place that acts on its own.
    private void Step()
    {
        if (_leaving || _passRunning || !_stateKnown)
        {
            return;
        }

        if (host.FirstRunCardOpen)
        {
            Arm(FirstRunWait, Step);
            return;
        }

        var situation = Situation(UpdateEvent.Tick, host.Surface());
        switch (UpdateDecider.Decide(situation))
        {
            case UpdateAction.Check:
                StartCheck(manual: false);
                break;

            case UpdateAction.Download:
                StartDownload(manual: false);
                break;

            case UpdateAction.Announce:
                AnnounceOnce();
                ArmTimer();
                break;

            case UpdateAction.WaitThirtyMinutes:
                // An announcement waits for the desktop in the pending notices, rather than 30 minutes here.
                if (WouldAnnounce(situation))
                {
                    AnnounceOnce();
                }

                host.Journal("update: busy, waits 30 min");
                Arm(UpdateDecider.BusyRetry, Step);
                break;

            default:
                ArmTimer();
                break;
        }
    }

    private static bool WouldAnnounce(UpdateSituation situation) =>
        UpdateDecider.Decide(situation with { FullScreen = false, Presentation = false, ClipInProgress = false }) == UpdateAction.Announce;

    // Each arrival in the pending notices starts 30 s of fast polling: once per version, not once per timer.
    private void AnnounceOnce()
    {
        if (_manifest?.Version is not { } version || _announcing == version)
        {
            return;
        }

        _announcing = version;
        host.Announce(version, host.Mode);
    }

    private ClickAnswer Click()
    {
        var surface = host.Surface();
        switch (UpdateDecider.Decide(Situation(UpdateEvent.UpdateClicked, surface)))
        {
            case UpdateAction.Apply:
                var status = RunSetup(UpdateDecider.RestartAfterApply(UpdateEvent.UpdateClicked));
                if (status == RunStatus.Started)
                {
                    return new ClickAnswer(ClickKind.Applied);
                }

                _card = UpdateCardNote.NotStarted;
                host.Changed();
                return new ClickAnswer(ClickKind.NotStarted, status);

            case UpdateAction.Download:
                _clickStands = true;
                StartDownload(manual: true);
                return new ClickAnswer(ClickKind.Downloading);

            default:
                if (!surface.Busy)
                {
                    return new ClickAnswer(ClickKind.Nothing);
                }

                // Dropped, not kept for later: coming back on it would be a restart nobody is looking at.
                _card = UpdateCardNote.NotNow;
                host.Changed();
                return new ClickAnswer(ClickKind.NotNow);
        }
    }

    // UpdateRunner reads the file, the flow trusts its memory: the memory goes to disk first.
    private RunStatus RunSetup(bool restartApp)
    {
        var ran = store.Save(_state);
        var status = ran ? new UpdateRunner(store, folder, start).Run(restartApp) : RunStatus.StateNotSaved;
        host.Journal($"update setup: {status}");
        if (status == RunStatus.Started)
        {
            _leaving = true;
            _timerGeneration++;
            return status;
        }

        _runProblem = status;

        // Run counted an attempt, or forgot a Pending whose file changed: take that back, if the file can be read.
        if (ran && store.TryLoad(out var after))
        {
            _state = after;
        }

        return status;
    }

    private void BeginPass(UpdatePassKind kind, UpdateVersion? version, bool manual)
    {
        // The end of the pass arms the next timer.
        _timerGeneration++;
        _passRunning = true;
        _passIsManual = manual;
        _passKind = kind;
        _passVersion = version;

        // Set by a switch to Off or by Quit. Left set, every later pass would stop itself.
        _stopPass = false;
        _problem = UpdateProblem.None;
        _runProblem = null;
        host.Changed();
    }

    // The mode is not in it: a Check now in Off is only cut by a game.
    private bool ShouldStop() => _stopPass || host.Surface().Busy;

    private void StartCheck(bool manual)
    {
        BeginPass(UpdatePassKind.Check, null, manual);
        host.RunPass(() => UpdateDownloader.Check(transport, ShouldStop), CheckDone);
    }

    private void StartDownload(bool manual)
    {
        // The decider asks for a download, and a click gets one, only with a manifest.
        var manifest = _manifest!;
        BeginPass(UpdatePassKind.Download, manifest.Version, manual);
        host.RunPass(() => UpdateDownloader.Download(manifest, folder, transport, ShouldStop), (result, ex) => DownloadDone(manifest, result, ex));
    }

    private void CheckDone(CheckResult result, Exception? ex)
    {
        _passRunning = false;

        // First: the default result of a pass that threw reads Found.
        if (ex is not null)
        {
            host.Journal($"update check: failed with {ex.GetType().Name}");
            result = new CheckResult(CheckStatus.TransportFailed, new TransportResult(TransportStatus.Failed), default, null);
        }

        if (_leaving)
        {
            return;
        }

        if (result.Status == CheckStatus.TransportFailed && result.Transport.Status == TransportStatus.Stopped)
        {
            if (StoppedByOff("check"))
            {
                return;
            }

            // It did not happen: no check time, and another try once the game is over.
            host.Journal("update check: stopped, waits 30 min");
            _problem = UpdateProblem.Paused;
            Arm(UpdateDecider.BusyRetry, Step);
            host.Changed();
            return;
        }

        // Kept in memory even when the disk refuses it: otherwise the next check stays due, and the timer loops at its floor.
        _state = _state with { LastCheck = host.Now };

        if (result.Status == CheckStatus.Found && result.Value is { } manifest)
        {
            _manifest = manifest;
            _state = _state.WithSeen(manifest.Version);
            var skipped = host.Skipped;
            var skippedAfter = UpdateDecider.SkippedAfter(skipped, manifest.Version);
            if (skippedAfter != skipped)
            {
                host.SetSkipped(skippedAfter);
            }

            host.Journal($"update check: Found {manifest.Version}");
            Save("check");
            host.Changed();
            Step();
            return;
        }

        host.Journal($"update check: {result.Status} {result.Transport.Status} {result.Transport.Detail} {result.Manifest}");
        _problem = UpdateProblem.CheckFailed;
        Save("check");
        ArmTimer();
        host.Changed();
    }

    private void DownloadDone(UpdateManifest manifest, DownloadResult result, Exception? ex)
    {
        _passRunning = false;

        // First: the default result of a pass that threw reads Ready.
        if (ex is not null)
        {
            host.Journal($"update download: failed with {ex.GetType().Name}");
            result = new DownloadResult(DownloadStatus.DiskFailed, default, null);
        }

        if (_leaving)
        {
            return;
        }

        if (result.Status == DownloadStatus.Ready)
        {
            var before = _state;
            _state = _state with { Pending = new PendingSetup(manifest.Version.ToString(), manifest.Sha256, manifest.Size) };

            // An antivirus may take an unsigned Setup away as soon as it is written. Gone already, the step after would
            // download it again at once, with no timer, and again: the network in a loop.
            var setupKept = UpdateFiles.DownloadedVersion(_state, folder) == manifest.Version;
            if (!setupKept)
            {
                host.Journal("update download: the Setup is gone");
            }

            if (setupKept && Save("download"))
            {
                host.Journal($"update download: Ready {manifest.Version}");
                host.Changed();
                if (_clickStands)
                {
                    _clickStands = false;

                    // The pass took the timer away: without a new one, nothing would check again until a restart.
                    if (!ApplyStandingClick())
                    {
                        ArmTimer();
                    }
                }
                else
                {
                    Step();
                }

                return;
            }

            // Without Pending on disk, Run would find nothing; without the Setup, nothing waits to install: a failure like
            // any other, and the next try waits for the next check.
            _state = before;
            result = result with { Status = DownloadStatus.DiskFailed };
        }

        if (result.Status == DownloadStatus.Interrupted && result.Transport.Status == TransportStatus.Stopped)
        {
            if (StoppedByOff("download"))
            {
                return;
            }

            host.Journal("update download: stopped, waits 30 min");
            _problem = UpdateProblem.Paused;
            DropClick(UpdateCardNote.NotNow);
            Arm(UpdateDecider.BusyRetry, Step);
            host.Changed();
            return;
        }

        host.Journal($"update download: {result.Status} {result.Transport.Status} {result.Transport.Detail}");

        // Without a manifest the decider asks for no download until the next due check: a broken release cannot loop.
        _manifest = null;
        _problem = UpdateProblem.DownloadFailed;
        DropClick(UpdateCardNote.DownloadFailed);
        ArmTimer();
        host.Changed();
    }

    // A switch to Off stopped the automatic pass, not a game: no pause to tell, and nothing to try again in 30 minutes.
    // A check the user asked for is only stopped by a game, even in Off.
    private bool StoppedByOff(string what)
    {
        if (host.Mode != UpdateMode.Off || _passIsManual)
        {
            return false;
        }

        host.Journal($"update {what}: stopped, updates are off");

        // Off: arms nothing, and the timer armed before does nothing.
        ArmTimer();
        host.Changed();
        return true;
    }

    // Decided again, not applied as clicked: a game or a clip may have come during the download. True when Setup started.
    private bool ApplyStandingClick()
    {
        if (Click().Kind != ClickKind.Applied)
        {
            return false;
        }

        host.LeaveForSetup();
        return true;
    }

    private void DropClick(UpdateCardNote note)
    {
        if (_clickStands)
        {
            _clickStands = false;
            _card = note;
        }
    }

    private bool Save(string what)
    {
        var saved = store.Save(_state);
        if (!saved)
        {
            host.Journal($"update state: not saved ({what})");
        }

        return saved;
    }
}
