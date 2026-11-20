using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hindsound.Linux.Audio;

/// <summary>
/// The calls of libpulse the capture and the preview make, as declared in pulse/thread-mainloop.h, context.h, introspect.h, subscribe.h,
/// stream.h, proplist.h, operation.h and error.h. Callbacks are passed as pointers to [UnmanagedCallersOnly] methods, and their user data is a
/// GCHandle, so nothing here needs reflection and the trimmed build keeps it all.
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class PulseNative
{
    private const string Library = "libpulse.so.0";

    // pa_context_state_t.
    public const int ContextReady = 4;
    public const int ContextFailed = 5;
    public const int ContextTerminated = 6;

    // pa_stream_state_t.
    public const int StreamReady = 2;
    public const int StreamFailed = 3;
    public const int StreamTerminated = 4;

    /// <summary>PA_CONTEXT_NOAUTOSPAWN: a missing server stays missing, the app does not start one of its own.</summary>
    public const uint NoAutospawn = 0x0001;

    [LibraryImport(Library, EntryPoint = "pa_threaded_mainloop_new")]
    public static partial IntPtr ThreadedMainloopNew();

    [LibraryImport(Library, EntryPoint = "pa_threaded_mainloop_start")]
    public static partial int ThreadedMainloopStart(IntPtr loop);

    [LibraryImport(Library, EntryPoint = "pa_threaded_mainloop_stop")]
    public static partial void ThreadedMainloopStop(IntPtr loop);

    [LibraryImport(Library, EntryPoint = "pa_threaded_mainloop_free")]
    public static partial void ThreadedMainloopFree(IntPtr loop);

    [LibraryImport(Library, EntryPoint = "pa_threaded_mainloop_lock")]
    public static partial void ThreadedMainloopLock(IntPtr loop);

    [LibraryImport(Library, EntryPoint = "pa_threaded_mainloop_unlock")]
    public static partial void ThreadedMainloopUnlock(IntPtr loop);

    [LibraryImport(Library, EntryPoint = "pa_threaded_mainloop_get_api")]
    public static partial IntPtr ThreadedMainloopGetApi(IntPtr loop);

    [LibraryImport(Library, EntryPoint = "pa_threaded_mainloop_in_thread")]
    public static partial int ThreadedMainloopInThread(IntPtr loop);

    [LibraryImport(Library, EntryPoint = "pa_context_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr ContextNew(IntPtr api, string name);

    [LibraryImport(Library, EntryPoint = "pa_context_set_state_callback")]
    public static partial void ContextSetStateCallback(IntPtr context, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_context_connect")]
    public static partial int ContextConnect(IntPtr context, IntPtr server, uint flags, IntPtr spawnApi);

    [LibraryImport(Library, EntryPoint = "pa_context_get_state")]
    public static partial int ContextGetState(IntPtr context);

    [LibraryImport(Library, EntryPoint = "pa_context_errno")]
    public static partial int ContextErrno(IntPtr context);

    [LibraryImport(Library, EntryPoint = "pa_context_disconnect")]
    public static partial void ContextDisconnect(IntPtr context);

    [LibraryImport(Library, EntryPoint = "pa_context_unref")]
    public static partial void ContextUnref(IntPtr context);

    [LibraryImport(Library, EntryPoint = "pa_context_get_server_info")]
    public static partial IntPtr ContextGetServerInfo(IntPtr context, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_context_get_sink_info_list")]
    public static partial IntPtr ContextGetSinkInfoList(IntPtr context, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_context_get_source_info_list")]
    public static partial IntPtr ContextGetSourceInfoList(IntPtr context, IntPtr callback, IntPtr userData);

    // The capture of one application (PulseAppCaptureSource): its playback streams, and a record stream on one of them. In
    // libpulse since 0.9.

    [LibraryImport(Library, EntryPoint = "pa_context_get_sink_input_info_list")]
    public static partial IntPtr ContextGetSinkInputInfoList(IntPtr context, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_proplist_gets", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr ProplistGets(IntPtr proplist, string key);

    [LibraryImport(Library, EntryPoint = "pa_stream_set_monitor_stream")]
    public static partial int StreamSetMonitorStream(IntPtr stream, uint sinkInputIndex);

    [LibraryImport(Library, EntryPoint = "pa_stream_get_buffer_attr")]
    public static partial PaBufferAttr* StreamGetBufferAttr(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_context_set_subscribe_callback")]
    public static partial void ContextSetSubscribeCallback(IntPtr context, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_context_subscribe")]
    public static partial IntPtr ContextSubscribe(IntPtr context, uint mask, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_operation_unref")]
    public static partial void OperationUnref(IntPtr operation);

    [LibraryImport(Library, EntryPoint = "pa_strerror")]
    public static partial IntPtr StrError(int error);

    [LibraryImport(Library, EntryPoint = "pa_stream_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr StreamNew(IntPtr context, string name, PaSampleSpec* spec, IntPtr channelMap);

    [LibraryImport(Library, EntryPoint = "pa_stream_set_state_callback")]
    public static partial void StreamSetStateCallback(IntPtr stream, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_stream_set_read_callback")]
    public static partial void StreamSetReadCallback(IntPtr stream, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_stream_connect_record", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int StreamConnectRecord(IntPtr stream, string device, PaBufferAttr* attributes, uint flags);

    [LibraryImport(Library, EntryPoint = "pa_stream_peek")]
    public static partial int StreamPeek(IntPtr stream, IntPtr* data, nuint* length);

    // The bytes a record stream holds for the client, the packet being read included; (size_t)-1 if it cannot say. Since 0.9.
    [LibraryImport(Library, EntryPoint = "pa_stream_readable_size")]
    public static partial nuint StreamReadableSize(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_drop")]
    public static partial int StreamDrop(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_cork")]
    public static partial IntPtr StreamCork(IntPtr stream, int corked, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_stream_get_state")]
    public static partial int StreamGetState(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_disconnect")]
    public static partial int StreamDisconnect(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_unref")]
    public static partial void StreamUnref(IntPtr stream);

    // The playback of the preview (PulsePlaybackOutput): every one of these is in libpulse since 0.9, long before 15.

    [LibraryImport(Library, EntryPoint = "pa_proplist_new")]
    public static partial IntPtr ProplistNew();

    [LibraryImport(Library, EntryPoint = "pa_proplist_sets", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int ProplistSets(IntPtr proplist, string key, string value);

    [LibraryImport(Library, EntryPoint = "pa_proplist_free")]
    public static partial void ProplistFree(IntPtr proplist);

    [LibraryImport(Library, EntryPoint = "pa_stream_new_with_proplist", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr StreamNewWithProplist(IntPtr context, string name, PaSampleSpec* spec, IntPtr channelMap, IntPtr proplist);

    [LibraryImport(Library, EntryPoint = "pa_stream_set_write_callback")]
    public static partial void StreamSetWriteCallback(IntPtr stream, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_stream_connect_playback", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int StreamConnectPlayback(IntPtr stream, string? device, PaBufferAttr* attributes, uint flags, IntPtr volume, IntPtr syncStream);

    [LibraryImport(Library, EntryPoint = "pa_stream_write")]
    public static partial int StreamWrite(IntPtr stream, void* data, nuint length, IntPtr freeCallback, long offset, int seek);

    [LibraryImport(Library, EntryPoint = "pa_stream_drain")]
    public static partial IntPtr StreamDrain(IntPtr stream, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_stream_update_timing_info")]
    public static partial IntPtr StreamUpdateTimingInfo(IntPtr stream, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_stream_get_latency")]
    public static partial int StreamGetLatency(IntPtr stream, ulong* microseconds, int* negative);

    /// <summary>The last timing the server gave, owned by the stream; null if none came yet.</summary>
    [LibraryImport(Library, EntryPoint = "pa_stream_get_timing_info")]
    public static partial PaTimingInfo* StreamGetTimingInfo(IntPtr stream);

    [LibraryImport(Library, EntryPoint = "pa_stream_flush")]
    public static partial IntPtr StreamFlush(IntPtr stream, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "pa_operation_cancel")]
    public static partial void OperationCancel(IntPtr operation);

    /// <summary>Releases an operation the caller does not follow. A null one means the call failed, and holds nothing.</summary>
    public static void Release(IntPtr operation)
    {
        if (operation != IntPtr.Zero)
        {
            OperationUnref(operation);
        }
    }

    /// <summary>The server's words for the last error of a context.</summary>
    public static string ErrorOf(IntPtr context) =>
        context == IntPtr.Zero ? "no context" : Marshal.PtrToStringUTF8(StrError(ContextErrno(context))) ?? "unknown error";

    /// <summary>A string of the server, copied while its pointer is valid.</summary>
    public static string? Text(IntPtr text) => text == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(text);
}
