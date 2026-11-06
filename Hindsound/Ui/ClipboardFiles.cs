using System.Runtime.InteropServices;
using System.Text;

namespace Hindsound.Ui;

/// <summary>Puts files on the clipboard as CF_HDROP, what Explorer and Discord paste. No OLE, no framework.</summary>
internal static unsafe class ClipboardFiles
{
    public const int Attempts = 5;
    public const int DelayMs = 50;

    private const int DropFilesHeaderBytes = 20;

    /// <summary>
    /// Tries, and tries again through <paramref name="after"/> while another app holds the clipboard, then reports.
    /// The waiting goes through the message loop, which stays free in between.
    /// </summary>
    public static void CopyWithRetries(Func<bool> attempt, Action<int, Action> after, Action<bool> done, int attempts = Attempts, int delayMs = DelayMs)
    {
        var left = attempts;
        Step();

        void Step()
        {
            left--;
            if (attempt())
            {
                done(true);
            }
            else if (left <= 0)
            {
                done(false);
            }
            else
            {
                after(delayMs, Step);
            }
        }
    }

    /// <summary>One attempt. Run it on the thread of the message loop.</summary>
    public static bool TrySet(nint owner, IReadOnlyList<string> paths)
    {
        var bytes = BuildDropFiles(paths);
        if (Native.OpenClipboard(owner) == 0)
        {
            return false;
        }

        try
        {
            var memory = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (nuint)bytes.Length);
            if (memory == 0)
            {
                return false;
            }

            var target = Native.GlobalLock(memory);
            if (target == null)
            {
                Native.GlobalFree(memory);
                return false;
            }

            Marshal.Copy(bytes, 0, (nint)target, bytes.Length);
            Native.GlobalUnlock(memory);
            Native.EmptyClipboard();

            // Once accepted, the memory belongs to the clipboard.
            if (Native.SetClipboardData(Native.CF_HDROP, memory) == 0)
            {
                Native.GlobalFree(memory);
                return false;
            }

            return true;
        }
        finally
        {
            Native.CloseClipboard();
        }
    }

    /// <summary>A DROPFILES header, then the wide paths, each ended by a null, and one more null at the end.</summary>
    public static byte[] BuildDropFiles(IReadOnlyList<string> paths)
    {
        var text = new StringBuilder();
        foreach (var path in paths)
        {
            text.Append(path).Append('\0');
        }

        text.Append('\0');
        var chars = Encoding.Unicode.GetBytes(text.ToString());
        var bytes = new byte[DropFilesHeaderBytes + chars.Length];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), DropFilesHeaderBytes); // pFiles
        BitConverter.TryWriteBytes(bytes.AsSpan(16), 1); // fWide
        chars.CopyTo(bytes, DropFilesHeaderBytes);
        return bytes;
    }
}
