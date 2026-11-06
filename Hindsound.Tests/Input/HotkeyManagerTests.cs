using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Tests.Input;

public class HotkeyManagerTests
{
    private sealed class FakeNative : IHotkeyNative
    {
        public HashSet<string> Refused { get; } = [];

        public Dictionary<int, (uint Mods, uint Key)> Registered { get; } = [];

        public event Action<int>? HotkeyPressed;

        /// <summary>Raised from inside Register once the shortcut is stored, as a press can arrive that early.</summary>
        public bool PressDuringRegister { get; set; }

        /// <summary>What a WM_HOTKEY does: the native layer raises the event with the id.</summary>
        public void Press(int id) => HotkeyPressed?.Invoke(id);

        public void Dispose() => Registered.Clear();

        // Like RegisterHotKey: a combination already registered is refused, even for the same caller.
        public bool Register(int id, uint modifiers, uint virtualKey)
        {
            var taken = Refused.Contains($"{modifiers}:{virtualKey}")
                || Registered.Values.Any(v => v.Mods == modifiers && v.Key == virtualKey);
            if (taken) return false;
            Registered[id] = (modifiers, virtualKey);
            if (PressDuringRegister) Press(id);
            return true;
        }

        public void Unregister(int id) => Registered.Remove(id);
    }

    private static HotkeySettings Keys(string? def = "Ctrl+Shift+F10", string? s15 = null, string? pause = null) =>
        new() { SaveDefault = def, Save15s = s15, TogglePause = pause };

