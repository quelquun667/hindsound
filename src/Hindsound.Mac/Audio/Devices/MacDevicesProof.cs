using System.Diagnostics;
using System.Runtime.Versioning;
using Hindsound.Audio;

namespace Hindsound.Mac.Audio.Devices;

/// <summary>
/// <c>--prove-devices &lt;folder&gt;</c>: the lists of the settings and the watch of the devices on a real macOS, each
/// measure a line on the console, for the mac job of the CI. The process list is read, with this app (named after its
/// bundle) and its parent in it. The devices are listed. Then the app writes "ready-switch" in the folder and waits for the
/// watch to see the default output change, made by the CI with SwitchAudioSource; then writes "ready-play" and waits for
/// "afplay", started by the CI, to be in the list of the applications that play, and to leave it when it ends. Nothing
/// opens a device: no right of TCC is asked.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacDevicesProof
{
    public const string Option = "--prove-devices";

    private const string Player = "afplay";
    private const int SwitchWaitMs = 20_000;
    private const int PlayWaitMs = 20_000;
    private const int PollMs = 100;

    /// <summary>The folder after <c>--prove-devices</c>, or null when the option is not there.</summary>
    public static string? Folder(string[] args)
    {
        var at = Array.IndexOf(args, Option);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    public static int Run(string folder, Action<string> say)
    {
        var failures = new List<string>();
        void Log(string line) => say("devices-proof " + line);
        void Check(bool passed, string what)
        {
            Log($"{what} {(passed ? "ok" : "FAILED")}");
            if (!passed)
            {
                failures.Add(what);
            }
        }

        Directory.CreateDirectory(folder);
        var readySwitch = Path.Combine(folder, "ready-switch");
        var readyPlay = Path.Combine(folder, "ready-play");
        File.Delete(readySwitch);
        File.Delete(readyPlay);

        // The process list: this app, named after its bundle, and its parent.
        var started = Stopwatch.StartNew();
        var processes = new MacProcessSnapshot().Take();
        Log($"processes: {processes.Count} read in {started.ElapsedMilliseconds} ms, responsible process known: {MacProcessSnapshot.KnowsResponsible}");
        var own = processes.FirstOrDefault(process => process.Id == Environment.ProcessId);
        var bundle = MacProcessNames.BundleName(Environment.ProcessPath);
        Log($"own: pid {Environment.ProcessId} path {Environment.ProcessPath} entry {own}");
        Check(own is not null && bundle is not null && own.ExeName == bundle, "processes.own_named_after_bundle");
        var parent = own is null ? null : processes.FirstOrDefault(process => process.Id == own.ParentId);
        Log($"parent: {parent}");
        Check(parent is not null, "processes.parent_found");
        Check(own is not null && SourceAppFinder.FindCaptureTarget(processes, own.ExeName) == own.Id, "processes.capture_target_is_own");

        // The devices.
        var choices = new MacAudioChoices(Log);
        var outputs = choices.Devices(AudioFlow.System);
        var inputs = choices.Devices(AudioFlow.Mic);
        foreach (var device in MacAudioChoices.ReadDevices())
        {
            Log($"device: uid '{device.Uid}' name '{device.Name}' inputs {device.Inputs} outputs {device.Outputs}");
        }

        Log("outputs: " + string.Join(" | ", outputs.Select(device => device.Name)));
        Log("inputs: " + string.Join(" | ", inputs.Select(device => device.Name)));
        Check(outputs.Any(device => device.Name.Contains("BlackHole", StringComparison.Ordinal)), "devices.blackhole_output");
        Check(inputs.Any(device => device.Name.Contains("BlackHole", StringComparison.Ordinal)), "devices.blackhole_input");

        // The watch: the CI changes the default output.
        var before = MacAudioChoices.DefaultDeviceName(AudioFlow.System);
        using var changed = new ManualResetEventSlim();
        var events = 0;
        using (var watcher = new MacDeviceWatcher(Log))
        {
            watcher.DevicesChanged += () =>
            {
                Interlocked.Increment(ref events);
                changed.Set();
            };
            watcher.Start();
            Log($"watch: started, default output '{before}'");
            File.WriteAllText(readySwitch, before ?? string.Empty);
            var waited = Stopwatch.StartNew();
            var seen = changed.Wait(SwitchWaitMs);
            var after = MacAudioChoices.DefaultDeviceName(AudioFlow.System);
            Log($"watch: change {(seen ? "seen" : "not seen")} after {waited.ElapsedMilliseconds} ms, default output '{before}' -> '{after}'");
            Check(seen && after != before, "watch.default_output_change_seen");

            // Late notifications of the same change are folded into the first event or follow it closely.
            Thread.Sleep(1_000);
            Log($"watch: {Volatile.Read(ref events)} event(s) for the change");
        }

        // The applications that play: the CI starts afplay once "ready-play" is there.
        var idle = choices.Apps();
        Log("apps before: " + Names(idle));
        Check(!idle.Any(IsPlayer), "apps.no_player_before");
        File.WriteAllText(readyPlay, string.Empty);
        var clock = Stopwatch.StartNew();
        var playing = WaitFor(() => choices.Apps().Any(IsPlayer), PlayWaitMs);
        var shownAt = clock.ElapsedMilliseconds;
        var clients = MacAudioChoices.ReadClients();
        var playingNow = choices.Apps();
        Log($"apps while playing ({shownAt} ms): {Names(playingNow)}");
        Log("hal clients: " + string.Join(" | ", clients.Select(client => $"{client.ProcessId} {client.BundleId} output={client.PlaysOutput}")));
        Check(playing, "apps.player_listed_while_playing");
        var snapshot = new MacProcessSnapshot().Take();
        var target = SourceAppFinder.FindCaptureTarget(snapshot, Player);
        Check(target is { } pid && clients.Any(client => client.ProcessId == pid && client.PlaysOutput), "apps.player_capture_target_is_the_hal_client");
        var gone = WaitFor(() => !choices.Apps().Any(IsPlayer), PlayWaitMs);
        Log($"apps after the end ({clock.ElapsedMilliseconds} ms): {Names(choices.Apps())}");
        Check(gone, "apps.player_gone_after_the_end");

        Log(failures.Count == 0 ? "result pass" : "result FAILED: " + string.Join(", ", failures));
        return failures.Count == 0 ? 0 : 1;
    }

    private static bool IsPlayer(AudioAppInfo app) => app.ExeName == Player;

    private static string Names(IReadOnlyList<AudioAppInfo> apps) =>
        apps.Count == 0 ? "(none)" : string.Join(" | ", apps.Select(app => $"{app.Name} ({app.ExeName})"));

    private static bool WaitFor(Func<bool> condition, int mostMs)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < mostMs)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(PollMs);
        }

        return condition();
    }
}
