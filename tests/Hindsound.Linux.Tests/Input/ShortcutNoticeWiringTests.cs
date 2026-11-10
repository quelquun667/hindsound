namespace Hindsound.Linux.Tests.Input;

/// <summary>
/// The notice of a desktop with no global shortcut is sent from the request of <c>ShortcutNoticeRequest</c>, untouched,
/// through the writer of every other notification. What a server receives is read back on Linux only; this test keeps the
/// wiring on any system, since a request changed on the way (a lower urgency, a request written by hand) would pass every
/// test of the request alone.
/// </summary>
public sealed class ShortcutNoticeWiringTests
{
    [Fact]
    public void The_notice_sends_its_request_untouched_through_the_shared_writer()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Hindsound.Linux", "Input", "ShortcutNotice.cs"));

        Assert.Contains(
            "private static void Write(ref MessageWriter writer) => FreedesktopNotification.Write(ref writer, ShortcutNoticeRequest.Build());",
            source,
            StringComparison.Ordinal);
        Assert.Contains("return bus.CallAsync(Notify, \"susssasa{sv}i\", Write);", source, StringComparison.Ordinal);
        Assert.Contains("var sent = await SendAsync(bus).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Single(source.Split("CallAsync(").Skip(1));
        Assert.Single(source.Split("ShortcutNoticeRequest.Build()").Skip(1));
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
