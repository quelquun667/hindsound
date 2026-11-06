using Hindsound.Ui.Window;

namespace Hindsound.Tests.Window;

/// <summary>The small decisions the window only plugs in, with no window.</summary>
public class WindowInputLogicTests
{
    // ---------------------------------------------------------------- wheel

    [Fact]
    public void A_full_wheel_notch_counts_at_once()
    {
        Assert.Equal((1, 0), WheelNotches.Add(0, 120));
        Assert.Equal((-1, 0), WheelNotches.Add(0, -120));
    }

    [Fact]
    public void Small_touchpad_deltas_add_up_to_a_notch()
    {
        var rest = 0;
        var notches = new List<int>();
        for (var i = 0; i < 8; i++)
        {
            (var n, rest) = WheelNotches.Add(rest, 30);
            notches.Add(n);
        }

        Assert.Equal([0, 0, 0, 1, 0, 0, 0, 1], notches);
        Assert.Equal(0, rest);
    }

    [Fact]
    public void What_is_left_over_is_kept_for_the_next_message()
    {
        Assert.Equal((2, 30), WheelNotches.Add(0, 270));
        Assert.Equal((1, 0), WheelNotches.Add(30, 90));
        Assert.Equal((-1, -30), WheelNotches.Add(0, -150));
    }

    [Fact]
    public void The_rest_starts_again_from_zero_when_the_direction_changes()
    {
        Assert.Equal((0, -30), WheelNotches.Add(90, -30));
        Assert.Equal((0, 30), WheelNotches.Add(-90, 30));
        Assert.Equal((-1, 0), WheelNotches.Add(90, -120));
    }

    [Fact]
    public void A_delta_of_nothing_keeps_the_rest()
    {
        Assert.Equal((0, 90), WheelNotches.Add(90, 0));
    }
}
