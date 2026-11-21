using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Mac.Interop;
using Hindsound.Mac.Notification;

namespace Hindsound.Mac.Tests.Notification;

/// <summary>
/// The subclass that keeps a click on the notification from activating the app, on an NSResponder made here (no window,
/// no main thread needed). The click itself is proved with the app, in the step of the mac job that shows a notification.
/// </summary>
[Trait("Category", "Mac")]
[SupportedOSPlatform("macos")]
public sealed class NonActivatingClicksNativeTests
{
    private static readonly nint AppKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

    [Fact]
    public void A_view_given_the_clicks_delays_its_ordering_takes_the_first_click_and_keeps_its_class_above()
    {
        Assert.NotEqual((nint)0, AppKit);
        var first = NewResponder();
        var second = NewResponder();
        try
        {
            Assert.True(NonActivatingClicks.Apply(first));
            Assert.True(NonActivatingClicks.Apply(first));
            Assert.True(NonActivatingClicks.Apply(second));

            Assert.Equal(NonActivatingClicks.Prefix + "NSResponder", ClassName(first));
            Assert.Equal(ObjC.Send(first, "class"), ObjC.Send(second, "class"));
            Assert.Equal("NSResponder", ObjC.String(ObjC.Send(ObjC.Send(first, "superclass"), "description")));
            Assert.True(ObjC.SendBool(first, "shouldDelayWindowOrderingForEvent:", 0));
            Assert.True(ObjC.SendBool(first, "acceptsFirstMouse:", 0));
            Assert.True(ObjC.SendBool(first, "respondsToSelector:", ObjC.Selector("mouseDown:")));
        }
        finally
        {
            ObjC.Release(first);
            ObjC.Release(second);
        }
    }

    [Fact]
    public void Nothing_to_give_the_clicks_to_is_said()
    {
        Assert.False(NonActivatingClicks.Apply(0));
    }

    private static nint NewResponder() => ObjC.Send(ObjC.Send(ObjC.Class("NSResponder"), "alloc"), "init");

    private static string? ClassName(nint value) => ObjC.String(ObjC.Send(ObjC.Send(value, "class"), "description"));
}
