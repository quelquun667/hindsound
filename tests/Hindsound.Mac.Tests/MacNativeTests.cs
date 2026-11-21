using System.Diagnostics;
using System.Runtime.Versioning;
using Hindsound.Mac.App;
using Hindsound.Mac.Interop;
using Hindsound.Mac.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Unix.App;
using Hindsound.Unix.Export;
using Hindsound.Unix.Window;
using Hindsound.Unix.Window.Text;

namespace Hindsound.Mac.Tests;

/// <summary>
/// What only macOS answers: Objective-C from C#, the run loop, CoreText, and the dylibs built by tools/mac/build-natives.sh,
/// which the CI copies next to these tests.
/// </summary>
[Trait("Category", "Mac")]
[SupportedOSPlatform("macos")]
public sealed class MacNativeTests
{
    [Fact]
    public void An_NSString_keeps_every_UTF16_unit()
    {
        const string Text = "Hindsound clip é 日本 \U0001F3B5";

        var ns = ObjC.NSString(Text);
        try
        {
            Assert.NotEqual(0, ns);
            Assert.Equal((nuint)Text.Length, ObjC.SendUInt(ns, "length"));
            Assert.Equal(Text, ObjC.String(ns));
        }
        finally
        {
            ObjC.Release(ns);
        }
    }

    [Fact]
    public void A_file_URL_is_made_from_an_absolute_path_only()
    {
        var url = ObjC.FileUrl("/tmp/Hindsound clip.wav");
        try
        {
            Assert.Equal("file:///tmp/Hindsound%20clip.wav", ObjC.UrlText(url));
            Assert.True(ObjC.SendBool(url, "isFileURL"));
        }
        finally
        {
            ObjC.Release(url);
        }

        Assert.Equal(0, ObjC.FileUrl("relative.wav"));
        Assert.NotEqual(0, ObjC.Class("NSURL"));
    }

    [Fact]
    public void A_wake_before_the_wait_ends_it_at_once_and_a_wait_without_one_lasts_its_time()
    {
        RunOnOwnThread(() =>
        {
            using var pump = new MacRunLoopPump();
            pump.Wake();
            var clock = Stopwatch.StartNew();
            pump.Wait(Timeout.Infinite);
            Assert.True(clock.ElapsedMilliseconds < 2000, clock.ElapsedMilliseconds + " ms");

            clock.Restart();
            pump.Wait(100);
            Assert.True(clock.ElapsedMilliseconds >= 80, clock.ElapsedMilliseconds + " ms");
        });
    }

    [Fact]
    public void A_post_from_another_thread_wakes_the_loop_waiting_in_the_run_loop()
    {
        RunOnOwnThread(() =>
        {
            using var pump = new MacRunLoopPump();
            using var loop = new UnixLoop(nowMs: null, idle: pump);
            var ranOn = -1;
            var poster = new Thread(() =>
            {
                Thread.Sleep(200);
                loop.Post(() => ranOn = Environment.CurrentManagedThreadId);
                loop.Post(loop.Quit);
            });
            var clock = Stopwatch.StartNew();
            poster.Start();
            loop.Run();
            poster.Join();

            Assert.Equal(Environment.CurrentManagedThreadId, ranOn);
            Assert.True(clock.ElapsedMilliseconds < 5000, clock.ElapsedMilliseconds + " ms");
        });
    }

    [Fact]
    public void CoreText_names_a_font_file_with_Japanese_that_FreeType_opens()
    {
        using var fallback = new CoreTextFallback();
        fallback.CheckLoadable();

        var (postScript, file) = fallback.Describe("日本語", bold: false);
        var found = fallback.Find(0x65E5, 80);

        Assert.False(string.IsNullOrEmpty(postScript));
        Assert.True(File.Exists(file), file);
        Assert.NotNull(found);
        Assert.Equal(0, found.Value.Path[^1]);
        Assert.True(found.Value.Index >= 0);
    }

    [Fact]
    public void The_dylibs_of_the_bundle_load_by_their_Linux_names()
    {
        Assert.True(UnixLibraries.TryLoad(UnixLibraries.FreeType));
        Assert.True(UnixLibraries.TryLoad(UnixLibraries.HarfBuzz));
        Assert.True(SdlLibrary.TryLoad(), SdlLibrary.LastError);
        Assert.EndsWith("libSDL3.0.dylib", SdlLibrary.LoadedFrom, StringComparison.Ordinal);
        Assert.NotNull(LameLibrary.Shared);
        Assert.Equal("3.100", LameLibrary.Shared.Version);
        Assert.EndsWith("libmp3lame.0.dylib", LameLibrary.Shared.LoadedFrom, StringComparison.Ordinal);
    }

    [Fact]
    public void The_text_of_the_Mac_measures_as_on_Linux_and_falls_back_to_a_font_of_the_system()
    {
        using var text = MacText.Create();
        var body = new FontSpec(Face.Body, 13);

        Assert.True(text.Measure(body, "Hindsound") > 0);
        Assert.True(text.Measure(body, "日本語") > 0);
        Assert.True(text.FallbackCount >= 1);
    }

    [Fact]
    public void The_place_of_the_single_instance_fits_a_socket_of_macOS()
    {
        var place = InstancePlace.ForThisSystem(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "k");

        Assert.NotNull(place);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(place.Socket) <= InstancePlace.MaxMacSocketBytes);
    }

    private static void RunOnOwnThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the test thread did not end");
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
