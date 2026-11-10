using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>
/// The keysym table of <see cref="HotkeyKeysyms"/> against libX11's own: each name the portal is offered is the name X11
/// knows, with the same value. Needs libX11.so.6, not an X display.
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed partial class KeysymNamesOnLinuxTests
{
    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XStringToKeysym(string name);

    [Fact]
    public void Every_keysym_name_and_value_is_the_one_of_libx11()
    {
        foreach (var vk in HotkeyKeysyms.KnownVirtualKeys())
        {
            var name = HotkeyKeysyms.KeysymName(vk)!;
            Assert.Equal((nuint)HotkeyKeysyms.ToKeysym(vk)!.Value, XStringToKeysym(name));
        }

        Assert.Equal((nuint)HotkeyKeysyms.KeysymNumLock, XStringToKeysym("Num_Lock"));
        Assert.Equal(XStringToKeysym("Page_Up"), XStringToKeysym("Prior"));
    }
}
