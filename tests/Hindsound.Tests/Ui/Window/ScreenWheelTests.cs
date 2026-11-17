using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The wheel moves what is under the mouse: the page, or the list of clips. Over the side bar of the sections, the band or the foot, it moves nothing.</summary>
public sealed class ScreenWheelTests : IDisposable
{
    private sealed class FakeNow : INowPanel
    {
        public Hindsound.Audio.WaveFacts WaveFacts => default;
    }

    private readonly ScreenRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private SettingsScreen Screen => _rig.Screen;

    /// <summary>The middle of the side bar, its last column, the band and the foot.</summary>
    public static TheoryData<float, float> OffThePage => new()
    {
        { SettingsScreen.NavWidth / 2, ScreenRig.PageY },
        { SettingsScreen.NavWidth - 1, ScreenRig.PageY },
        { ScreenRig.PageX, SettingsScreen.BandHeight / 2 },
        { ScreenRig.PageX, SettingsScreen.ViewBottom + SettingsScreen.FootHeight / 2 },
    };

    private List<string> Painted()
    {
        _rig.Canvas.Drawn.Clear();
        Screen.Paint(_rig.Canvas);
        return [.. _rig.Canvas.Drawn];
    }

    private void ShowLongLibrary()
    {
        Screen.UpdateStatus(ScreenRig.Capturing());
        Screen.SetNow(new FakeNow());
        Screen.SetLibrary(new FakeLibrary().With(2000));
        Screen.ShowSection(Section.Library);
    }

    [Theory]
    [MemberData(nameof(OffThePage))]
    public void The_wheel_off_the_page_leaves_the_page_where_it_was(float x, float y)
    {
        Screen.ShowSection(Section.General);
        Assert.True(Screen.MaxScroll > 0);

        Screen.Wheel(-3, x, y);

        Assert.Equal(0, Screen.Scroll);
    }

    [Fact]
    public void The_wheel_on_the_first_column_of_the_page_scrolls_it()
    {
        Screen.ShowSection(Section.General);

        Screen.Wheel(-3, SettingsScreen.NavWidth, ScreenRig.PageY);

        Assert.True(Screen.Scroll > 0);
    }

    [Theory]
    [MemberData(nameof(OffThePage))]
    public void The_wheel_off_the_page_leaves_the_list_of_clips_where_it_was(float x, float y)
    {
        ShowLongLibrary();
        var before = Painted();
        Assert.Contains("clip0000", before);

        Screen.Wheel(-5, x, y);

        Assert.Equal(before, Painted());
    }

    [Fact]
    public void The_wheel_on_the_list_of_clips_scrolls_it()
    {
        ShowLongLibrary();
        Assert.Contains("clip0000", Painted());

        _rig.Wheel(-5);

        Assert.DoesNotContain("clip0000", Painted());
    }
}
