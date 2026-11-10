using System.Security.Cryptography;
using System.Text;
using Hindsound.App;

namespace Hindsound.Linux.App;

/// <summary>Where the single instance of Linux lives: a lock file, held by the running instance, and the socket it listens on.</summary>
/// <param name="Folder">The folder of both.</param>
/// <param name="OwnFolder">The folder is the app's own, made for it, readable by this user only.</param>
/// <param name="Socket">The Unix socket a second launch connects to.</param>
/// <param name="Lock">The file the running instance keeps locked: whoever holds it is the one instance.</param>
internal sealed record InstancePlace(string Folder, bool OwnFolder, string Socket, string Lock)
{
    /// <summary>The longest path a Unix socket takes: sun_path holds 108 bytes, the last one a NUL.</summary>
    public const int MaxSocketBytes = 107;

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
