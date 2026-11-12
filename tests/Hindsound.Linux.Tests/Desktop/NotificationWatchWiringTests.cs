namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The watches of the clicks are put on the connection claimed, and put again from each notice and from each new connection.
/// What a bus does with them is read back on Linux only (NotificationWatchRetryOnLinuxTests), since it takes a real bus and a
/// connection lost at a given moment; this test keeps the wiring on any system, since a watch put on the bus, or put once
/// only, would pass every test of the parts alone.
/// </summary>
public sealed class NotificationWatchWiringTests
{
    [Fact]
    public void The_watches_go_on_the_connection_claimed_and_are_put_again()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Hindsound.Linux", "Desktop", "FreedesktopNotification.cs"));

        Assert.Contains(
            "() => PutWatch(ActionInvoked, connection.WatchSignalAsync(bus, ActionInvoked, ReadIdAndKey, pair => _loop.Post(() => OnAction(pair.Id, pair.Key)))),",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "() => PutWatch(NotificationClosed, connection.WatchSignalAsync(bus, NotificationClosed, ReadIdAndReason, pair => _loop.Post(() => OnClosed(pair.Id)))),",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("bus.WatchSignalAsync(", source, StringComparison.Ordinal);
        Assert.Equal(2, source.Split("WatchSignalAsync(").Length - 1);

        Assert.Contains("bus.Connected += connection => { _ = WatchOnceAsync(bus); };", source, StringComparison.Ordinal);
        Assert.Contains("_ = Task.Run(() => WatchOnceAsync(bus));", source, StringComparison.Ordinal);
        Assert.Contains(
            "LogWatchFailureAsync(() => _watchedOn.WatchOnceAsync(bus.ConnectionAsync, connection => PutWatchesAsync(bus, connection)), _log);",
            source,
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Hindsound.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("Hindsound.slnx is not above the test assembly");
    }
}
