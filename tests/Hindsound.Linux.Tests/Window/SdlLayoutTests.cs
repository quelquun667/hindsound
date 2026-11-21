using System.Runtime.InteropServices;
using Hindsound.Linux.Window;
using Hindsound.Unix.Window;

namespace Hindsound.Linux.Tests.Window;

/// <summary>
/// The structures SDL fills, at the offsets of SDL3/SDL_events.h, SDL_surface.h and SDL_rect.h (release 3.2.0) on a 64-bit
/// system. A field one byte off would read the wrong key or the wrong coordinate with no error, so each is pinned here.
/// </summary>
/// <remarks>Only the layouts are read, nothing calls SDL: these run on any 64-bit system.</remarks>
#pragma warning disable CA1416 // The structures are declared in a class marked for Linux, but reading their layout calls nothing.
public sealed unsafe class SdlLayoutTests
{
    private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    [Fact]
    public void The_event_is_the_union_of_128_bytes_and_every_member_starts_with_its_type()
    {
        Assert.Equal(128, sizeof(SdlNative.Event));
        Assert.Equal(16, sizeof(SdlNative.CommonEvent));
        Assert.Equal(8, Offset<SdlNative.CommonEvent>(nameof(SdlNative.CommonEvent.Timestamp)));

        var e = default(SdlNative.Event);
        e.Type = 0x1234;
        Assert.Equal(0x1234u, e.Window.Type);
        Assert.Equal(0x1234u, e.Key.Type);
        Assert.Equal(0x1234u, e.Motion.Type);
        Assert.Equal(0x1234u, e.User.Type);
    }

    [Fact]
    public void Window_and_display_events_carry_an_id_then_two_values()
    {
        Assert.Equal(16, Offset<SdlNative.WindowEvent>(nameof(SdlNative.WindowEvent.WindowId)));
        Assert.Equal(20, Offset<SdlNative.WindowEvent>(nameof(SdlNative.WindowEvent.Data1)));
        Assert.Equal(24, Offset<SdlNative.WindowEvent>(nameof(SdlNative.WindowEvent.Data2)));
        Assert.Equal(16, Offset<SdlNative.DisplayEvent>(nameof(SdlNative.DisplayEvent.DisplayId)));
        Assert.Equal(20, Offset<SdlNative.DisplayEvent>(nameof(SdlNative.DisplayEvent.Data1)));
        Assert.Equal(24, Offset<SdlNative.DisplayEvent>(nameof(SdlNative.DisplayEvent.Data2)));
    }

    [Fact]
    public void The_keyboard_event_has_the_layout_of_sdl_3_2()
    {
        Assert.Equal(16, Offset<SdlNative.KeyboardEvent>(nameof(SdlNative.KeyboardEvent.WindowId)));
        Assert.Equal(20, Offset<SdlNative.KeyboardEvent>(nameof(SdlNative.KeyboardEvent.Which)));
        Assert.Equal(24, Offset<SdlNative.KeyboardEvent>(nameof(SdlNative.KeyboardEvent.Scancode)));
        Assert.Equal(28, Offset<SdlNative.KeyboardEvent>(nameof(SdlNative.KeyboardEvent.Key)));
        Assert.Equal(32, Offset<SdlNative.KeyboardEvent>(nameof(SdlNative.KeyboardEvent.Mod)));
        Assert.Equal(34, Offset<SdlNative.KeyboardEvent>(nameof(SdlNative.KeyboardEvent.Raw)));
        Assert.Equal(36, Offset<SdlNative.KeyboardEvent>(nameof(SdlNative.KeyboardEvent.DownByte)));
        Assert.Equal(37, Offset<SdlNative.KeyboardEvent>(nameof(SdlNative.KeyboardEvent.RepeatByte)));

        var e = default(SdlNative.Event);
        ((byte*)&e)[36] = 1;
        Assert.True(e.Key.Down);
        Assert.False(e.Key.Repeat);
    }

