using Hindsound.App;
using Hindsound.Tests.Updates;
using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>
/// The channel the Windows layer sets, read from a folder of the test's own as the zip's copy, an installed copy or an exe
/// copied by hand would have it; and the line the entry point writes when portable.txt lies next to an installed copy.
/// </summary>
public class WindowsPortableFolderTests
{
    private static void Put(TempFolder folder, string name) => File.WriteAllText(folder.File(name), "");

    [Fact]
    public void Portable_txt_alone_sets_the_portable_channel()
    {
        using var folder = new TempFolder();
        Put(folder, PortableCopy.MarkerName);

        Assert.Same(UpdateChannel.WindowsPortable, WindowsPlatform.ChannelFor(folder.Path, out var ignored));
        Assert.False(ignored);
    }

    [Fact]
    public void Portable_txt_next_to_the_uninstaller_keeps_the_installed_channel_and_is_ignored()
    {
        using var folder = new TempFolder();
        Put(folder, PortableCopy.MarkerName);
        Put(folder, InstallKind.UninstallerName);

        Assert.Null(WindowsPlatform.ChannelFor(folder.Path, out var ignored));
        Assert.True(ignored);
    }

    [Fact]
    public void Without_portable_txt_the_default_stays_and_nothing_is_ignored()
    {
        using var installed = new TempFolder();
        Put(installed, InstallKind.UninstallerName);
        using var byHand = new TempFolder();

        Assert.Null(WindowsPlatform.ChannelFor(installed.Path, out var installedIgnored));
        Assert.False(installedIgnored);
        Assert.Null(WindowsPlatform.ChannelFor(byHand.Path, out var byHandIgnored));
        Assert.False(byHandIgnored);
    }

    [Fact]
    public void The_entry_point_logs_the_ignored_marker_only_when_it_was_ignored()
    {
        using var folder = new TempFolder();
        Put(folder, PortableCopy.MarkerName);
        Put(folder, InstallKind.UninstallerName);
        WindowsPlatform.ChannelFor(folder.Path, out var ignored);
        var ignoredLog = new Journal(folder.File("ignored.log"));
        var keptLog = new Journal(folder.File("kept.log"));

        Program.WriteMarkerLine(ignoredLog, ignored);
        Program.WriteMarkerLine(keptLog, markerIgnored: false);
        keptLog.Write("launch");

        Assert.Contains(PortableCopy.MarkerIgnoredLine, File.ReadAllText(ignoredLog.Path), StringComparison.Ordinal);
        Assert.DoesNotContain(PortableCopy.MarkerIgnoredLine, File.ReadAllText(keptLog.Path), StringComparison.Ordinal);
    }
}
