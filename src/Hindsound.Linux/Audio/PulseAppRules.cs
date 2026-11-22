using System.Text;
using Hindsound.Audio;

namespace Hindsound.Linux.Audio;

/// <summary>A playback stream of the sound server (a "sink input"), with what it says of the program that plays it.</summary>
/// <param name="Index">The stream's index on the server.</param>
/// <param name="Sink">The index of the output it plays on.</param>
/// <param name="ProcessId">application.process.id, if the stream gives one.</param>
/// <param name="Binary">application.process.binary: the program's file name, if the stream gives it.</param>
/// <param name="AppName">application.name: the name the program gives itself, if any.</param>
internal sealed record PulsePlaybackStream(uint Index, uint Sink, int? ProcessId, string? Binary, string? AppName, bool Corked = false);

/// <summary>One playback stream recorded alone, on the output it plays on: a stream moved to another output is another one.</summary>
internal readonly record struct PulseMonitorKey(uint SinkInput, uint Sink);

/// <summary>
/// Which playback streams belong to the application being recorded, and what the list of applications offers. A stream says
/// which process plays it; the application is a process and its children, read from /proc. Nothing here touches the server or
/// the system, so it is tested everywhere.
/// </summary>
internal static class PulseAppRules
{
    /// <summary>PA_SUBSCRIPTION_MASK_SINK_INPUT.</summary>
    public const uint SubscribeSinkInputs = 0x0004;

    /// <summary>The events a capture of one application follows: its streams, and the outputs whose monitors it records.</summary>
    public const uint SubscriptionMask = PulseFormats.SubscribeSinks | SubscribeSinkInputs;

    /// <summary>
    /// Not corked: the capture mixes what comes, from its own clock. Never moved by the server: a stream that moves to another
    /// output is recorded again from that output's monitor.
    /// </summary>
    public const uint RecordFlags = PulseFormats.AdjustLatency | PulseFormats.DontMove;

    /// <summary>How long a process name is in /proc: the kernel keeps the first 15 bytes of the program's file name.</summary>
    public const int KernelNameBytes = 15;

    /// <summary>
    /// How long a recording that failed waits before it is tried again, doubled at each try, and how many times it is: a
    /// stream the server moves because its output went away (a headset unplugged) may refuse a recording for a moment.
    /// </summary>
    public const int RetryMilliseconds = 1000;

    public const int MaxRetries = 4;

    /// <summary>
    /// How long the capture waits before it looks again at a stream of the application that plays on no output it knows, doubled
    /// each time, <see cref="MaxRetries"/> times at most until the server says something changed. PipeWire moves a stream whose
    /// output went away by unlinking it, then linking it to another output, and tells nothing of either: a look between the two
    /// sees the stream on no output, and no event comes when it is linked again (pipewire-pulse 1.0.5 sends none for a link).
    /// The 250 ms, 500 ms, 1 s, 2 s and 4 s add up to less than 8 s, and a stream that stays nowhere costs five looks.
    /// </summary>
    public const int PlaceMilliseconds = 250;

    /// <summary>
    /// How long the server must have had a recording ready for its tries to count from zero again when it ends. Twice the
    /// 15 seconds all the waits between tries add up to (1 + 2 + 4 + 8): a recording the server ends again and again within
    /// the time of its own tries still runs out of them, while a stream that plays for hours (a call) and is ended a few times
    /// in those hours is recorded again each time. Half a minute without sound from it costs little next to such a stream.
    /// </summary>
    public const int SteadyMilliseconds = 30_000;

    private const uint FacilityMask = 0x000F;
    private const uint FacilitySink = 0x0000;
    private const uint FacilitySinkInput = 0x0002;
    private const uint TypeMask = 0x0030;
    private const uint TypeChange = 0x0010;

    /// <summary>
    /// Whether an event may change the streams to record: a playback stream that comes, goes or changes (a move to another
    /// output is a change), or an output that comes or goes.
    /// </summary>
    public static bool IsStreamChange(uint eventType) => (eventType & FacilityMask) switch
    {
        FacilitySinkInput => true,
        FacilitySink => (eventType & TypeMask) != TypeChange,
        _ => false,
    };

