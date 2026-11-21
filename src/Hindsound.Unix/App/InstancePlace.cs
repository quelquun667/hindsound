using System.Security.Cryptography;
using System.Text;
using Hindsound.App;

namespace Hindsound.Unix.App;

/// <summary>Where the single instance lives, on Linux and macOS: a lock file, held by the running instance, and the socket it listens on.</summary>
/// <param name="Folder">The folder of both.</param>
/// <param name="OwnFolder">The folder is the app's own, made for it, readable by this user only.</param>
/// <param name="Socket">The Unix socket a second launch connects to.</param>
/// <param name="Lock">The file the running instance keeps locked: whoever holds it is the one instance.</param>
internal sealed record InstancePlace(string Folder, bool OwnFolder, string Socket, string Lock)
{
    /// <summary>The longest path a Unix socket takes: sun_path holds 108 bytes, the last one a NUL.</summary>
    public const int MaxSocketBytes = 107;

    /// <summary>The same on macOS, whose sun_path holds 104 bytes.</summary>
    public const int MaxMacSocketBytes = 103;

    /// <summary>The temporary folder of the user on macOS (/var/folders/.../T/), readable by this user only.</summary>
    public const string MacTemporaryVariable = "TMPDIR";

    /// <summary>The place of this system: <see cref="ForMac"/> on macOS, <see cref="For"/> elsewhere.</summary>
    public static InstancePlace? ForThisSystem(Func<string, string?> environment, string? home, string key) =>
        OperatingSystem.IsMacOS() ? ForMac(environment, home, key, MacUserTemporary.Read) : For(environment, home, key);

    /// <summary>
    /// The place on macOS, which has no XDG_RUNTIME_DIR: the user's own temporary folder (<c>TMPDIR</c>, under /var/folders,
    /// private to the user and emptied at restart), else a folder of the app in ~/Library/Caches. Without <c>TMPDIR</c>
    /// (<c>env -i</c>, a launch by launchd), <paramref name="userTemporary"/> asks the system for that same folder, so that
    /// both launches meet in one place. When the socket would be too long for macOS in the temporary folder, the folder in
    /// ~/Library/Caches, with or without <c>TMPDIR</c> alike. Null when no folder is known or none is short enough.
    /// </summary>
    /// <param name="userTemporary">The temporary folder of the user as the system gives it (confstr), or null.</param>
    public static InstancePlace? ForMac(Func<string, string?> environment, string? home, string key, Func<string?>? userTemporary = null)
    {
        var name = FileNameFor(key);
        if ((Absolute(environment(MacTemporaryVariable)) ?? Absolute(userTemporary?.Invoke())) is { } temporary
            && MacPlace(temporary, own: false, name) is { } inTemporary)
        {
            return inTemporary;
        }

        return Absolute(home) is { } h ? MacPlace(Join(Join(Join(h, "Library"), "Caches"), "Hindsound"), own: true, name) : null;
    }

    private static InstancePlace? MacPlace(string folder, bool own, string name)
    {
        var socket = Join(folder, name + ".sock");
        return Encoding.UTF8.GetByteCount(socket) > MaxMacSocketBytes ? null : new InstancePlace(folder, own, socket, Join(folder, name + ".lock"));
    }

    public const string RuntimeVariable = "XDG_RUNTIME_DIR";

    public const string CacheVariable = "XDG_CACHE_HOME";

    /// <summary>
    /// The place for the key of <see cref="SingleInstance.NameFor(AppOptions)"/>. <c>XDG_RUNTIME_DIR</c> belongs to the
    /// user alone and is emptied at logout, which suits a socket; without it, a folder of the app in the user's cache.
    /// Null when no folder is known, or when the path is too long for a socket.
    /// </summary>
    public static InstancePlace? For(Func<string, string?> environment, string? home, string key)
    {
        string folder;
        bool own;
        if (Absolute(environment(RuntimeVariable)) is { } runtime)
        {
            folder = runtime;
            own = false;
        }
        else if ((Absolute(environment(CacheVariable)) ?? (Absolute(home) is { } h ? Join(h, ".cache") : null)) is { } cache)
        {
            folder = Join(cache, "hindsound");
            own = true;
        }
        else
        {
            return null;
        }

        var name = FileNameFor(key);
        var socket = Join(folder, name + ".sock");
        return Encoding.UTF8.GetByteCount(socket) > MaxSocketBytes ? null : new InstancePlace(folder, own, socket, Join(folder, name + ".lock"));
    }

    /// <summary>
    /// Short and the same from one process to the next: the key itself can be 90 characters long, too much for a socket path
    /// once a folder is in front of it.
    /// </summary>
    public static string FileNameFor(string key) =>
        "hindsound-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 8).ToLowerInvariant();

    private static string? Absolute(string? path) => path is { Length: > 1 } && path[0] == '/' ? path.TrimEnd('/') : null;

    // Linux paths, the same on any system a test runs on.
    private static string Join(string folder, string name) => folder + "/" + name;
}

/// <summary>The temporary folder of the user on macOS as the system gives it, whatever TMPDIR says: confstr(_CS_DARWIN_USER_TEMP_DIR).</summary>
internal static unsafe class MacUserTemporary
{
    private const int DarwinUserTempDir = 65537;

    public static string? Read()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        const int Size = 1024;
        var buffer = stackalloc byte[Size];
        var length = confstr(DarwinUserTempDir, buffer, Size);
        return length is > 1 and <= Size ? Encoding.UTF8.GetString(buffer, (int)length - 1) : null;
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern nuint confstr(int name, byte* buffer, nuint length);
}

/// <summary>What travels on the socket: one short line from the second launch, one short line back.</summary>
internal static class InstanceWire
{
    public const int MaxLength = 16;

    private const string ShowLine = "show\n";
    private const string SaveLine = "save\n";

    public static byte[] Ack { get; } = "ok\n"u8.ToArray();

    public static byte[] Encode(InstanceMessage message) => Encoding.ASCII.GetBytes(message switch
    {
        InstanceMessage.Save => SaveLine,
        _ => ShowLine,
    });

    /// <summary>The message of a line, or null for anything else: a stranger on the socket gets nothing done.</summary>
    public static InstanceMessage? Decode(ReadOnlySpan<byte> line)
    {
        if (line.SequenceEqual(Encoding.ASCII.GetBytes(ShowLine)))
        {
            return InstanceMessage.Show;
        }

        return line.SequenceEqual(Encoding.ASCII.GetBytes(SaveLine)) ? InstanceMessage.Save : null;
    }

    /// <summary>Whether <paramref name="line"/> is whole: it ends with its newline.</summary>
    public static bool Complete(ReadOnlySpan<byte> line) => line.Length > 0 && line[^1] == (byte)'\n';
}
