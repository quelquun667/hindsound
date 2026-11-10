using System.Globalization;
using System.Runtime.Versioning;
using Hindsound.Linux.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>
/// What the X11 shortcuts set up before they open the display, on a real Linux with libX11 but no X server: the wake pipe
/// is closed on exec, and a lost X connection goes to the app's handler, not to Xlib's, which ends the process.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class X11NativeOnLinuxTests
{
    // The open flags of a descriptor, as the kernel shows them in /proc/self/fdinfo: "flags:" then octal digits.
    private static int FlagsOf(int fd)
    {
        foreach (var line in File.ReadAllLines($"/proc/self/fdinfo/{fd}"))
        {
            if (line.StartsWith("flags:", StringComparison.Ordinal))
            {
                return Convert.ToInt32(line["flags:".Length..].Trim(), 8);
            }
        }

        throw new InvalidOperationException("no flags line for descriptor " + fd.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Both_ends_of_the_wake_pipe_are_closed_on_exec()
    {
        Assert.True(X11Native.WakePipe(out var read, out var write));
        try
        {
            Assert.NotEqual(0, FlagsOf(read) & X11Native.CloseOnExec);
            Assert.NotEqual(0, FlagsOf(write) & X11Native.CloseOnExec);
        }
        finally
        {
            X11Native.close(read);
            X11Native.close(write);
        }
    }

    [Fact]
    public void A_lost_connection_goes_to_the_handler_of_the_app()
    {
        X11Native.SurviveConnectionLoss(() => { });

        var installed = X11Native.SwapToDefaultIOErrorHandler();

        Assert.Equal(X11Native.ConnectionLostHandler, installed);
    }
}