    [Fact]
    public void Text_events_point_to_their_text_after_the_window_id()
    {
        Assert.Equal(16, Offset<SdlNative.TextInputEvent>(nameof(SdlNative.TextInputEvent.WindowId)));
        Assert.Equal(24, Offset<SdlNative.TextInputEvent>(nameof(SdlNative.TextInputEvent.Text)));
        Assert.Equal(24, Offset<SdlNative.TextEditingEvent>(nameof(SdlNative.TextEditingEvent.Text)));
        Assert.Equal(32, Offset<SdlNative.TextEditingEvent>(nameof(SdlNative.TextEditingEvent.Start)));
        Assert.Equal(36, Offset<SdlNative.TextEditingEvent>(nameof(SdlNative.TextEditingEvent.Length)));

        var utf8 = "été 音"u8.ToArray().Append((byte)0).ToArray();
        fixed (byte* text = utf8)
        {
            var e = default(SdlNative.Event);
            *(byte**)((byte*)&e + 24) = text;
            Assert.Equal("été 音", e.Text.TextString);
        }

        Assert.Equal("", default(SdlNative.TextInputEvent).TextString);
    }

    [Fact]
    public void Mouse_events_have_the_layout_of_sdl_3_2()
    {
        Assert.Equal(24, Offset<SdlNative.MouseMotionEvent>(nameof(SdlNative.MouseMotionEvent.State)));
        Assert.Equal(28, Offset<SdlNative.MouseMotionEvent>(nameof(SdlNative.MouseMotionEvent.X)));
        Assert.Equal(32, Offset<SdlNative.MouseMotionEvent>(nameof(SdlNative.MouseMotionEvent.Y)));
        Assert.Equal(36, Offset<SdlNative.MouseMotionEvent>(nameof(SdlNative.MouseMotionEvent.XRel)));
        Assert.Equal(40, Offset<SdlNative.MouseMotionEvent>(nameof(SdlNative.MouseMotionEvent.YRel)));

        Assert.Equal(20, Offset<SdlNative.MouseButtonEvent>(nameof(SdlNative.MouseButtonEvent.Which)));
        Assert.Equal(24, Offset<SdlNative.MouseButtonEvent>(nameof(SdlNative.MouseButtonEvent.Button)));
        Assert.Equal(25, Offset<SdlNative.MouseButtonEvent>(nameof(SdlNative.MouseButtonEvent.DownByte)));
        Assert.Equal(26, Offset<SdlNative.MouseButtonEvent>(nameof(SdlNative.MouseButtonEvent.Clicks)));
        Assert.Equal(28, Offset<SdlNative.MouseButtonEvent>(nameof(SdlNative.MouseButtonEvent.X)));
        Assert.Equal(32, Offset<SdlNative.MouseButtonEvent>(nameof(SdlNative.MouseButtonEvent.Y)));

        Assert.Equal(24, Offset<SdlNative.MouseWheelEvent>(nameof(SdlNative.MouseWheelEvent.X)));
        Assert.Equal(28, Offset<SdlNative.MouseWheelEvent>(nameof(SdlNative.MouseWheelEvent.Y)));
        Assert.Equal(32, Offset<SdlNative.MouseWheelEvent>(nameof(SdlNative.MouseWheelEvent.Direction)));
        Assert.Equal(36, Offset<SdlNative.MouseWheelEvent>(nameof(SdlNative.MouseWheelEvent.MouseX)));
        Assert.Equal(40, Offset<SdlNative.MouseWheelEvent>(nameof(SdlNative.MouseWheelEvent.MouseY)));
    }

    [Fact]
    public void The_user_event_and_the_surface_put_their_pointers_on_8_bytes()
    {
        Assert.Equal(20, Offset<SdlNative.UserEvent>(nameof(SdlNative.UserEvent.Code)));
        Assert.Equal(24, Offset<SdlNative.UserEvent>(nameof(SdlNative.UserEvent.Data1)));
        Assert.Equal(32, Offset<SdlNative.UserEvent>(nameof(SdlNative.UserEvent.Data2)));

        Assert.Equal(16, Offset<SdlNative.Surface>(nameof(SdlNative.Surface.Pitch)));
        Assert.Equal(24, Offset<SdlNative.Surface>(nameof(SdlNative.Surface.Pixels)));
        Assert.Equal(32, Offset<SdlNative.Surface>(nameof(SdlNative.Surface.Refcount)));
        Assert.Equal(16, sizeof(SdlNative.Rect));
        Assert.Equal(12, Offset<SdlNative.Rect>(nameof(SdlNative.Rect.H)));
    }
}
#pragma warning restore CA1416
