using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Tests.Input;

public class HotkeyManagerTests
{
    private sealed class FakeNative : IHotkeyNative
    {
        public HashSet<string> Refused { get; } = [];

        public Dictionary<int, (uint Mods, uint Key)> Registered { get; } = [];

#pragma warning disable CS0067
        public event Action<int>? HotkeyPressed;
#pragma warning restore CS0067

        public void Dispose() => Registered.Clear();

        public bool Register(int id, uint modifiers, uint virtualKey)
        {
            var taken = Refused.Contains($"{modifiers}:{virtualKey}")
                || Registered.Values.Any(v => v.Mods == modifiers && v.Key == virtualKey);
            if (taken) return false;
            Registered[id] = (modifiers, virtualKey);
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