    [Fact]
    public void Apply_RegistersEveryConfiguredShortcut()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);

        var results = m.Apply(Keys(s15: "Ctrl+Alt+1", pause: "F9"));

        Assert.Equal(6, results.Count);
        Assert.Equal(HotkeyStatus.Registered, results[(int)HotkeyAction.SaveDefault].Status);
        Assert.Equal(HotkeyStatus.Registered, results[(int)HotkeyAction.Save15s].Status);
        Assert.Equal(HotkeyStatus.NotSet, results[(int)HotkeyAction.Save1m].Status);
        Assert.Equal(HotkeyStatus.Registered, results[(int)HotkeyAction.TogglePause].Status);
        Assert.Equal(3, native.Registered.Count);
    }

    [Fact]
    public void Refusal_IsAReadableResult_NotAnException()
    {
        var native = new FakeNative();
        native.Refused.Add("6:121"); // Ctrl+Shift+F10
        using var m = new HotkeyManager(native);

        var result = m.Apply(Keys()).First(r => r.Action == HotkeyAction.SaveDefault);

        Assert.Equal(HotkeyStatus.InUse, result.Status);
        Assert.Equal("Hotkey already in use by another app", result.Message);
        Assert.False(result.Ok);
        Assert.Empty(native.Registered);
    }

    [Fact]
    public void ChangingAShortcut_ReleasesTheOldOne()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(Keys());

        var result = m.Apply(Keys(def: "Ctrl+Alt+H")).First(r => r.Action == HotkeyAction.SaveDefault);

        Assert.Equal(HotkeyStatus.Registered, result.Status);
        Assert.Equal([(0x3u, 0x48u)], native.Registered.Values);
    }

    [Fact]
    public void FailedChange_KeepsTheOldShortcut()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(Keys());
        native.Refused.Add("3:72"); // Ctrl+Alt+H

        var result = m.Apply(Keys(def: "Ctrl+Alt+H")).First(r => r.Action == HotkeyAction.SaveDefault);

        Assert.Equal(HotkeyStatus.InUse, result.Status);
        Assert.Equal([(0x6u, 0x79u)], native.Registered.Values);
        var pressed = new List<HotkeyAction>();
        m.Pressed += pressed.Add;
        Assert.True(m.Dispatch(native.Registered.Keys.Single()));
        Assert.Equal([HotkeyAction.SaveDefault], pressed);
    }

    [Fact]
    public void InvalidText_IsReported_AndKeepsTheOldShortcut()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(Keys());

        var result = m.Apply(Keys(def: "Ctrl+Nope")).First(r => r.Action == HotkeyAction.SaveDefault);

        Assert.Equal(HotkeyStatus.Invalid, result.Status);
        Assert.Single(native.Registered);
    }

    [Fact]
    public void RemovingAShortcut_ReleasesIt()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(Keys(s15: "F9"));

        m.Apply(Keys());

        Assert.Single(native.Registered);
    }

    [Fact]
    public void SameSettingsTwice_DoesNotRegisterAgain()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(Keys());

        var again = m.Apply(Keys());

        Assert.All(again.Where(r => r.Action == HotkeyAction.SaveDefault), r => Assert.Equal(HotkeyStatus.Registered, r.Status));
        Assert.Single(native.Registered);
    }

    [Fact]
    public void SuspendAndResume_ReleaseThenRestore()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(Keys(s15: "F9"));

        m.Suspend();
        Assert.Empty(native.Registered);
        var during = m.Apply(Keys(def: "Ctrl+Alt+H", s15: "F9"));
        Assert.Equal(HotkeyStatus.Suspended, during[(int)HotkeyAction.SaveDefault].Status);
        Assert.Empty(native.Registered);

        var after = m.Resume();

        Assert.Equal(HotkeyStatus.Registered, after[(int)HotkeyAction.SaveDefault].Status);
        Assert.Equal(2, native.Registered.Count);
        Assert.Contains((0x3u, 0x48u), native.Registered.Values);
    }

    [Fact]
    public void Dispatch_RaisesPressedForTheRightAction_AndIgnoresOthers()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(Keys(s15: "F9"));
        var pressed = new List<HotkeyAction>();
        m.Pressed += pressed.Add;
        var f9 = native.Registered.Single(kv => kv.Value.Key == 0x78).Key;

        Assert.True(m.Dispatch(f9));
        Assert.False(m.Dispatch(999));
        Assert.False(m.Dispatch(-1));

        Assert.Equal([HotkeyAction.Save15s], pressed);
    }

    private static HotkeySettings KeysOf(string? def, string? s15, string? s1m = null) =>
        new() { SaveDefault = def, Save15s = s15, Save1m = s1m };

    private static HotkeyAction ActionOfKey(HotkeyManager m, FakeNative native, uint key)
    {
        var pressed = new List<HotkeyAction>();
        void Collect(HotkeyAction a) => pressed.Add(a);
        m.Pressed += Collect;
        m.Dispatch(native.Registered.Single(kv => kv.Value.Key == key).Key);
        m.Pressed -= Collect;
        return pressed.Single();
    }

    [Fact]
    public void SwappingTwoShortcuts_Works_AndEachActionFollowsItsNewKey()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(KeysOf("F9", "F8"));

        var results = m.Apply(KeysOf("F8", "F9"));

        Assert.All(results.Take(2), r => Assert.Equal(HotkeyStatus.Registered, r.Status));
        Assert.Equal(2, native.Registered.Count);
        Assert.Equal(HotkeyAction.SaveDefault, ActionOfKey(m, native, 0x77)); // F8
        Assert.Equal(HotkeyAction.Save15s, ActionOfKey(m, native, 0x78)); // F9
    }

    [Fact]
    public void ShiftingShortcuts_WorksWhateverTheOrder()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(KeysOf("F9", "F8", "F7"));

        // Each action wants the key the next one holds: nothing can move first without waiting for another.
        var results = m.Apply(KeysOf("F8", "F7", "F6"));

        Assert.All(results.Take(3), r => Assert.Equal(HotkeyStatus.Registered, r.Status));
        Assert.Equal(3, native.Registered.Count);
        Assert.Equal(HotkeyAction.SaveDefault, ActionOfKey(m, native, 0x77)); // F8
        Assert.Equal(HotkeyAction.Save15s, ActionOfKey(m, native, 0x76)); // F7
        Assert.Equal(HotkeyAction.Save1m, ActionOfKey(m, native, 0x75)); // F6
    }

    [Fact]
    public void ARingOfThreeShortcuts_IsExchangedInOneStep()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(KeysOf("F9", "F8", "F7"));

        var results = m.Apply(KeysOf("F8", "F7", "F9"));

        Assert.All(results.Take(3), r => Assert.Equal(HotkeyStatus.Registered, r.Status));
        Assert.Equal(HotkeyAction.Save1m, ActionOfKey(m, native, 0x78)); // F9
    }

    [Fact]
    public void TheSameKeyTwice_IsReportedAsADuplicate_NotAsAnotherApp()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);

        var results = m.Apply(KeysOf("F9", "F9"));

        Assert.Equal(HotkeyStatus.Registered, results[(int)HotkeyAction.SaveDefault].Status);
        var duplicate = results[(int)HotkeyAction.Save15s];
        Assert.Equal(HotkeyStatus.Duplicate, duplicate.Status);
        Assert.Equal(HotkeyResult.DuplicateMessage, duplicate.Message);
        Assert.False(duplicate.Ok);
        Assert.Single(native.Registered);
    }

    [Fact]
    public void AnExchange_ThatCannotComplete_LosesNoWorkingShortcut()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(KeysOf("F9", "F8"));
        native.Refused.Add("0:118"); // F7, held by another app

        // SaveDefault waits for F8 held by Save15s, which in turn wants the refused F7: nothing can move.
        var results = m.Apply(KeysOf("F8", "F7"));

        Assert.Equal(HotkeyStatus.InUse, results[(int)HotkeyAction.SaveDefault].Status);
        Assert.Equal(HotkeyStatus.InUse, results[(int)HotkeyAction.Save15s].Status);
        Assert.Equal(HotkeyAction.SaveDefault, ActionOfKey(m, native, 0x78)); // still F9
        Assert.Equal(HotkeyAction.Save15s, ActionOfKey(m, native, 0x77)); // still F8
    }

    [Fact]
    public async Task APressRaisedByTheNativeLayer_ReachesPressed()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(Keys(s15: "F9"));
        var received = new TaskCompletionSource<HotkeyAction>();
        m.Pressed += received.SetResult;

        native.Press(native.Registered.Single(kv => kv.Value.Key == 0x78).Key); // WM_HOTKEY for F9

        Assert.Equal(HotkeyAction.Save15s, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task APressRaisedWhileRegistering_FindsTheActionAlready()
    {
        // The press can arrive before Register returns: the id must be known by then.
        var native = new FakeNative { PressDuringRegister = true };
        using var m = new HotkeyManager(native);
        var received = new TaskCompletionSource<HotkeyAction>();
        m.Pressed += action => received.TrySetResult(action);

        m.Apply(Keys());

        Assert.Equal(HotkeyAction.SaveDefault, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task APressOfAnUnknownIdOrAfterDispose_RaisesNothing()
    {
        var native = new FakeNative();
        var m = new HotkeyManager(native);
        m.Apply(Keys());
        var id = native.Registered.Keys.Single();
        var received = new List<HotkeyAction>();
        m.Pressed += received.Add;

        native.Press(999);
        m.Dispose();
        native.Press(id);
        await Task.Delay(100);

        Assert.Empty(received);
    }

    [Fact]
    public void AHandlerThatThrows_DoesNotPropagate_AndTheNextOneStillGetsThePress()
    {
        var native = new FakeNative();
        using var m = new HotkeyManager(native);
        m.Apply(Keys());
        var received = new List<HotkeyAction>();
        m.Pressed += _ => throw new InvalidOperationException("a broken subscriber");
        m.Pressed += received.Add;

        var handled = m.Dispatch(native.Registered.Keys.Single());

        Assert.True(handled);
        Assert.Equal([HotkeyAction.SaveDefault], received);
    }

    [Fact]
    public void Dispose_ReleasesEverything()
    {
        var native = new FakeNative();
        var m = new HotkeyManager(native);
        m.Apply(Keys(s15: "F9", pause: "F8"));

        m.Dispose();
        m.Dispose();

        Assert.Empty(native.Registered);
        Assert.Throws<ObjectDisposedException>(() => m.Apply(Keys()));
    }

    // Real RegisterHotKey, with a combination nobody uses. Excluded from the normal run.
    [Fact]
    [Trait("Category", "Device")]
    public void Real_RegistersAndReleasesAnUnlikelyShortcut()
    {
        var settings = Keys(def: "Ctrl+Alt+Shift+F24");
        using (var first = new HotkeyManager())
        {
            Assert.Equal(HotkeyStatus.Registered, first.Apply(settings)[(int)HotkeyAction.SaveDefault].Status);

            using var second = new HotkeyManager();
            var clash = second.Apply(settings)[(int)HotkeyAction.SaveDefault];
            Assert.Equal(HotkeyStatus.InUse, clash.Status);
            Assert.Equal("Hotkey already in use by another app", clash.Message);
        }

        using var third = new HotkeyManager();
        Assert.Equal(HotkeyStatus.Registered, third.Apply(settings)[(int)HotkeyAction.SaveDefault].Status);
    }
}
