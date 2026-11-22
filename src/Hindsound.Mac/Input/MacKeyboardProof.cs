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
/// of the start is selected again, and they must come back. The result line says what passed.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacKeyboardProof
{
    public const string Option = "--prove-keyboard";

    private const string French = "com.apple.keylayout.French";
    private const string German = "com.apple.keylayout.German";
    private const string Us = "com.apple.keylayout.US";
    private const int StepMs = 100;
    private const int WaitMs = 10_000;
    private const int MostMs = 60_000;

    private static readonly HotkeyGesture KeyA = new(HotkeyGesture.ModControl | HotkeyGesture.ModAlt, 'A');
    private static readonly HotkeyGesture KeyQ = new(HotkeyGesture.ModControl | HotkeyGesture.ModAlt, 'Q');

    public static int Run(Action<string> log)
    {
        var failures = new List<string>();
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
        using var carbon = CarbonHotkeys.TryOpen(log);
        Check(carbon is not null, "hotkey.handler");
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

        loop.Post(() =>
        {
            var before = carbon.Relayouts;
            var status = MacKeyboardLayouts.Select(targetId);
            log($"keyboard proof: select {targetId}: {status}");
            Check(status == 0, "layout.select");
            AfterChange(before, targetId, () =>
            {
                Check(At(target, "after " + targetId), "hotkey.moved");
                Check(targetId != French || (carbon.CodeOf(idA) == 0x0C && carbon.CodeOf(idQ) == 0x00), "hotkey.azerty");
                var back = carbon.Relayouts;
                var again = MacKeyboardLayouts.Select(original);
                log($"keyboard proof: select {original}: {again}");
                Check(again == 0, "layout.select.back");
                AfterChange(back, original, () =>
                {
                    Check(At(MacKeyboardLayouts.ById(original), "after " + original), "hotkey.back");
                    loop.Quit();
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
            if (MacKeyboardLayouts.CurrentId() != original)
            {
                log($"keyboard proof: {original} selected again at the end: {MacKeyboardLayouts.Select(original)}");
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
