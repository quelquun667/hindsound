using System.Runtime.Versioning;
using Hindsound.Input;
using Hindsound.Mac.App;

namespace Hindsound.Mac.Input;

/// <summary>
/// <c>--prove-keyboard</c>: the shortcuts follow the keyboard layout, on the main thread of a real macOS session, each
/// line on the console, for the mac job of the CI. The French, German and US layouts are read by their ids, whichever is
/// selected, and the keys of A, Q, Z, W, M, Y and 1 checked; two Carbon shortcuts that trade keys between QWERTY and
/// AZERTY (Control+Option+A and Control+Option+Q) are registered in the current layout; the French layout is then
/// selected, and the notification of macOS must move both, registered, to the keys that type A and Q there; the layout
/// of the start is selected again, and they must come back.
/// <para>
/// A second part shows the shortcut that no key can hold: L and P are registered, another registration of the process holds
/// the key the Dvorak layout gives P, the Dvorak layout is selected, and P, whose old key L takes, must stay in waiting (not
/// lost, told to the Core, said in the log); it must be back on its key when the layout of the start is selected again.
/// </para>
/// <para>
/// The callback of a change must have come on the main thread, the observer must be added before the layout is read, and the
/// layouts the proof enabled are disabled at the end. The result line says what passed.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacKeyboardProof
{
    public const string Option = "--prove-keyboard";

    private const string French = "com.apple.keylayout.French";
    private const string German = "com.apple.keylayout.German";
    private const string Us = "com.apple.keylayout.US";
    private const string Dvorak = "com.apple.keylayout.Dvorak";
    private const int StepMs = 100;
    private const int WaitMs = 10_000;
    private const int MostMs = 60_000;

    private const uint ControlOption = HotkeyGesture.ModControl | HotkeyGesture.ModAlt;

    private static readonly HotkeyGesture KeyA = new(ControlOption, 'A');
    private static readonly HotkeyGesture KeyQ = new(ControlOption, 'Q');
    private static readonly HotkeyGesture KeyL = new(ControlOption, 'L');
    private static readonly HotkeyGesture KeyP = new(ControlOption, 'P');

    public static int Run(Action<string> log)
    {
        var failures = new List<string>();
        var journal = new List<string>();
        var initiallyEnabled = new Dictionary<string, bool>();
        nint? outsider = null;
        void Journal(string line)
        {
            journal.Add(line);
            log(line);
        }

        // Selects a layout, noting first whether the user had it enabled: what the proof enables, it disables at the end.
        int Pick(string id)
        {
            if (!initiallyEnabled.ContainsKey(id))
            {
                initiallyEnabled[id] = MacKeyboardLayouts.IsEnabled(id);
            }

            return MacKeyboardLayouts.Select(id);
        }

        void Check(bool passed, string what)
        {
            log($"keyboard proof: {what} {(passed ? "ok" : "FAILED")}");
            if (!passed)
            {
                failures.Add(what);
            }
        }

        // The layouts by their ids.
        var french = MacKeyboardLayouts.ById(French);
        var german = MacKeyboardLayouts.ById(German);
        var us = MacKeyboardLayouts.ById(Us);
        foreach (var layout in new[] { french, german, us })
        {
            log("keyboard proof: " + (layout is null ? "a layout is missing" : $"{layout.Name}: {Codes(layout)}"));
        }

        Check(french is not null && french.CodeOf('A') == 0x0C && french.CodeOf('Q') == 0x00 && french.CodeOf('Z') == 0x0D
            && french.CodeOf('W') == 0x06 && french.CodeOf('M') == 0x29 && french.CodeOf('1') == 0x12, "layout.french");
        Check(german is not null && german.CodeOf('Y') == 0x06 && german.CodeOf('Z') == 0x10 && german.CodeOf('A') == 0x00, "layout.german");
        Check(us is not null && us.CodeOf('A') == 0x00 && us.CodeOf('Q') == 0x0C && us.CodeOf('M') == 0x2E && us.Count == 36, "layout.us");

        var original = MacKeyboardLayouts.CurrentId();
        log("keyboard proof: layout at the start " + (original ?? "unknown"));
        var targetId = original == French ? German : French;
        var target = targetId == French ? french : german;

        var shell = new MacShell(log);
        using var loop = (MacAppLoop)shell.NewLoop();
        using var carbon = CarbonHotkeys.TryOpen(Journal);
        Check(carbon is not null, "hotkey.handler");
        var watching = journal.FindIndex(line => line.Contains("watching the keyboard layout"));
        var reading = journal.FindIndex(line => line.StartsWith("hotkeys: keyboard layout ", StringComparison.Ordinal));
        Check(watching >= 0 && reading > watching, "open.observer-first");
        if (carbon is null || target is null || original is null)
        {
            return Result(log, failures.Append("setup").ToList());
        }

        using var keys = new KeyByKeyHotkeys(carbon);
        var bound = keys.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, KeyA), new HotkeyRequest(HotkeyAction.Save15s, KeyQ)]);
        var idA = carbon.Ids.FirstOrDefault(id => keys.TryGetAction(id, out var action) && action == HotkeyAction.SaveDefault);
        var idQ = carbon.Ids.FirstOrDefault(id => keys.TryGetAction(id, out var action) && action == HotkeyAction.Save15s);
        bool At(MacKeyLayout? layout, string what)
        {
            var a = MacKeyCodes.Resolve('A', layout)?.Code;
            var q = MacKeyCodes.Resolve('Q', layout)?.Code;
            log($"keyboard proof: {what}: {KeyA} on 0x{carbon.CodeOf(idA):X2} (0x{a:X2} wanted), {KeyQ} on 0x{carbon.CodeOf(idQ):X2} (0x{q:X2} wanted), layout {carbon.Layout?.Name}");
            return carbon.Ids.Count == 2 && carbon.CodeOf(idA) == a && carbon.CodeOf(idQ) == q;
        }

        Check(bound.All(b => b.State == HotkeyBindingState.Bound) && At(carbon.Layout, "registered"), "hotkey.register");
        Check(carbon.Layout?.Name == original, "layout.current");

        // Waits on the loop for a change of layout taken by the shortcuts, then goes on, whether it came or not.
        void AfterChange(int before, string id, Action then)
        {
            var waited = 0;
            void Look()
            {
                if ((carbon.Relayouts > before && carbon.Layout?.Name == id) || waited >= WaitMs)
                {
                    log($"keyboard proof: {carbon.Relayouts - before} change(s) taken after {waited} ms");
                    then();
                    return;
                }

                waited += StepMs;
                loop.After(StepMs, Look);
            }

            Look();
        }

        // The shortcut that no key can hold: L moves onto the key P leaves, and P onto a key another registration holds.
        void Waiting()
        {
            var dvorak = MacKeyboardLayouts.ById(Dvorak);
            var start = carbon.Layout;
            var oldL = MacKeyCodes.Resolve('L', start)?.Code;
            var oldP = MacKeyCodes.Resolve('P', start)?.Code;
            var newL = MacKeyCodes.Resolve('L', dvorak)?.Code;
            var newP = MacKeyCodes.Resolve('P', dvorak)?.Code;
            log($"keyboard proof: at the start L is on 0x{oldL:X2} and P on 0x{oldP:X2}; on Dvorak L is on 0x{newL:X2} and P on 0x{newP:X2}");
            var shape = dvorak is not null && original != Dvorak && newL == oldP && newP is not null && newP != oldL && newP != oldP;
            Check(shape, "pending.setup");
            if (!shape)
            {
                loop.Quit();
                return;
            }

            keys.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, null), new HotkeyRequest(HotkeyAction.Save15s, null)]);
            var set = keys.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, KeyL), new HotkeyRequest(HotkeyAction.Save15s, KeyP)]);
            var idL = carbon.Ids.FirstOrDefault(id => keys.TryGetAction(id, out var action) && action == HotkeyAction.SaveDefault);
            var idP = carbon.Ids.FirstOrDefault(id => keys.TryGetAction(id, out var action) && action == HotkeyAction.Save15s);
            Check(set.All(b => b.State == HotkeyBindingState.Bound) && carbon.CodeOf(idL) == oldL && carbon.CodeOf(idP) == oldP && idL < idP, "pending.register");
            outsider = CarbonHotkeys.HoldOutside(newP!.Value, ControlOption);
            Check(outsider is not null, "pending.outside");
            var told = 0;
            keys.BindingsChanged += () => told++;
            var before = carbon.Relayouts;
            var status = Pick(Dvorak);
            log($"keyboard proof: select {Dvorak}: {status}");
            Check(status == 0, "layout.select.dvorak");
            AfterChange(before, Dvorak, () =>
            {
                var movedL = keys.Current(HotkeyAction.SaveDefault);
                var waitingP = keys.Current(HotkeyAction.Save15s);
                log($"keyboard proof: L on 0x{carbon.CodeOf(idL):X2} {movedL.State}, P waits for 0x{carbon.CodeOf(idP):X2} held={carbon.IsHeld(idP)} {waitingP.State}, the Core was told {told} time(s)");
                Check(carbon.IsHeld(idL) && carbon.CodeOf(idL) == newL && movedL.State == HotkeyBindingState.Bound, "pending.moved");
                Check(carbon.Ids.Contains(idP) && !carbon.IsHeld(idP) && carbon.CodeOf(idP) == newP, "pending.kept");
                Check(waitingP.State == HotkeyBindingState.Refused && told > 0, "pending.core");
                Check(journal.Any(line => line.Contains($"hotkeys: {idP} waiting")) && !journal.Any(line => line.Contains("lost")), "pending.journal");
                var back = carbon.Relayouts;
                var again = Pick(original);
                log($"keyboard proof: select {original}: {again}");
                Check(again == 0, "layout.select.back.pending");
                AfterChange(back, original, () =>
                {
                    var returned = keys.Current(HotkeyAction.Save15s);
                    log($"keyboard proof: P on 0x{carbon.CodeOf(idP):X2} held={carbon.IsHeld(idP)} {returned.State}, L on 0x{carbon.CodeOf(idL):X2}");
                    Check(carbon.IsHeld(idP) && carbon.CodeOf(idP) == oldP && carbon.CodeOf(idL) == oldL && returned.State == HotkeyBindingState.Bound, "pending.back");
                    loop.Quit();
                });
            });
        }

        loop.Post(() =>
        {
            var before = carbon.Relayouts;
            var status = Pick(targetId);
            log($"keyboard proof: select {targetId}: {status}");
            Check(status == 0, "layout.select");
            AfterChange(before, targetId, () =>
            {
                Check(At(target, "after " + targetId), "hotkey.moved");
                Check(targetId != French || (carbon.CodeOf(idA) == 0x0C && carbon.CodeOf(idQ) == 0x00), "hotkey.azerty");
                log($"keyboard proof: the change came on the main thread: {carbon.ChangeOnMainThread?.ToString() ?? "no change"}");
                Check(carbon.ChangeOnMainThread == true, "layout.thread");
                var back = carbon.Relayouts;
                var again = Pick(original);
                log($"keyboard proof: select {original}: {again}");
                Check(again == 0, "layout.select.back");
                AfterChange(back, original, () =>
                {
                    Check(At(MacKeyboardLayouts.ById(original), "after " + original), "hotkey.back");
                    Waiting();
                });
            });
        });
        loop.After(MostMs, () =>
        {
            log("keyboard proof: time is up");
            failures.Add("time");
            loop.Quit();
        });

        try
        {
            loop.Run();
        }
        finally
        {
            if (outsider is { } held)
            {
                CarbonHotkeys.ReleaseOutside(held);
            }

            if (MacKeyboardLayouts.CurrentId() != original)
            {
                log($"keyboard proof: {original} selected again at the end: {Pick(original)}");
            }

            // What the proof enabled it disables, so that a real Mac keeps the input sources it had.
            foreach (var id in initiallyEnabled.Where(kv => !kv.Value).Select(kv => kv.Key).ToList())
            {
                var disabled = MacKeyboardLayouts.Disable(id);
                log($"keyboard proof: {id} was not enabled: disabled again, status {disabled}");
                Check(disabled == 0 && !MacKeyboardLayouts.IsEnabled(id), "layout.cleanup");
            }
        }

        return Result(log, failures);
    }

    private static int Result(Action<string> log, List<string> failures)
    {
        log("keyboard proof: result " + (failures.Count == 0 ? "pass" : "fail: " + string.Join(", ", failures)));
        return failures.Count == 0 ? 0 : 3;
    }

    private static string Codes(MacKeyLayout layout) =>
        string.Join(' ', "AQZWMY1".Select(key => $"{key}=0x{layout.CodeOf(key):X2}"));
}
