using System.Diagnostics;
using System.Runtime.Versioning;
using Hindsound.App;
using Hindsound.Input;
using Hindsound.Mac.Input;
using Hindsound.Mac.Tray;
using Hindsound.Ui;

namespace Hindsound.Mac.App;

/// <summary>
/// <c>--prove-loop</c>: the parts of the running app that only a real macOS session shows, in one run of the bundle on the
/// main thread, each line in the log, for the mac job of the CI. The loop waits on the events of NSApplication; the icon of
/// the menu bar is added, its menu filled through its delegate and read back from AppKit, a line chosen; a Carbon shortcut
/// is registered, its press sent to the application target, then a real press posted as a keyboard would; the instance
/// waits for <c>hindsound --save</c> from a second launch. The result line says what passed; the real press is reported
/// apart, since it depends on a right the runner may not give.
/// </summary>
internal static class MacLoopProof
{
    /// <summary>The log line the CI waits for before it launches <c>hindsound --save</c>.</summary>
    public const string ReadyLine = "loop proof: ready, waiting for --save";

    private const int MostMs = 90_000;
    private const int RealPressAfterMs = 500;
    private const int RealPressWaitMs = 5_000;

    // Control+Option+F9: a combination macOS takes from an app (the probe saw it registered), and no runner uses.
    private static readonly HotkeyGesture Key = new(HotkeyGesture.ModControl | HotkeyGesture.ModAlt, 0x78);

    [SupportedOSPlatform("macos")]
    public static int Run(IInstanceChannel instance, Action<string> log)
    {
        var failures = new List<string>();
        void Check(bool passed, string what)
        {
            log($"loop proof: {what} {(passed ? "ok" : "FAILED")}");
            if (!passed)
            {
                failures.Add(what);
            }
        }

        var shell = new MacShell(log);
        using var loop = (MacAppLoop)shell.NewLoop();
        Check(loop.Pump.AppEvents, "loop.nsapplication");

        // The icon and its menu.
        var expected = TrayMenu.Items(paused: false, hasLastClip: true, closingAfterClip: false);
        var chosen = new List<TrayCommand>();
        using var tray = (MacStatusItem)shell.NewTray(loop);
        var gestures = 0;
        tray.Gesture = gesture =>
        {
            gestures++;
            if (gesture == TrayGesture.Menu)
            {
                tray.ShowMenu(expected);
            }
        };
        tray.Chosen = command =>
        {
            log("loop proof: tray chosen " + command);
            chosen.Add(command);
        };
        Check(tray.Add(TrayState.Active, 0x22C55E, "Hindsound"), "tray.add");
        tray.Opening();
        var facts = tray.Read();
        log($"loop proof: tray present={facts.Present} wired={facts.Wired} template={facts.Template} points={facts.Points} pixels={facts.PixelsWide} tip={facts.Tip} gestures={gestures}");
        for (var i = 0; i < facts.Lines.Count; i++)
        {
            var line = facts.Lines[i];
            log($"loop proof: menu {i}: \"{line.Title}\" {line.Command} enabled={line.Enabled} separator={line.Separator} on={line.On} targeted={line.Targeted}");
        }

        Check(facts is { Present: true, Wired: true, Template: true, Points: TemplateIcon.Points, PixelsWide: TemplateIcon.Pixels, Tip: "Hindsound" } && gestures == 1, "tray.icon");
        Check(SameLines(expected, facts.Lines), "tray.menu");
        tray.Choose(IndexOf(expected, TrayCommand.Settings));
        tray.Opening();
        tray.Close();

        // The shortcut: registered, then its press sent to the application target.
        using var carbon = CarbonHotkeys.TryOpen(log);
        Check(carbon is not null, "hotkey.handler");
        using var keys = carbon is null ? null : new KeyByKeyHotkeys(carbon);
        var synthetic = 0;
        var real = 0;
        var posting = false;
        var saved = false;
        var realOver = false;
        var clock = Stopwatch.StartNew();
        void MaybeQuit()
        {
            if (saved && realOver)
            {
                loop.Quit();
            }
        }

        if (keys is not null)
        {
            keys.Pressed += action => loop.Post(() =>
            {
                log($"loop proof: hotkey pressed {action} at {clock.ElapsedMilliseconds} ms ({(posting ? "real" : "sent")})");
                if (posting)
                {
                    real++;
                    realOver = true;
                    MaybeQuit();
                }
                else
                {
                    synthetic++;
                }
            });
            var bound = keys.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, Key)]);
            log($"loop proof: hotkey {Key} {bound[0].State}");
            Check(bound[0].State == HotkeyBindingState.Bound && carbon!.Ids.Count == 1, "hotkey.register");
            Check(carbon!.Ids.Count == 1 && carbon.SendPressed(carbon.Ids.First()), "hotkey.sent");
        }

        instance.Listen(loop, message =>
        {
            log("loop proof: instance message " + message);
            saved |= message == InstanceMessage.Save;
            MaybeQuit();
        });

        // A real press, through the window server, once the loop runs.
        loop.After(RealPressAfterMs, () =>
        {
            posting = true;
            log($"loop proof: may post keys={CarbonHotkeys.MayPostKeys()}, posted={keys is not null && CarbonHotkeys.PostKey(Key.Modifiers, Key.VirtualKey)}");
            loop.After(RealPressWaitMs, () =>
            {
                realOver = true;
                MaybeQuit();
            });
        });
        loop.After(MostMs, () =>
        {
            log("loop proof: time is up");
            loop.Quit();
        });

        log(ReadyLine);
        loop.Run();

        Check(synthetic == 1, "hotkey.pressed");
        Check(chosen.SequenceEqual([TrayCommand.Settings, TrayCommand.None]), "tray.chosen");
        Check(saved, "instance.save");
        log($"loop proof: hotkey.real pressed={real} (reported apart, not in the result)");
        log($"loop proof: events handed to sendEvent: {loop.Pump.EventsSent}");
        log("loop proof: result " + (failures.Count == 0 ? "pass" : "fail: " + string.Join(", ", failures)));
        return failures.Count == 0 ? 0 : 3;
    }

    /// <summary>Whether AppKit holds the lines of the menu as the app gave them.</summary>
    internal static bool SameLines(IReadOnlyList<TrayMenuItem> expected, IReadOnlyList<MenuLine> lines) =>
        expected.Count == lines.Count && expected.Zip(lines).All(pair => pair.First.Command == TrayCommand.None
            ? pair.Second.Separator
            : !pair.Second.Separator && pair.Second.Title == pair.First.Text && pair.Second.Command == pair.First.Command
                && pair.Second.Enabled == pair.First.Enabled && pair.Second.On == (pair.First.Checked == true) && pair.Second.Targeted);

    private static int IndexOf(IReadOnlyList<TrayMenuItem> items, TrayCommand command)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Command == command)
            {
                return i;
            }
        }

        throw new ArgumentException("no line " + command, nameof(items));
    }
}
