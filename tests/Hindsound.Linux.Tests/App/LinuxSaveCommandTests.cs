using System.Runtime.Versioning;
using Hindsound.Linux.Input;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// The command the Hotkeys page shows and copies, as the Linux layer gives it: the one the notice names, and none where the
/// notice falls back to words. A bare "hindsound --save" would find no program: nothing puts an AppImage on the PATH.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class LinuxSaveCommandTests
{
    [Theory]
    [InlineData("/home/you/a&b/Hindsound.AppImage")]
    [InlineData("/home/you/100%/Hindsound.AppImage")]
    [InlineData("/home/you/Apps/Hind$ound.AppImage")]
    [InlineData(null)]
    public void A_program_the_notice_cannot_name_gives_no_command_to_copy(string? program)
    {
        Assert.Null(HotkeyChoice.SaveCommandFor(program));
        Assert.Null(LinuxPlatform.SaveCommand(program));
    }

    [Theory]
    [InlineData("/home/you/Applications/Hindsound.AppImage", "/home/you/Applications/Hindsound.AppImage --save")]
    [InlineData("/home/you/My apps/Hindsound.AppImage", "\"/home/you/My apps/Hindsound.AppImage\" --save")]
    // The .deb: /usr/bin/hindsound is a link, and the program launched is the real file it leads to.
    [InlineData("/usr/lib/hindsound/hindsound", "/usr/lib/hindsound/hindsound --save")]
    public void A_program_the_notice_names_gives_that_command(string program, string command) =>
        Assert.Equal(command, LinuxPlatform.SaveCommand(program));
}
