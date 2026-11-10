using System.Runtime.InteropServices;

namespace Hindsound.Linux.Export;

/// <summary>
/// The few functions of libmp3lame the app calls, found by name in the library once it is loaded. Reached through function
/// pointers, not [DllImport]: the library comes from next to the program, then from the system, and a test can point it at
/// any file. The signatures are those of lame.h (LAME 3.100); every function is cdecl.
/// </summary>
internal sealed unsafe class LameLibrary
{
    /// <summary>The name of the library on a system that has LAME installed.</summary>
    public const string SystemName = "libmp3lame.so.0";

    private readonly nint _handle;

    private readonly delegate* unmanaged[Cdecl]<nint> _init;
    private readonly delegate* unmanaged[Cdecl]<nint, int> _close;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int> _setInSampleRate;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int> _setNumChannels;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int> _setBitrate;
    private readonly delegate* unmanaged[Cdecl]<nint, int> _initParams;
    private readonly delegate* unmanaged[Cdecl]<nint, short*, int, byte*, int, int> _encodeInterleaved;
    private readonly delegate* unmanaged[Cdecl]<nint, short*, short*, int, byte*, int, int> _encode;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, int, int> _encodeFlush;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, nuint, nuint> _lametagFrame;
    private readonly delegate* unmanaged[Cdecl]<byte*> _getVersion;

    private LameLibrary(nint handle, string loadedFrom)
    {
        _handle = handle;
        LoadedFrom = loadedFrom;
        _init = (delegate* unmanaged[Cdecl]<nint>)Export("lame_init");
        _close = (delegate* unmanaged[Cdecl]<nint, int>)Export("lame_close");
        _setInSampleRate = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("lame_set_in_samplerate");
        _setNumChannels = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("lame_set_num_channels");
        _setBitrate = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("lame_set_brate");
        _initParams = (delegate* unmanaged[Cdecl]<nint, int>)Export("lame_init_params");
        _encodeInterleaved = (delegate* unmanaged[Cdecl]<nint, short*, int, byte*, int, int>)Export("lame_encode_buffer_interleaved");
        _encode = (delegate* unmanaged[Cdecl]<nint, short*, short*, int, byte*, int, int>)Export("lame_encode_buffer");
        _encodeFlush = (delegate* unmanaged[Cdecl]<nint, byte*, int, int>)Export("lame_encode_flush");
        _lametagFrame = (delegate* unmanaged[Cdecl]<nint, byte*, nuint, nuint>)Export("lame_get_lametag_frame");
        _getVersion = (delegate* unmanaged[Cdecl]<byte*>)Export("get_lame_version");
    }

    /// <summary>The file or name the library was loaded from.</summary>
    public string LoadedFrom { get; }

    /// <summary>The version LAME reports about itself, such as "3.100".</summary>
    public string Version => Marshal.PtrToStringAnsi((nint)_getVersion()) ?? "";

    /// <summary>
    /// Where the library is looked for, in order: next to the program (the app ships its own), then by name in the system.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string programDirectory) =>
        [Path.Combine(programDirectory, SystemName), SystemName];

    /// <summary>The library of the app, loaded on first use. Null while it cannot be loaded, with the reason in <see cref="LastError"/>.</summary>
    public static LameLibrary? Shared
    {
        get
        {
            lock (SharedLock)
            {
                if (_shared is null)
                {
                    try
                    {
                        _shared = Load(Candidates(AppContext.BaseDirectory));
                        LastError = null;
                    }
                    catch (DllNotFoundException ex)
                    {
                        LastError = ex.Message;
                    }
                }

                return _shared;
            }
        }
    }

    /// <summary>Why <see cref="Shared"/> is null, or null when it is not.</summary>
    public static string? LastError { get; private set; }

    private static readonly object SharedLock = new();
    private static LameLibrary? _shared;

