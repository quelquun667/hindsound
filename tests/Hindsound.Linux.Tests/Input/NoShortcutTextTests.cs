using Hindsound.Linux.App;
using Hindsound.Linux.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>
/// The notice of a desktop with no global shortcut says what to bind. The AppImage puts no <c>hindsound</c> on the PATH, so
/// the notice names the file that was launched, as a command a person can copy into the keyboard settings.
/// </summary>
public sealed class NoShortcutTextTests
{
    [Fact]
    public void The_notice_names_the_AppImage_that_was_launched_followed_by_save()
    {
        Assert.Equal(
            "No global shortcut on this desktop. In your keyboard settings, bind this command to a key: /home/you/Applications/hindsound-0.6.0-linux-x64.AppImage --save",
            HotkeyChoice.NoShortcutTextFor("/home/you/Applications/hindsound-0.6.0-linux-x64.AppImage"));
    }

    [Fact]
    public void A_path_with_a_space_is_written_between_double_quotes()
    {
        Assert.Equal(
            "No global shortcut on this desktop. In your keyboard settings, bind this command to a key: \"/home/you/My apps/Hindsound.AppImage\" --save",
            HotkeyChoice.NoShortcutTextFor("/home/you/My apps/Hindsound.AppImage"));
    }

    [Fact]
    public void Without_a_path_the_notice_says_to_bind_the_full_path_of_the_program()
    {
        const string Generic =
            "No global shortcut on this desktop. In your keyboard settings, bind the full path of the Hindsound program followed by --save to a key.";

        Assert.Equal(Generic, HotkeyChoice.NoShortcutGenericText);
        Assert.Equal(Generic, HotkeyChoice.NoShortcutTextFor(null));
        Assert.Equal(Generic, HotkeyChoice.NoShortcutTextFor(""));
    }

    [Theory]
    [InlineData("hindsound")]
    [InlineData("Applications/Hindsound.AppImage")]
    [InlineData(@"C:\Hindsound\Hindsound.exe")]
    public void A_path_that_is_not_absolute_gets_the_generic_text(string program)
    {
        Assert.Equal(HotkeyChoice.NoShortcutGenericText, HotkeyChoice.NoShortcutTextFor(program));
    }

    [Theory]
    [InlineData("/home/you/R&D/Hindsound.AppImage")]
    [InlineData("/home/you/<apps>/Hindsound.AppImage")]
    [InlineData("/home/you/100%/Hindsound.AppImage")]
    [InlineData("/home/you/$HOME/Hindsound.AppImage")]
    [InlineData("/home/you/say \"hi\"/Hindsound.AppImage")]
    [InlineData("/home/you/back`tick/Hindsound.AppImage")]
    [InlineData("/home/you/back\\slash/Hindsound.AppImage")]
    [InlineData("/home/you/new\nline/Hindsound.AppImage")]
    public void A_path_that_markup_or_a_command_line_would_change_gets_the_generic_text(string program)
    {
        Assert.Equal(HotkeyChoice.NoShortcutGenericText, HotkeyChoice.NoShortcutTextFor(program));
    }

    [Fact]
    public void The_notice_is_the_one_for_the_file_this_copy_was_launched_by()
    {
        Assert.Equal(HotkeyChoice.NoShortcutTextFor(LaunchPaths.Current()), HotkeyChoice.NoShortcutText);
    }

    [Fact]
    public void The_notice_never_asks_for_a_bare_hindsound_command()
    {
        Assert.DoesNotContain("Bind hindsound --save", HotkeyChoice.NoShortcutText, StringComparison.Ordinal);
        Assert.DoesNotContain("Bind hindsound --save", HotkeyChoice.NoShortcutGenericText, StringComparison.Ordinal);
    }
}
