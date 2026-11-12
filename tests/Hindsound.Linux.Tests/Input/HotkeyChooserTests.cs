using Hindsound.Input;
using Hindsound.Linux.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>The choice of a shortcut layer, and the time before it is known. Pure logic over fake layers: runs everywhere.</summary>
public sealed class HotkeyChooserTests
{
    private static readonly HotkeyGesture CtrlShiftF10 = new(HotkeyGesture.ModControl | HotkeyGesture.ModShift, 0x79);

    private sealed class FakeLayer : IHotkeyNative
    {
        public List<HotkeyRequest> Asked { get; } = [];

        public bool Disposed { get; private set; }

        public event Action<HotkeyAction>? Pressed;

        public event Action? BindingsChanged
        {
            add { }
            remove { }
        }

        public bool Pending => false;

        public IReadOnlyList<HotkeyBinding> Bind(IReadOnlyList<HotkeyRequest> requests)
        {
            Asked.AddRange(requests);
            return requests.Select(r => new HotkeyBinding(r.Action, r.Proposed, r.Proposed is null ? HotkeyBindingState.Unbound : HotkeyBindingState.Bound)).ToList();
        }

        public HotkeyGesture? BoundKey(HotkeyAction action) => Asked.LastOrDefault(r => r.Action == action).Proposed;

        public void Press(HotkeyAction action) => Pressed?.Invoke(action);

        public void Dispose() => Disposed = true;
    }

    private sealed class Run
    {
        public List<string> Log { get; } = [];

        public int Announced { get; set; }

        public int X11Asked { get; set; }

        public int PortalAsked { get; set; }

        public TaskCompletionSource<IHotkeyNative?> Portal { get; } = new();

        public HotkeyChooser Make(bool wayland, bool xDisplay, IHotkeyNative? x11) => new(
            Log.Add,
            wayland,
            xDisplay,
            () =>
            {
                X11Asked++;
                return x11;
            },
            () =>
            {
                PortalAsked++;
                return Portal.Task;
            },
            () => Announced++);
    }

    [Fact]
    public void An_x11_session_grabs_with_x11_at_once_and_never_asks_the_portal()
    {
        var run = new Run();
        var x11 = new FakeLayer();
        using var chooser = run.Make(wayland: false, xDisplay: true, x11);

        Assert.Same(x11, chooser.Layer);
        Assert.False(chooser.Pending);
        Assert.Equal(0, run.PortalAsked);
        Assert.Equal(HotkeyBindingState.Bound, chooser.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, CtrlShiftF10)])[0].State);
    }

    [Fact]
    public void Wayland_asks_the_portal_only_even_with_an_x_display()
    {
        var run = new Run();
        using var chooser = run.Make(wayland: true, xDisplay: true, new FakeLayer());

        Assert.Equal(0, run.X11Asked);
        Assert.Equal(1, run.PortalAsked);
    }

    [Fact]
    public void An_x11_session_whose_display_fails_asks_the_portal()
    {
        var run = new Run();
        using var chooser = run.Make(wayland: false, xDisplay: true, x11: null);

        Assert.Equal(1, run.X11Asked);
        Assert.Equal(1, run.PortalAsked);
    }

    [Fact]
    public async Task Keys_asked_before_the_portal_answers_are_pending_then_handed_to_it()
    {
        var run = new Run();
        using var chooser = run.Make(wayland: true, xDisplay: false, null);
        var changed = 0;
        chooser.BindingsChanged += () => changed++;

        var answers = chooser.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, CtrlShiftF10), new HotkeyRequest(HotkeyAction.CopyLast, null)]);
        Assert.Equal(new[] { HotkeyBindingState.Pending, HotkeyBindingState.Unbound }, answers.Select(a => a.State));
        Assert.True(chooser.Pending);
        Assert.Equal(HotkeyBindingState.Pending, chooser.Current(HotkeyAction.SaveDefault).State);

        var portal = new FakeLayer();
        run.Portal.SetResult(portal);
        await chooser.Decided;

        Assert.Equal(new[] { new HotkeyRequest(HotkeyAction.SaveDefault, CtrlShiftF10) }, portal.Asked);
        Assert.Equal(1, changed);
        Assert.False(chooser.Pending);
        Assert.Equal(CtrlShiftF10, chooser.BoundKey(HotkeyAction.SaveDefault));
        Assert.Equal(0, run.Announced);
    }

    [Fact]
    public void A_press_of_the_layer_reaches_the_app()
    {
        var run = new Run();
        var x11 = new FakeLayer();
        using var chooser = run.Make(wayland: false, xDisplay: true, x11);
        var pressed = new List<HotkeyAction>();
        chooser.Pressed += pressed.Add;

        x11.Press(HotkeyAction.TogglePause);

        Assert.Equal(new[] { HotkeyAction.TogglePause }, pressed);
    }

    [Fact]
    public async Task Without_any_layer_each_key_is_unavailable_the_log_says_how_to_save_and_the_user_is_told()
    {
        var run = new Run();
        using var chooser = run.Make(wayland: true, xDisplay: false, null);
        chooser.Bind([new HotkeyRequest(HotkeyAction.SaveDefault, CtrlShiftF10)]);

        run.Portal.SetResult(null);
        await chooser.Decided;

        Assert.Equal(1, run.Announced);
        Assert.Contains("hotkeys: " + HotkeyChoice.NoShortcutText, run.Log);
        Assert.False(chooser.Pending);
        Assert.Equal(HotkeyBindingState.Unavailable, chooser.Current(HotkeyAction.SaveDefault).State);
        var answers = chooser.Bind([new HotkeyRequest(HotkeyAction.Save15s, CtrlShiftF10), new HotkeyRequest(HotkeyAction.SaveDefault, null)]);
        Assert.Equal(new[] { HotkeyBindingState.Unavailable, HotkeyBindingState.Unbound }, answers.Select(a => a.State));
    }

    [Fact]
    public async Task A_portal_that_throws_counts_as_none()
    {
        var run = new Run();
        using var chooser = run.Make(wayland: true, xDisplay: false, null);

        run.Portal.SetException(new InvalidOperationException());
        await chooser.Decided;

        Assert.Equal(1, run.Announced);
        Assert.Contains("hotkeys: the portal failed: InvalidOperationException", run.Log);
    }

    [Fact]
    public async Task A_layer_that_comes_after_the_end_is_released_and_nothing_is_announced()
    {
        var run = new Run();
        var chooser = run.Make(wayland: true, xDisplay: false, null);
        chooser.Dispose();

        var portal = new FakeLayer();
        run.Portal.SetResult(portal);
        await chooser.Decided;

        Assert.True(portal.Disposed);
        Assert.Null(chooser.Layer);
        Assert.Equal(0, run.Announced);
    }

    [Fact]
    public async Task The_manager_over_a_desktop_without_shortcuts_reports_them_unavailable()
    {
        var run = new Run();
        run.Portal.SetResult(null);
        var chooser = run.Make(wayland: true, xDisplay: false, null);
        await chooser.Decided;
        using var manager = new HotkeyManager(chooser);

        var results = manager.Apply(new Hindsound.Settings.HotkeySettings { SaveDefault = "Ctrl+Shift+F10" });

        Assert.Equal(HotkeyStatus.Unavailable, results.Single(r => r.Action == HotkeyAction.SaveDefault).Status);
    }
}
