using Hindsound.Linux.App;
using Hindsound.Linux.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>
/// The notice of a desktop with no global shortcut names the file this copy was launched by. On Windows the path of the test
/// process is not an absolute path of Linux, so the notice read there is the generic one whatever its wiring: the path an
/// AppImage gives is checked from a set environment, and the property is held to the launched file by its source.
/// </summary>
public sealed class NoShortcutTextWiringTests
{
    [Fact]
    public void The_notice_names_the_AppImage_a_copy_was_launched_by()
    {
        var program = LaunchPaths.Resolve(
            name => name == LaunchPaths.AppImageVariable ? "/home/someone/Apps/Hindsound.AppImage" : null,
            @"C:\tests\testhost.exe");

        Assert.Equal(
            "No global shortcut on this desktop. In your keyboard settings, bind this command to a key: /home/someone/Apps/Hindsound.AppImage --save",
            HotkeyChoice.NoShortcutTextFor(program));
    }

    [Fact]
    public void The_notice_is_built_from_the_file_launched()
    {
        var root = RepositoryRoot();
        var choice = File.ReadAllText(Path.Combine(root, "src", "Hindsound.Linux", "Input", "HotkeyChoice.cs"));
        var paths = File.ReadAllText(Path.Combine(root, "src", "Hindsound.Linux", "App", "LaunchPaths.cs"));

        Assert.Contains("public static string NoShortcutText { get; } = NoShortcutTextFor(LaunchPaths.Current());", choice, StringComparison.Ordinal);
        Assert.Single(choice.Split("NoShortcutText {").Skip(1));
        Assert.Contains("public static string? Current() => Resolve(Environment.GetEnvironmentVariable, Environment.ProcessPath);", paths, StringComparison.Ordinal);
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
