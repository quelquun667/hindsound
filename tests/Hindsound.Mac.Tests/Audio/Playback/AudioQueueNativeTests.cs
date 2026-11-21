using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hindsound.Mac.Audio.Playback;

namespace Hindsound.Mac.Tests.Audio.Playback;

/// <summary>
/// AudioToolbox on the Mac, without opening any device: a queue opened here, before the microphone right of the test host is
/// decided, could leave coreaudiod stuck for every later client (the probe saw it). The sound itself is proven by the app,
/// with its rights written, in the mac job (<c>--prove-preview</c>).
/// </summary>
[Trait("Category", "Mac")]
[SupportedOSPlatform("macos")]
public sealed class AudioQueueNativeTests
{
    [Fact]
    public void Every_AudioQueue_function_the_preview_calls_is_in_AudioToolbox()
    {
        var library = NativeLibrary.Load(AudioQueueNative.AudioToolbox);
        try
        {
            var missing = AudioQueueNative.Functions.Where(name => !NativeLibrary.TryGetExport(library, name, out _)).ToList();
            Assert.Empty(missing);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [Fact]
    public void The_shell_of_the_Mac_hands_the_AudioQueue_player()
    {
        var shell = new Hindsound.Mac.App.MacShell(static _ => { });

        var player = shell.NewPreviewPlayer();

        Assert.IsType<MacPreviewPlayer>(player);
        ((IDisposable)player).Dispose();
    }

    [Fact]
    public void A_stream_asks_for_one_or_two_channels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioQueueStream.Open(3, new NoFeed()));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioQueueStream.Open(0, new NoFeed()));
    }

    private sealed class NoFeed : IMacPlaybackFeed
    {
        public int Fill(Span<float> interleaved) => 0;

        public void Drained(bool played)
        {
        }

        public void Failed(string reason)
        {
        }
    }
}
