using Hindsound.App;
using Hindsound.Linux.DBus;
using Hindsound.Ui;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// Shows the folder of the clips with the clips selected, through org.freedesktop.FileManager1.ShowItems, which Nautilus,
/// Dolphin, Nemo and Thunar answer. Without it, the folder alone, opened by xdg-open. Called off the loop: it waits for the
/// bus, a bounded time.
/// </summary>
internal sealed class LinuxFolderShell : IFolderShell
{
    public const int Ok = 0;

    // E_FAIL: the caller logs an HRESULT.
    public const int Failed = unchecked((int)0x80004005);

    internal static readonly BusMethod ShowItems = new("org.freedesktop.FileManager1", "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1", "ShowItems");

    private const int BusTimeoutMs = 5000;

    private readonly Action<string> _log;
    private readonly Func<SessionBus?> _bus;
    private readonly IFileOpener _opener;

    public LinuxFolderShell(Action<string> log)
        : this(log, SharedBus, new XdgFileOpener(log))
    {
    }

    internal LinuxFolderShell(Action<string> log, Func<SessionBus?> bus, IFileOpener opener)
    {
        _log = log;
        _bus = bus;
        _opener = opener;
    }

    public int ShowSelected(string folder, IReadOnlyList<string> files)
    {
        if (files.Count > 0 && _bus() is { } bus)
        {
            var uris = files.Select(UriList.FileUri).ToArray();
            try
            {
                // An empty startup id: the app has no window whose activation token it could pass on.
                var call = bus.CallAsync(ShowItems, "ass", (ref MessageWriter writer) =>
                {
                    writer.WriteArray(uris);
                    writer.WriteString("");
                });
                if (call.Wait(BusTimeoutMs) && call.Result.Ok)
                {
                    return Ok;
                }
            }
            catch (AggregateException ex)
            {
                _log($"show in folder: the file manager failed ({ex.InnerException?.GetType().Name})");
            }
        }

        return _opener.Open(folder) is { } error ? Fail(error) : Ok;
    }

    private int Fail(string error)
    {
        _log("show in folder: " + error);
        return Failed;
    }

    private static SessionBus? SharedBus()
    {
        try
        {
            return SessionBus.Shared;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
