using Hindsound.Ui;

namespace Hindsound.Tests.Ui.Window;

/// <summary>The band calls a memory one second short of the setting full: the menu must not say the opposite.</summary>
public class KeptMenuMarginTests
{
    private static string TextOf5Min(double seconds) =>
        TrayMenu.Items(false, true, false, null, TimeSpan.FromSeconds(seconds)).Single(item => item.Text.StartsWith("Save up to 5 min", StringComparison.Ordinal)).Text;

    [Theory]
    [InlineData(300, "Save up to 5 min")]
    [InlineData(299, "Save up to 5 min")]
    [InlineData(298, "Save up to 5 min (only 4:58)")]
    public void One_second_short_is_not_worth_saying(double seconds, string expected)
    {
        Assert.Equal(expected, TextOf5Min(seconds));
    }
}
