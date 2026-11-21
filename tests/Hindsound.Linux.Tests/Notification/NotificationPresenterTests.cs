using Hindsound.App;
using Hindsound.Ui;
using Hindsound.Unix.Notification;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Linux.Tests.Notification;

/// <summary>The drawn notification in its window: where it goes, how it moves, its clicks and its end, on a fake loop and window.</summary>
public sealed class NotificationPresenterTests
{
    private const uint Accent = NotificationPalette.Lime;
    private static readonly ScreenRect Work = new(0, 25, 1440, 900);

    private readonly FakeLoop _loop = new();
    private readonly FakeSurface _surface = new();
    private readonly FakeText _text = new();
    private readonly List<string> _log = [];

    private NotificationPresenter NewView(bool offscreen = false) =>
        new(_loop, offscreen ? null : () => _surface, () => _text, _log.Add, () => _loop.Now);

    [Fact]
    public void It_goes_against_the_right_edge_below_the_menu_bar_and_comes_out_of_it()
    {
        using var view = NewView();

        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(Work, 96, false, false), Accent);

        Assert.Equal((new ScreenRect(1080, 35, 1440, 99), false), _surface.Opened.Single());
        Assert.Equal((true, (0, 359, 1)), (_surface.Shown[0].Changed, _surface.Shown[0].Slice));
        _loop.Advance(70);

        // The frames come every 16 ms: the last one before 70 ms was at 64.
        Assert.Equal(NotificationMotion.Slice(360, NotificationMotion.Visible(64), false), _surface.Shown[^1].Slice);
        Assert.InRange(_surface.Shown[^1].Slice.DestinationX, 40, 70);
        _loop.Advance(80);
        Assert.Equal((0, 0, 360), _surface.Shown[^1].Slice);
        Assert.Same(view.Showing, view.Picture!.Content);
    }

    [Fact]
    public void On_the_left_edge_it_is_mirrored_and_comes_out_of_that_edge()
    {
        using var view = NewView();

        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(Work, 96, false, LeftEdge: true), Accent);
        _loop.Advance(70);

        Assert.Equal(new ScreenRect(0, 35, 360, 99), _surface.Opened.Single().Bounds);
        Assert.Equal(NotificationMotion.Slice(360, NotificationMotion.Visible(64), true), _surface.Shown[^1].Slice);
        Assert.Equal(0, _surface.Shown[^1].Slice.DestinationX);
        Assert.Equal(0u, _surface.Shown[^1].Corner);
    }

    [Fact]
    public void With_no_area_from_the_app_it_takes_the_main_screen_and_draws_at_its_density()
    {
        _surface.Density = 2;
        using var view = NewView();

        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(default, 0, false, false), Accent);

        Assert.Equal(new ScreenRect(1080, 35, 1440, 99), _surface.Opened.Single().Bounds);
        Assert.Equal((2.0, 720, 128), (view.Scale, view.Picture!.Width, view.Picture.Height));
    }

    [Fact]
    public void It_holds_still_then_leaves_at_2920_ms_and_says_so_once()
    {
        using var view = NewView();
        var finished = 0;
        view.Finished = () => finished++;

        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(Work, 96, false, false), Accent);
        _loop.Advance(300);
        var framesAtHold = view.Frames;
        _loop.Advance(2400);
        Assert.Equal(framesAtHold, view.Frames);
        Assert.Contains(50, _loop.Delays);
        Assert.Equal(0, finished);

        _loop.Advance(300);

        Assert.Equal(1, finished);
        Assert.Null(view.Showing);
        Assert.Null(view.Picture);
        Assert.Equal(1, _surface.Closed);
        Assert.Equal(0, _loop.Pending);
        Assert.True(view.Frames > framesAtHold + 5);
    }

    [Fact]
    public void The_bars_rise_in_the_first_200_ms()
    {
        using var view = NewView();

        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(Work, 96, false, false), Accent);
        var early = view.Picture!.Count(Accent);
        _loop.Advance(250);

        Assert.True(view.Picture!.Count(Accent) > 2 * early, $"{early} pixels of the accent at first");
        Assert.All(_surface.Shown.Take(5), shown => Assert.True(shown.Changed));
    }

    [Fact]
    public void A_click_is_told_once_and_not_through_a_window_clicks_pass_through()
    {
        using var view = NewView();
        var clicked = 0;
        view.Clicked = () => clicked++;
        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(Work, 96, false, false), Accent);

        _surface.PendingClicks = 2;
        _loop.Advance(100);
        Assert.Equal(1, clicked);

        view.Present(NotificationStates.NoDevice(), new NotificationSpot(Work, 96, ClickThrough: true, false), Accent);
        _surface.PendingClicks = 1;
        _loop.Advance(100);

        Assert.Equal(1, clicked);
        Assert.True(_surface.Opened[^1].ClickThrough);
    }

    [Fact]
    public void Replaced_while_up_it_does_not_come_out_again_and_its_hold_starts_over()
    {
        using var view = NewView();
        var finished = 0;
        view.Finished = () => finished++;
        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(Work, 96, false, false), Accent);
        _loop.Advance(1000);
        var timers = _loop.Pending;

        view.Present(NotificationStates.NoDevice(), new NotificationSpot(Work, 96, false, false), Accent);

        Assert.Equal((true, (0, 0, 360)), (_surface.Shown[^1].Changed, _surface.Shown[^1].Slice));
        Assert.Equal(timers, _loop.Pending);
        _loop.Advance(2700);
        Assert.Equal(0, finished);
        _loop.Advance(100);
        Assert.Equal(1, finished);
    }

    [Fact]
    public void Held_open_it_stays_until_let_go()
    {
        using var view = NewView();
        var finished = 0;
        view.Finished = () => finished++;
        view.HoldOpen = true;
        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(Work, 96, false, false), Accent);

        _loop.Advance(10_000);
        Assert.Equal(0, finished);
        Assert.Equal((0, 0, 360), _surface.Shown[^1].Slice);

        view.HoldOpen = false;
        _loop.Advance(400);
        Assert.Equal(1, finished);
    }

    [Fact]
    public void Offscreen_nothing_is_opened_and_the_times_are_kept()
    {
        using var view = NewView(offscreen: true);
        var finished = 0;
        view.Finished = () => finished++;

        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(Work, 144, false, false), Accent);
        Assert.Equal(1.5, view.Scale);
        _loop.Advance(NotificationMotion.TotalMs + 20);

        Assert.Empty(_surface.Opened);
        Assert.Equal(1, finished);
    }

    [Fact]
    public void Without_its_text_nothing_shows_the_log_says_why_and_the_app_hears_it_ended()
    {
        using var view = new NotificationPresenter(_loop, () => _surface, () => throw new TextUnavailableException("Selawik-Regular.ttf is missing"), _log.Add, () => _loop.Now);
        var finished = 0;
        view.Finished = () => finished++;

        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(Work, 96, false, false), Accent);
        _loop.Advance(1);

        Assert.Null(view.Showing);
        Assert.Empty(_surface.Opened);
        Assert.Equal(1, finished);
        Assert.Contains("notification: no text: Selawik-Regular.ttf is missing", _log);
    }

    [Fact]
    public void Disposed_it_closes_its_window_and_frees_its_text()
    {
        var view = NewView();
        view.Present(NotificationPictureTests.Saved(), new NotificationSpot(Work, 96, false, false), Accent);

        view.Dispose();
        _loop.Advance(5000);

        Assert.True(_surface.Disposed);
        Assert.True(_text.Disposed);
        Assert.Equal(1, _surface.Closed);
        Assert.Equal(0, _loop.Pending);
    }
}
