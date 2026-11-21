using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Linux.Window;
using Hindsound.Unix.Window.Input;
using Hindsound.Unix.Window;

namespace Hindsound.Linux.Tests.Window.Input;

/// <summary>Tests that start SDL's video: one at a time, as SDL's state is the whole process's.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SdlInputCollection
{
    public const string Name = "SDL input";
}

/// <summary>
/// The text input, the clipboard text and the folder dialog with the real SDL 3, on its offscreen video driver: no display is
/// needed. When HINDSOUND_SDL_DIR names the folder of a libSDL3.so.0 built by the CI, that library is put next to the tests,
/// where the app looks first; without it, the system's own is used.
/// What the offscreen driver cannot show: the input method's window, a real desktop clipboard (SDL keeps the text itself),
/// and a dialog answered by a user (the session bus of the CI has no portal, so the dialog fails and must say so at once).
/// </summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
[Collection(SdlInputCollection.Name)]
public sealed class SdlInputOnLinuxTests : IDisposable
{
    private const uint InitVideo = 0x20;
    private const ulong WindowHidden = 0x8;

    private readonly nint _window;

    public SdlInputOnLinuxTests()
    {
        PutLibraryBesideTheTests();
        Assert.True(SdlTestNative.SDL_SetHint("SDL_VIDEO_DRIVER", "offscreen"), SdlNative.InputError());
        Assert.True(SdlTestNative.SDL_InitSubSystem(InitVideo), SdlNative.InputError());
        _window = SdlTestNative.SDL_CreateWindow("Hindsound input test", 320, 200, WindowHidden);
        Assert.True(_window != 0, SdlNative.InputError());
    }

    public void Dispose()
    {
        if (_window != 0)
        {
            SdlTestNative.SDL_DestroyWindow(_window);
        }

        SdlTestNative.SDL_QuitSubSystem(InitVideo);
    }

    private static void PutLibraryBesideTheTests()
    {
        if (Environment.GetEnvironmentVariable("HINDSOUND_SDL_DIR") is not { Length: > 0 } built)
        {
            return;
        }

        var target = Path.Combine(AppContext.BaseDirectory, "libSDL3.so.0");
        if (!File.Exists(target))
        {
            File.Copy(Path.Combine(built, "libSDL3.so.0"), target);
        }
    }

    [Fact]
    public void Text_input_is_on_while_a_field_wants_it_and_off_after()
    {
        var input = new SdlTextInput(_window);
        Assert.False(SdlTestNative.SDL_TextInputActive(_window));

        input.Update(true, 10, 20, 200, 30, 12);
        Assert.True(input.On);
        Assert.True(SdlTestNative.SDL_TextInputActive(_window), SdlNative.InputError());

        input.Update(true, 10, 20, 200, 30, 40);
        Assert.True(SdlTestNative.SDL_TextInputActive(_window));

        input.Update(false, 10, 20, 200, 30, 40);
        Assert.False(input.On);
        Assert.False(SdlTestNative.SDL_TextInputActive(_window));
    }

    [Fact]
    public void The_clipboard_gives_back_the_text_put_on_it()
    {
        const string text = "Clip été 音 \U0001F3B5";
        Assert.True(SdlClipboardText.Set(text), SdlNative.InputError());
        Assert.Equal(text, SdlClipboardText.Get());
    }

    [Fact]
    public void An_empty_clipboard_reads_as_none()
    {
        SdlClipboardText.Set(string.Empty);
        Assert.Null(SdlClipboardText.Get());
    }

    [Fact]
    public void A_folder_dialog_that_cannot_open_answers_none_at_once()
    {
        var clock = Stopwatch.StartNew();
        var pumps = 0;
        var folder = SdlFolderPicker.Pick(_window, Path.GetTempPath(), () =>
        {
            pumps++;
            SdlTestNative.SDL_PumpEvents();
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException("The folder dialog never answered.");
            }

            Thread.Sleep(10);
        }, out var error);

        Assert.Null(folder);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"{pumps} pumps, error: {error}");
    }
}

/// <summary>The calls of SDL that only the tests make: the window itself belongs to the window's own code.</summary>
[SupportedOSPlatform("linux")]
internal static partial class SdlTestNative
{
    private const string Library = "libSDL3.so.0";

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SDL_SetHint(string name, string value);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SDL_InitSubSystem(uint flags);

    [LibraryImport(Library)]
    internal static partial void SDL_QuitSubSystem(uint flags);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint SDL_CreateWindow(string title, int w, int h, ulong flags);

    [LibraryImport(Library)]
    internal static partial void SDL_DestroyWindow(nint window);

    [LibraryImport(Library)]
    internal static partial void SDL_PumpEvents();

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SDL_TextInputActive(nint window);
}