    /// <summary>Loads the first candidate that opens and has every function. Throws <see cref="DllNotFoundException"/> if none does.</summary>
    public static LameLibrary Load(IEnumerable<string> candidates)
    {
        var tried = new List<string>();
        foreach (var candidate in candidates)
        {
            // A path that is not there is skipped here, not left to dlopen: a bare name is searched in the system, a path is not.
            if (Path.IsPathRooted(candidate) && !File.Exists(candidate))
            {
                tried.Add(candidate + " (no such file)");
                continue;
            }

            if (!NativeLibrary.TryLoad(candidate, out var handle))
            {
                tried.Add(candidate + " (cannot be loaded)");
                continue;
            }

            try
            {
                return new LameLibrary(handle, candidate);
            }
            catch (EntryPointNotFoundException ex)
            {
                NativeLibrary.Free(handle);
                tried.Add(candidate + " (" + ex.Message + ")");
            }
        }

        throw new DllNotFoundException("libmp3lame was not found: " + string.Join("; ", tried));
    }

    private nint Export(string name) => NativeLibrary.GetExport(_handle, name);

    /// <summary>A new encoder, already given its format and initialised. Throws <see cref="IOException"/> if LAME refuses the settings.</summary>
    public nint OpenEncoder(int sampleRate, int channels, int bitrateKbps)
    {
        var gfp = _init();
        if (gfp == 0)
            throw new IOException("LAME could not create an encoder.");

        try
        {
            Check(_setInSampleRate(gfp, sampleRate), "the sample rate");
            Check(_setNumChannels(gfp, channels), "the channel count");
            Check(_setBitrate(gfp, bitrateKbps), "the bitrate");
            Check(_initParams(gfp), "the initialisation");
            return gfp;
        }
        catch
        {
            _close(gfp);
            throw;
        }
    }

    public void CloseEncoder(nint gfp) => _close(gfp);

    /// <summary>Encodes whole frames of 16-bit PCM (interleaved if stereo) and returns the bytes of MP3 written to <paramref name="mp3"/>.</summary>
    public int Encode(nint gfp, ReadOnlySpan<short> pcm, int channels, Span<byte> mp3)
    {
        var frames = pcm.Length / channels;
        if (frames == 0)
            return 0;

        int written;
        fixed (short* samples = pcm)
        fixed (byte* output = mp3)
        {
            // A mono encoder reads only the left pointer; the right one is given too so that no version of LAME sees a null.
            written = channels == 2
                ? _encodeInterleaved(gfp, samples, frames, output, mp3.Length)
                : _encode(gfp, samples, samples, frames, output, mp3.Length);
        }

        return CheckEncoded(written);
    }

    /// <summary>Writes what LAME still holds. The buffer needs at least 7200 bytes.</summary>
    public int Flush(nint gfp, Span<byte> mp3)
    {
        fixed (byte* output = mp3)
            return CheckEncoded(_encodeFlush(gfp, output, mp3.Length));
    }

    /// <summary>The frame that belongs at the very start of the file once it is complete, or empty if LAME has none.</summary>
    public byte[] LametagFrame(nint gfp)
    {
        var buffer = new byte[4096];
        fixed (byte* output = buffer)
        {
            var size = _lametagFrame(gfp, output, (nuint)buffer.Length);
            if (size > (nuint)buffer.Length)
            {
                // Too small: the answer is the size it needs.
                buffer = new byte[(int)size];
                fixed (byte* bigger = buffer)
                    size = _lametagFrame(gfp, bigger, (nuint)buffer.Length);
            }

            return buffer.AsSpan(0, (int)size).ToArray();
        }
    }

    /// <summary>The buffer LAME documents as enough for <paramref name="frames"/> sample frames: 1.25 times them, plus 7200 bytes.</summary>
    public static int Mp3BufferSize(int frames) => frames + frames / 4 + 7200;

    private static void Check(int result, string what)
    {
        if (result < 0)
            throw new IOException("LAME refused " + what + " (code " + result + ").");
    }

    private static int CheckEncoded(int result) =>
        result >= 0
            ? result
            : throw new IOException(result switch
            {
                -1 => "LAME: the output buffer was too small.",
                -2 => "LAME: out of memory.",
                -3 => "LAME: the encoder was not initialised.",
                -4 => "LAME: a problem in the psychoacoustic model.",
                _ => "LAME failed with code " + result + ".",
            });
}
