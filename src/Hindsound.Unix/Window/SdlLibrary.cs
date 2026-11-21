using System.Runtime.InteropServices;

namespace Hindsound.Unix.Window;

/// <summary>
/// Where libSDL3.so.0 comes from on Linux: next to the program first (the app ships its own, as most systems have no SDL 3 yet),
/// then by name in the system, the order libmp3lame.so.0 is looked for in (Export/LameLibrary.cs). The [LibraryImport]
/// calls of <see cref="SdlNative"/> reach it through the resolver of this assembly (<see cref="UnixLibraries"/>). On macOS
/// the same name stands for the libSDL3.0.dylib of the bundle.
/// </summary>
internal static class SdlLibrary
{
    /// <summary>The name of the library on a system that has SDL 3, and of the file the app ships.</summary>
    public const string SystemName = "libSDL3.so.0";

    private static readonly object Gate = new();
    private static bool _opened;
    private static nint _handle;

    /// <summary>Where the library is looked for, in order: next to the program, then by name in the system.</summary>
    public static IReadOnlyList<string> Candidates(string programDirectory) =>
        [Path.Combine(programDirectory, SystemName), SystemName];

    /// <summary>
    /// Where the app looks: next to the program, which for the published single file is the folder it unpacks or lives in
    /// (<see cref="AppContext.BaseDirectory"/>), then the system.
    /// </summary>
    public static IReadOnlyList<string> ProgramCandidates => Candidates(AppContext.BaseDirectory);

    /// <summary>Where the app looks on this system: <see cref="ProgramCandidates"/> on Linux, the dylib of the bundle on macOS.</summary>
    public static IReadOnlyList<string> LoadCandidates => OperatingSystem.IsMacOS()
        ? UnixLibraries.MacCandidates(AppContext.BaseDirectory, UnixLibraries.MacFiles[SystemName])
        : ProgramCandidates;

    /// <summary>The file or name the library was loaded from, or null while it is not loaded.</summary>
    public static string? LoadedFrom { get; private set; }

    /// <summary>Why the library could not be loaded, or null when it was (or was not tried yet).</summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// One way to try a candidate: true with its handle if it loads and is SDL 3, false if it is not SDL 3. A candidate that
    /// the loader refuses is reported by throwing <see cref="DllNotFoundException"/> or <see cref="BadImageFormatException"/>,
    /// which carry the loader's reason into <see cref="LastError"/>.
    /// </summary>
    public delegate bool TryOpen(string candidate, out nint handle);

    /// <summary>
    /// The first candidate that opens: a path that is not there is skipped without asking the loader, since a bare name is
    /// searched in the system and a path is not. Returns 0 with every reason in <paramref name="error"/> when none opens.
    /// </summary>
    public static nint Open(IEnumerable<string> candidates, Func<string, bool> exists, TryOpen tryOpen, out string? loadedFrom, out string? error)
    {
        var tried = new List<string>();
        foreach (var candidate in candidates)
        {
            if (Path.IsPathRooted(candidate) && !exists(candidate))
            {
                tried.Add(candidate + " (no such file)");
                continue;
            }

            try
            {
                if (tryOpen(candidate, out var handle))
                {
                    loadedFrom = candidate;
                    error = null;
                    return handle;
                }

                tried.Add(candidate + " (cannot be loaded)");
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                // The loader's own words: a missing dependency, a version of the C library that is too old, a wrong architecture.
                tried.Add(candidate + " (" + ex.Message + ")");
            }
        }

        loadedFrom = null;
        error = SystemName + " was not found: " + string.Join("; ", tried);
        return 0;
    }

    /// <summary>
    /// Loads the library once, from <see cref="LoadCandidates"/>, and says whether it is there: what the app asks before
    /// it offers a window. A library without SDL_Init is not taken.
    /// </summary>
    public static bool TryLoad() => Handle() != 0;

    /// <summary>Points the [LibraryImport] calls of this assembly to <see cref="SystemName"/> at the library found here,
    /// through the one resolver of the assembly (<see cref="UnixLibraries"/>). Once.</summary>
    public static void InstallResolver() => UnixLibraries.InstallResolver();

    internal static nint Handle()
    {
        lock (Gate)
        {
            if (!_opened)
            {
                _opened = true;
                _handle = Open(LoadCandidates, File.Exists, OpenSdl, out var from, out var error);
                LoadedFrom = from;
                LastError = error;
            }

            return _handle;
        }
    }

    private static bool OpenSdl(string candidate, out nint handle)
    {
        handle = NativeLibrary.Load(candidate);
        if (NativeLibrary.TryGetExport(handle, "SDL_Init", out _))
        {
            return true;
        }

        NativeLibrary.Free(handle);
        handle = 0;
        return false;
    }
}
