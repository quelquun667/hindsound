namespace Hindsound.Linux.Desktop;

/// <summary>What a path is, for a trash: the files and links it takes, and the rest it leaves alone.</summary>
internal enum TrashKind
{
    File,
    Symlink,
    Directory,
    Other,
}

/// <summary>The errno values a trash tells apart, as Linux numbers them.</summary>
internal static class Errno
{
    public const int NotPermitted = 1;
    public const int NoEntry = 2;
    public const int Io = 5;
    public const int Access = 13;
    public const int Exists = 17;
    public const int CrossDevice = 18;
    public const int NotDirectory = 20;
    public const int Invalid = 22;
    public const int NotImplemented = 38;
}

/// <summary>The answer of stat or lstat for one path. <see cref="Error"/> is 0 when the path was found.</summary>
/// <param name="Error">0, or the errno.</param>
/// <param name="Device">The device holding the path: equal for two paths of one file system.</param>
/// <param name="Owner">The user id of the owner.</param>
/// <param name="Mode">The permission bits, with the sticky bit (01000).</param>
internal readonly record struct TrashStat(int Error, TrashKind Kind, ulong Device, uint Owner, uint Mode)
{
    public const uint StickyBit = 0x200;

    public bool Found => Error == 0;

    public bool Sticky => (Mode & StickyBit) != 0;

    public static TrashStat Missing(int error) => new(error, TrashKind.Other, 0, 0, 0);
}

/// <summary>
/// The few things a trash asks of the file system, each as the system answers it (an errno, 0 for success), so that the
/// rules of the trash run against a fake on any system and against Linux in the CI. None of them copies a file: moving a
/// file to another file system fails with <see cref="Errno.CrossDevice"/> and leaves it where it was.
/// </summary>
internal interface ITrashFileSystem
{
    /// <summary>The user the trash is made for.</summary>
    uint UserId { get; }

    /// <param name="follow">True to look at what a link points to (stat), false to look at the link itself (lstat).</param>
    TrashStat Stat(string path, bool follow);

    /// <summary>The path with every link of it resolved; null when it can't be.</summary>
    string? RealPath(string path);

    /// <summary>Makes one folder, readable by its owner alone (0700). Its parent must exist.</summary>
    int MakeDirectory(string path);

    /// <summary>Makes a file that must not exist yet, readable by its owner alone (0600), and writes the text, in UTF-8, into it.</summary>
    int CreateFile(string path, string content);

    /// <summary>Moves a file or a link, by rename. Never replaces what is at the destination, and never copies.</summary>
    int Rename(string from, string to);

    /// <summary>Removes a file, quietly when it is not there.</summary>
    void Delete(string path);
}
