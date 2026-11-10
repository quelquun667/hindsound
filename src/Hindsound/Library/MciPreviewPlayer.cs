using System.Runtime.InteropServices;
using Hindsound.Audio;
using Hindsound.Ui;

namespace Hindsound.Library;

/// <summary>
/// MCI itself. The <c>mpegvideo</c> type goes through DirectShow, which reads MP3 and WAV alike and plays on the default output.
/// The mode is read as a number: the words of <c>status mode</c> come from a string table of Windows, which can be translated.
/// </summary>
internal sealed unsafe partial class MciPreviewPlayer : IPreviewPlayer
{
    private const int InvalidDeviceName = 263;

    private const uint MCI_STATUS = 0x0814;
    private const nuint MCI_STATUS_ITEM = 0x00000100;
    private const uint MCI_STATUS_MODE = 0x00000004;

    private const uint QS_ALLINPUT = 0x04FF;
    private const uint MWMO_INPUTAVAILABLE = 0x0004;
    private const uint PM_REMOVE = 0x0001;

    // Enough for what a graph posts itself, and a thread that never stops being posted to still looks at its wake event.
    private const int MostMessagesPerWait = 64;

    public int Send(string command) => (int)mciSendStringW(command, null, 0, 0);

    public int ModeOf(string alias, out int mode)
    {
        mode = 0;
        var device = mciGetDeviceIDW(alias);
        if (device == 0)
        {
            return InvalidDeviceName;
        }

        var status = new StatusParameters { Item = MCI_STATUS_MODE };
        var code = mciSendCommandW(device, MCI_STATUS, MCI_STATUS_ITEM, (nuint)(&status));
        if (code == 0)
        {
            mode = (int)status.Return;
        }

        return (int)code;
    }

    // The documentation asks for no message loop by name. DirectShow creates hidden windows on the thread that builds a graph,
    // and a single-threaded apartment receives COM calls as messages, so the thread answers them rather than leave them waiting.
    public void Wait(WaitHandle wake, int milliseconds)
    {
        var handle = wake.SafeWaitHandle.DangerousGetHandle();
        _ = MsgWaitForMultipleObjectsEx(1, &handle, (uint)milliseconds, QS_ALLINPUT, MWMO_INPUTAVAILABLE);

        Message message;
        for (var i = 0; i < MostMessagesPerWait && PeekMessageW(&message, 0, 0, 0, PM_REMOVE) != 0; i++)
        {
            _ = DispatchMessageW(&message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StatusParameters
    {
        public nuint Callback;
        public nuint Return;
        public uint Item;
        public uint Track;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint mciSendStringW(string command, char* answer, uint answerChars, nint callback);

    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint mciGetDeviceIDW(string alias);

    [LibraryImport("winmm.dll")]
    private static partial uint mciSendCommandW(uint device, uint message, nuint flags, nuint parameters);

    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint milliseconds, uint wakeMask, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int PeekMessageW(Message* message, nint window, uint first, uint last, uint remove);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Message* message);
}
