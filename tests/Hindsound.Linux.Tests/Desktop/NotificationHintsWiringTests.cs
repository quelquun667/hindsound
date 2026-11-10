namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The urgency and the suppress-sound hint go on the wire from the request, untouched. What a server receives is read back
/// on Linux only (DesktopOnLinuxTests); this test keeps the wiring on any system, since a writer that sent a fixed urgency
/// or a fixed false would pass every test of the request alone: at low urgency, GNOME and Plasma show no banner.
/// </summary>
public sealed class NotificationHintsWiringTests
{
    [Fact]
    public void The_writer_sends_the_urgency_and_the_sound_hint_of_the_request()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "src", "Hindsound.Linux", "Desktop", "FreedesktopNotification.cs"))
            .Select(line => line.Trim())
            .ToList();

        AssertFollows(lines, "writer.WriteString(\"urgency\");", "writer.WriteVariantByte(request.Urgency);");
        AssertFollows(lines, "writer.WriteString(\"suppress-sound\");", "writer.WriteVariantBool(request.SuppressSound);");
    }

    private static void AssertFollows(List<string> lines, string key, string value)
    {
        Assert.Single(lines, line => line == key);
        Assert.Single(lines, line => line == value);
        Assert.Equal(lines.IndexOf(key) + 1, lines.IndexOf(value));
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
