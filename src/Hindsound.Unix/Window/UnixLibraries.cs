using System.Reflection;
using System.Runtime.InteropServices;

namespace Hindsound.Unix.Window;

/// <summary>
/// The native libraries of the shared code, by system. The imports keep the Linux names (libSDL3.so.0, libfreetype.so.6,
/// libharfbuzz.so.0, libmp3lame.so.0): on Linux they are loaded exactly as before, next to the program and then from the
/// system. On macOS each name stands for the dylib the app ships in Contents/Frameworks (or next to the program, for a run
/// outside a bundle), never one of the system or of Homebrew, and the one resolver of this assembly answers for them.
/// </summary>
internal static class UnixLibraries
{
    public const string FreeType = "libfreetype.so.6";
    public const string HarfBuzz = "libharfbuzz.so.0";

    /// <summary>The file the app ships on macOS for each Linux name it imports, as its install name says it.</summary>
    public static IReadOnlyDictionary<string, string> MacFiles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SdlLibrary.SystemName] = "libSDL3.0.dylib",
        [FreeType] = "libfreetype.6.dylib",
        [HarfBuzz] = "libharfbuzz.0.dylib",
        ["libmp3lame.so.0"] = "libmp3lame.0.dylib",
    };

    private static readonly object Gate = new();
    private static readonly Dictionary<string, nint> MacHandles = new(StringComparer.Ordinal);
    private static bool _resolverInstalled;

    /// <summary>
    /// Where a dylib of the app is looked for on macOS, in order: Contents/Frameworks of the bundle, the program being in
    /// Contents/MacOS; then next to the program. Never a bare name: dlopen would search the system and Homebrew.
    /// </summary>
    public static IReadOnlyList<string> MacCandidates(string programDirectory, string file) =>
        [Path.GetFullPath(Path.Combine(programDirectory, "..", "Frameworks", file)), Path.Combine(programDirectory, file)];

    /// <summary>The file of a Linux name on this system: the name itself on Linux, the shipped dylib on macOS.</summary>
    public static string FileFor(string linuxName) =>
        OperatingSystem.IsMacOS() && MacFiles.TryGetValue(linuxName, out var file) ? file : linuxName;

    /// <summary>Points the [LibraryImport] calls of this assembly at the libraries above. Once; before the first call.</summary>
    public static void InstallResolver()
    {
        lock (Gate)
        {
            if (_resolverInstalled)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(UnixLibraries).Assembly, Resolve);
            _resolverInstalled = true;
        }
    }

    /// <summary>
    /// Whether the library of <paramref name="linuxName"/> loads: on Linux by its name from the system, as the text has always
    /// checked; on macOS from the candidates above. Installs the resolver first, so the calls that follow reach the same file.
    /// </summary>
    public static bool TryLoad(string linuxName)
    {
        InstallResolver();
        if (OperatingSystem.IsMacOS())
        {
            return MacHandle(linuxName) != 0;
        }

        if (!NativeLibrary.TryLoad(linuxName, out var handle))
        {
            return false;
        }

        NativeLibrary.Free(handle);
        return true;
    }

    /// <summary>The first of <see cref="MacCandidates"/> that loads, kept loaded for the process; 0 when none does.</summary>
    internal static nint MacHandle(string linuxName)
    {
        if (!MacFiles.TryGetValue(linuxName, out var file))
        {
            return 0;
        }

        lock (Gate)
        {
            if (!MacHandles.TryGetValue(linuxName, out var handle))
            {
                handle = 0;
                foreach (var candidate in MacCandidates(AppContext.BaseDirectory, file))
                {
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out handle))
                    {
                        break;
                    }
                }

                MacHandles[linuxName] = handle;
            }

            return handle;
        }
    }

    // SDL has its own search (SdlLibrary): it checks that the library is SDL 3. Any other name gets 0 on Linux, which leaves
    // it to the runtime's own search, as before this resolver answered for more than SDL.
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name == SdlLibrary.SystemName)
        {
            return SdlLibrary.Handle();
        }

        return OperatingSystem.IsMacOS() ? MacHandle(name) : 0;
    }
}