    /// <summary>A program's file name as /proc shows it: its first 15 bytes, never a character cut in two.</summary>
    public static string? KernelName(string? binary)
    {
        if (string.IsNullOrEmpty(binary))
        {
            return null;
        }

        if (Encoding.UTF8.GetByteCount(binary) <= KernelNameBytes)
        {
            return binary;
        }

        var bytes = Encoding.UTF8.GetBytes(binary);
        var length = KernelNameBytes;

        // The kernel cuts bytes; a cut character is left out here, so the name compares as text.
        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
        {
            length--;
        }

        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    /// <summary>
    /// The process of this system that plays a stream, or null. Its process id, when /proc has it under the same name as the
    /// stream's program. An application in a sandbox (Flatpak, Snap) may give the id it has in its own namespace, which here
    /// is another process or none: it is then found by its program's name, as the settings find an application. When nothing
    /// has that name, the id stands as it is, if /proc has it: a program started through a link has another name in /proc.
    /// </summary>
    public static int? OwnerOf(PulsePlaybackStream stream, IReadOnlyDictionary<int, ProcessEntry> byId, IReadOnlyList<ProcessEntry> processes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var name = KernelName(stream.Binary);
        var entry = stream.ProcessId is > 0 && byId.TryGetValue(stream.ProcessId.Value, out var found) ? found : null;
        if (entry is not null && (name is null || string.Equals(entry.ExeName, name, StringComparison.Ordinal)))
        {
            return entry.Id;
        }

        var byName = name is null ? null : SourceAppFinder.FindCaptureTarget(processes, name);
        return byName ?? entry?.Id;
    }

    /// <summary>A process and all its descendants, by the parents /proc gives. The process itself is in it even when it is gone.</summary>
    public static HashSet<int> TreeOf(IReadOnlyList<ProcessEntry> processes, int root)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var children = new Dictionary<int, List<int>>();
        foreach (var process in processes)
        {
            if (process.ParentId == process.Id)
            {
                continue;
            }

            if (!children.TryGetValue(process.ParentId, out var list))
            {
                children[process.ParentId] = list = [];
            }

            list.Add(process.Id);
        }

        var tree = new HashSet<int> { root };
        var queue = new Queue<int>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            if (!children.TryGetValue(queue.Dequeue(), out var list))
            {
                continue;
            }

            foreach (var child in list)
            {
                // A set: ids reused into a loop are walked once.
                if (tree.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return tree;
    }

    /// <summary>
    /// The streams of the application: those played by <paramref name="root"/> or one of its descendants, never by this app,
    /// even when it was started from the application (a terminal, say).
    /// </summary>
    public static IReadOnlyList<PulsePlaybackStream> StreamsOf(IReadOnlyList<PulsePlaybackStream> streams, IReadOnlyList<ProcessEntry> processes, int root, int ownProcessId)
    {
        ArgumentNullException.ThrowIfNull(streams);
        var byId = ById(processes);
        var tree = TreeOf(processes, root);
        tree.Remove(ownProcessId);
        return [.. streams.Where(stream => OwnerOf(stream, byId, processes) is { } owner && tree.Contains(owner))];
    }

    /// <summary>
    /// The sessions <see cref="AudioAppList.Build"/> makes the list from: for each stream, the process to offer and the name the
    /// stream gives, when that name holds the process's name (else null, and the list shows the process's name). A process of a program made of several, whose name in /proc is not the program's (a browser's tab, say), is
    /// offered as its highest ancestor that bears the program's name, so that choosing it records the whole program.
    /// </summary>
    public static IReadOnlyList<(int ProcessId, string? DisplayName)> Sessions(IReadOnlyList<PulsePlaybackStream> streams, IReadOnlyList<ProcessEntry> processes)
    {
        ArgumentNullException.ThrowIfNull(streams);
        var byId = ById(processes);
        var sessions = new List<(int ProcessId, string? DisplayName)>();
        foreach (var stream in streams)
        {
            var owner = OwnerOf(stream, byId, processes) ?? 0;
            var program = ProgramOf(owner, KernelName(stream.Binary), byId);
            sessions.Add((program, NameFor(stream.AppName, program, byId)));
        }

        return sessions;
    }

    // The name a stream gives itself is kept only when it holds the process's name, whatever the case: "Firefox" for firefox,
    // "Google Chrome" for chrome. "WEBRTC VoiceEngine" for Discord or "Chromium" for an Electron app say nothing of the
    // program, so the list falls back on the process's name.
    private static string? NameFor(string? appName, int program, Dictionary<int, ProcessEntry> byId)
    {
        if (string.IsNullOrWhiteSpace(appName) || !byId.TryGetValue(program, out var entry) || string.IsNullOrEmpty(entry.ExeName))
        {
            return null;
        }

        return appName.Contains(entry.ExeName, StringComparison.OrdinalIgnoreCase) ? appName : null;
    }

    /// <summary>
    /// What to change: the streams to start recording (those of the application not recorded yet, on an output that has a
    /// monitor), and those to stop (recorded, but no longer the application's, or on another output now). A stream whose
    /// recording failed stays in <paramref name="current"/> until it ends, so it is not opened again and again.
    /// </summary>
    public static (IReadOnlyList<(PulseMonitorKey Key, string MonitorSource)> Open, IReadOnlyList<PulseMonitorKey> Close) Changes(
        IEnumerable<PulseMonitorKey> current,
        IReadOnlyList<PulsePlaybackStream> wanted,
        IReadOnlyDictionary<uint, string> monitors)
    {
        var have = current.ToHashSet();
        var want = new Dictionary<PulseMonitorKey, string>();
        foreach (var stream in wanted)
        {
            if (monitors.TryGetValue(stream.Sink, out var monitor) && !string.IsNullOrEmpty(monitor))
            {
                want[new PulseMonitorKey(stream.Index, stream.Sink)] = monitor;
            }
        }

        var open = want.Where(pair => !have.Contains(pair.Key)).Select(pair => (pair.Key, pair.Value)).ToList();
        var close = have.Where(key => !want.ContainsKey(key)).ToList();
        return (open, close);
    }

    private static Dictionary<int, ProcessEntry> ById(IReadOnlyList<ProcessEntry> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var byId = new Dictionary<int, ProcessEntry>();
        foreach (var process in processes)
        {
            byId[process.Id] = process;
        }

        return byId;
    }

    /// <summary>
    /// How many tries a recording that ended counts, for its next try: those it had, or none again when the server had it ready
    /// for at least <paramref name="steady"/>. A recording that ends after it held so long has not failed again and again: a
    /// stream that plays for hours (a call) may be ended by the server a few times in those hours, and is recorded again each
    /// time. One that ends quickly, or never started, keeps its count, so quick failures still stop after the last try.
    /// </summary>
    /// <param name="tries">The tries it had before this one.</param>
    /// <param name="held">How long the server had it ready before it ended; null if it never was.</param>
    /// <param name="steady">How long it must have held for its count to start again.</param>
    public static int TriesAfterEnd(int tries, TimeSpan? held, TimeSpan steady) =>
        held is { } time && time >= steady ? 0 : tries;

    /// <summary>
    /// Whether a recording is abandoned: it ended, with all its tries had, or it never started in all of them. Its stream
    /// plays and is not recorded, which the window says, until the stream ends or moves (<see cref="PulseAppCaptureSource.SoundLost"/>).
    /// </summary>
    public static bool Abandoned(bool dead, int tries) => dead && tries >= MaxRetries;

    /// <summary>
    /// Whether one of the <paramref name="recordings"/> is abandoned (<see cref="Abandoned"/>) while its stream is one of the
    /// <paramref name="playing"/> streams of the application, on the same output, and not paused: a stream that ended or
    /// moved no longer plays unrecorded, and a paused one plays nothing, so neither is a sound lost.
    /// </summary>
    public static bool AnyAbandoned(IEnumerable<(PulseMonitorKey Key, bool Dead, int Tries)> recordings, IReadOnlyList<PulsePlaybackStream> playing)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(playing);
        return recordings.Any(recording => Abandoned(recording.Dead, recording.Tries)
            && playing.Any(stream => !stream.Corked && stream.Index == recording.Key.SinkInput && stream.Sink == recording.Key.Sink));
    }

    /// <summary>Whether one of the application's streams plays on no output among <paramref name="monitors"/>: it is being moved.</summary>
    public static bool AnyUnplaced(IReadOnlyList<PulsePlaybackStream> wanted, IReadOnlyDictionary<uint, string> monitors) =>
        wanted.Any(stream => !monitors.TryGetValue(stream.Sink, out var monitor) || string.IsNullOrEmpty(monitor));

    /// <summary>
    /// How long to wait before the next look at a stream on no output, after <paramref name="looks"/> such looks since the
    /// server last said something; null once they are all had (<see cref="PlaceMilliseconds"/>).
    /// </summary>
    public static TimeSpan? PlaceWait(int looks) =>
        looks < 0 || looks > MaxRetries ? null : TimeSpan.FromMilliseconds(PlaceMilliseconds << looks);

    // The highest ancestor of a process, itself included, that bears the program's name; the process itself if none does.
    private static int ProgramOf(int owner, string? name, Dictionary<int, ProcessEntry> byId)
    {
        if (name is null || owner <= 0)
        {
            return owner;
        }

        var found = owner;
        var seen = new HashSet<int>();
        for (var id = owner; seen.Add(id) && byId.TryGetValue(id, out var entry); id = entry.ParentId)
        {
            if (string.Equals(entry.ExeName, name, StringComparison.Ordinal))
            {
                found = id;
            }
        }

        return found;
    }
}
