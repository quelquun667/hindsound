using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// The file system of Linux for a trash: stat by statx (glibc 2.28, kernel 4.11), whose layout is the same on every
/// architecture, the move by renameat2 with RENAME_NOREPLACE (glibc 2.28) or else rename, the rest by libc. A rename between
/// two file systems fails with EXDEV and moves nothing: nothing here ever copies.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxTrashFileSystem : ITrashFileSystem
{
    private const int CurrentFolder = -100; // AT_FDCWD
    private const int NoFollow = 0x100; // AT_SYMLINK_NOFOLLOW
    private const int NoAutomount = 0x800; // AT_NO_AUTOMOUNT
    private const uint WantTypeModeOwner = 0x1 | 0x2 | 0x8; // STATX_TYPE | STATX_MODE | STATX_UID
    private const uint NoReplace = 1; // RENAME_NOREPLACE
    private const uint Private = 0x1C0; // 0700
    private const int StatxSize = 256;
    private const string LibC = "libc.so.6";

    // The offsets of struct statx (linux/stat.h): stx_uid, stx_mode, stx_dev_major, stx_dev_minor.
    private const int OwnerAt = 20;
    private const int ModeAt = 28;
    private const int DeviceMajorAt = 136;
    private const int DeviceMinorAt = 140;

    public uint UserId => GetUid();

    public unsafe TrashStat Stat(string path, bool follow)
    {
        var buffer = stackalloc byte[StatxSize];
        int result;
        try
        {
            result = StatX(CurrentFolder, path, NoAutomount | (follow ? 0 : NoFollow), WantTypeModeOwner, buffer);
        }
        catch (EntryPointNotFoundException)
        {
            return TrashStat.Missing(Errno.NotImplemented);
        }

        if (result != 0)
        {
            return TrashStat.Missing(Marshal.GetLastPInvokeError());
        }

        var mode = *(ushort*)(buffer + ModeAt);
        var kind = (mode & 0xF000) switch
        {
            0x8000 => TrashKind.File,
            0xA000 => TrashKind.Symlink,
            0x4000 => TrashKind.Directory,
            _ => TrashKind.Other,
        };
        var device = ((ulong)*(uint*)(buffer + DeviceMajorAt) << 32) | *(uint*)(buffer + DeviceMinorAt);
        return new TrashStat(0, kind, device, *(uint*)(buffer + OwnerAt), (uint)(mode & 0xFFF));
    }

    public string? RealPath(string path) => LinuxRealPath.Of(path);

    public int MakeDirectory(string path) => MkDir(path, Private) == 0 ? 0 : Marshal.GetLastPInvokeError();

    public int CreateFile(string path, string content)
    {
        // The open comes first, on its own: when it fails (a file that exists, no right to write) this call made nothing, and
        // nothing is deleted. Only a file this call made, and failed to write, is taken away.
        FileStream file;
        try
        {
            file = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });
        }
        catch (Exception ex)
        {
            return ErrorOf(ex);
        }

        try
        {
            using (file)
            {
                file.Write(new UTF8Encoding(false).GetBytes(content));
            }

            return 0;
        }
        catch (Exception ex)
        {
            Delete(path);
            return ErrorOf(ex);
        }
    }

    public int Rename(string from, string to)
    {
        int result;
        int error;
        try
        {
            result = RenameAt2(CurrentFolder, from, CurrentFolder, to, NoReplace);
            error = result == 0 ? 0 : Marshal.GetLastPInvokeError();
        }
        catch (EntryPointNotFoundException)
        {
            result = -1;
            error = Errno.NotImplemented;
        }

        if (result == 0)
        {
            return 0;
        }

        // A kernel or a file system without RENAME_NOREPLACE: look first, then rename. Another program making the file in
        // between is the one case the flag would have caught.
        if (error is Errno.NotImplemented or Errno.Invalid)
        {
            if (Stat(to, follow: false).Error != Errno.NoEntry)
            {
                return Errno.Exists;
            }

            return RenameC(from, to) == 0 ? 0 : Marshal.GetLastPInvokeError();
        }

        return error;
    }

    public void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The errno of an exception of the file API: .NET keeps it in the HResult of an IOException on Linux.</summary>
    private static int ErrorOf(Exception ex) => ex switch
    {
        UnauthorizedAccessException => Errno.Access,
        DirectoryNotFoundException or FileNotFoundException => Errno.NoEntry,
        IOException { HResult: > 0 and < 4096 } io => io.HResult,
        _ => Errno.Io,
    };

    [LibraryImport(LibC, EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial int StatX(int folder, string path, int flags, uint mask, byte* buffer);

    [LibraryImport(LibC, EntryPoint = "mkdir", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MkDir(string path, uint mode);

    [LibraryImport(LibC, EntryPoint = "renameat2", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameAt2(int fromFolder, string from, int toFolder, string to, uint flags);

    [LibraryImport(LibC, EntryPoint = "rename", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameC(string from, string to);

    [LibraryImport(LibC, EntryPoint = "getuid")]
    private static partial uint GetUid();
}
