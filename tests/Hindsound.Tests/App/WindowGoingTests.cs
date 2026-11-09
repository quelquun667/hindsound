using Hindsound.App;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>What the app does with the route of a gesture, on a window that is open and on one that is not. The table itself is WindowRoutesTests.</summary>
public class WindowGoingTests
{
    private sealed class FakeScreen(Section on, List<string> calls) : IWindowScreen
    {
        public Section Section { get; private set; } = on;

        public void ShowSection(Section section)
        {
            Section = section;
            calls.Add("show " + section);
        }

        public void ShowUpdates() => calls.Add("updates");

        public void ShowUpdateCard() => calls.Add("card");

        public void SelectClip(string path) => calls.Add("clip " + path);
    }

    private sealed class Harness
    {
        public readonly List<string> Calls = [];
        public readonly List<string> Journal = [];
        public bool MayOpen = true;
        public FakeScreen? Open;
        public WindowGoing Going;

        public Harness(Section? openOn = null)
        {
            Open = openOn is { } on ? new FakeScreen(on, Calls) : null;
            Going = new WindowGoing(
                () => MayOpen,
                () => Open,
                () => Calls.Add("bring back"),
                target =>
                {
                    Calls.Add("create " + target);
                    Open = new FakeScreen(target, Calls);
                },
                Journal.Add);
        }
    }

    public static TheoryData<int, int> Cases()
    {
        var data = new TheoryData<int, int>();
        foreach (var gesture in Enum.GetValues<WindowGesture>().Select(g => (int)g))
        {
            foreach (var openOn in new[] { -1, (int)Section.Now, (int)Section.Audio, (int)Section.General })
            {
                data.Add(gesture, openOn);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_gesture_does_what_its_route_says(int gestureNumber, int openOnNumber)
    {
        var gesture = (WindowGesture)gestureNumber;
        Section? openOn = openOnNumber < 0 ? null : (Section)openOnNumber;
        var h = new Harness(openOn);
        h.Going.SettingsPageSeen(Section.Durations);
        var route = WindowRoutes.Of(gesture, openOn, Section.Durations);

        h.Going.Go(gesture);

        Assert.Single(h.Journal);
        Assert.StartsWith($"window gesture {gesture}: {route.Action}", h.Journal[0], StringComparison.Ordinal);
        var target = route.Section ?? Section.Now;
        Assert.Equal(target, h.Open!.Section);
        Assert.Equal(openOn is null ? $"create {target}" : "bring back", h.Calls[0]);
        Assert.Equal(openOn is null ? 1 : 2, h.Calls.TakeWhile(c => c != "updates" && c != "card").Count());
        Assert.Equal(route.Action == WindowAction.ShowUpdates, h.Calls.Contains("updates"));
        Assert.Equal(route.Action == WindowAction.ShowUpdateCard, h.Calls.Contains("card"));
    }

    [Fact]
    public void An_open_window_is_moved_and_not_made_again()
    {
        var h = new Harness(Section.Audio);

        h.Going.Go(WindowGesture.SettingsMenu);

        Assert.Equal(["bring back", "show Hotkeys"], h.Calls);
        Assert.Equal(Section.Hotkeys, h.Open!.Section);
    }

    [Fact]
    public void The_settings_page_seen_last_is_where_the_menu_goes_back_to()
    {
        var h = new Harness();
        h.Going.SettingsPageSeen(Section.Export);

        h.Going.Go(WindowGesture.SettingsMenu);

        Assert.Equal(["create Export"], h.Calls);
    }

    [Fact]
    public void Nothing_opens_while_the_app_is_leaving()
    {
        var h = new Harness(Section.Audio) { MayOpen = false };

        h.Going.Go(WindowGesture.OpenApp);
        h.Going.Open(Section.Now);

        Assert.Empty(h.Calls);
        Assert.Equal(Section.Audio, h.Open!.Section);
    }

    [Fact]
    public void A_click_on_a_notification_takes_the_route_of_its_kind()
    {
        var h = new Harness();

        h.Going.Click(NotificationStates.MicLost());
        h.Going.Click(null);

        Assert.Equal(2, h.Journal.Count);
        Assert.Contains($"window gesture {WindowRoutes.ClickOn(NotificationStates.MicLost())}:", h.Journal[0], StringComparison.Ordinal);
        Assert.Contains($"window gesture {WindowRoutes.ClickOn(null)}:", h.Journal[1], StringComparison.Ordinal);
    }
}
