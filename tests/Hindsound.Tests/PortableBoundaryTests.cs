using System.Reflection;
using Hindsound.App;

namespace Hindsound.Tests;

/// <summary>
/// The code meant to run on any system, Hindsound.Core, must not reach Windows: no WASAPI, no registry, no P/Invoke, and none
/// of the Windows layer of this app, which is the Hindsound project. Every type of Hindsound.Core is read, its signatures and
/// the IL of its methods, so a call hidden in a method body counts as much as a field type.
/// </summary>
public class PortableBoundaryTests
{
    /// <summary>The Windows layer: the types that may reach Win32, WASAPI or the registry. Nested types follow their outer one.</summary>
    private static readonly string[] WindowsTypes =
    [
        "Hindsound.Ui.Native", "Hindsound.Ui.FontSpec", "Hindsound.Ui.FontFaces", "Hindsound.Ui.GdiFonts", "Hindsound.Ui.TextEngine",
        "Hindsound.Ui.TextPainter", "Hindsound.Ui.TrayPresence", "Hindsound.Ui.TrayAddRetry", "Hindsound.Ui.TrayIcon", "Hindsound.Ui.UiLoop",
        "Hindsound.Ui.Notification", "Hindsound.Ui.NotificationBitmap", "Hindsound.Ui.ClipboardFiles", "Hindsound.Ui.ConfirmSound",
        "Hindsound.Ui.ForegroundWindow", "Hindsound.Ui.ExplorerFolderShell", "Hindsound.Ui.NotificationPlacement",
        "Hindsound.Ui.Window.GdiCanvas", "Hindsound.Ui.Window.PngFile", "Hindsound.Ui.Window.WindowNative", "Hindsound.Ui.Window.SettingsWindow",
        "Hindsound.Ui.Window.FolderPicker", "Hindsound.Ui.Window.SettingsDump",
        "Hindsound.Input.Win32HotkeyNative",
        "Hindsound.Audio.CaptureSource", "Hindsound.Audio.WasapiCaptureSourceFactory", "Hindsound.Audio.DeviceWatcher", "Hindsound.Audio.ToolHelpProcessSnapshot",
        "Hindsound.Settings.RegistryRunKey",
        "Hindsound.Updates.WinHttpTransport",
        "Hindsound.Library.MciPreviewPlayer",
        "Hindsound.App.HindsoundApp", "Hindsound.App.Program", "Hindsound.App.WindowLibrary", "Hindsound.App.NotificationDump",
        "Hindsound.App.WindowsPlatform", "Hindsound.App.WindowsScreens", "Hindsound.App.WindowsQuietState", "Hindsound.App.WindowsConfirmSound",
        "Hindsound.App.ShellFileOpener", "Hindsound.App.WindowsProcessMeter",
    ];

    private static readonly Assembly Core = typeof(PlatformDefaults).Assembly;

    private static readonly Assembly Windows = typeof(WindowsPlatform).Assembly;

    [Fact]
    public void Every_type_of_the_Windows_layer_is_still_there()
    {
        var missing = WindowsTypes.Where(name => Windows.GetType(name) is null).ToList();
        var inCore = WindowsTypes.Where(name => Core.GetType(name) is not null).ToList();

        Assert.NotSame(Core, Windows);
        Assert.Empty(missing);
        Assert.Empty(inCore);
    }

    [Fact]
    public void The_portable_code_reaches_nothing_of_Windows()
    {
        var portable = Core.GetTypes().Where(type => type.Namespace?.StartsWith("Hindsound", StringComparison.Ordinal) == true && !IsWindows(type)).ToList();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in portable)
        {
            foreach (var (where, reached) in Reached(type))
            {
                if (Forbidden(reached) is { } why)
                {
                    found.Add($"{Outermost(type).FullName}: {where} reaches {why}");
                }
            }

            foreach (var method in Methods(type).Where(method => method is MethodInfo && (method.Attributes & MethodAttributes.PinvokeImpl) != 0))
            {
                found.Add($"{Outermost(type).FullName}: {method.Name} is a P/Invoke");
            }
        }

        Assert.True(portable.Count > 100, $"only {portable.Count} portable types: the scan reads the wrong assembly");
        Assert.Empty(found);
    }

    [Fact]
    public void The_scan_sees_a_call_in_a_method_body()
    {
        // The Windows platform's own body makes Windows objects, in lambdas the compiler moves to a nested type; read as if it
        // were portable, it must be caught, as the scan above reads nested types too.
        var types = typeof(WindowsPlatform).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Prepend(typeof(WindowsPlatform));
        var reached = types.SelectMany(Reached).Select(pair => Forbidden(pair.Reached)).Where(why => why is not null).ToList();

        Assert.Contains(reached, why => why!.Contains("Hindsound.Audio.WasapiCaptureSourceFactory", StringComparison.Ordinal));
        Assert.Contains(reached, why => why!.Contains("Hindsound.Settings.RegistryRunKey", StringComparison.Ordinal));
    }

    private static Type Outermost(Type type) => CoreScan.Outermost(type);

    private static IEnumerable<(string Where, Type Reached)> Reached(Type type) => CoreScan.Reached(type);

    private static IEnumerable<MethodBase> Methods(Type type) => CoreScan.Methods(type);

    private static bool IsWindows(Type type) => WindowsTypes.Contains(Outermost(type).FullName);

    /// <summary>Why this type may not be named by portable code, or null.</summary>
    private static string? Forbidden(Type type)
    {
        while (type.HasElementType)
        {
            type = type.GetElementType()!;
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (Forbidden(argument) is { } inner)
                {
                    return inner;
                }
            }

            type = type.GetGenericTypeDefinition();
        }

        if (type.IsGenericParameter)
        {
            return null;
        }

        if (CoreScan.ForbiddenType(type) is { } why)
        {
            return why;
        }

        if (type.Assembly == Windows)
        {
            return $"{Outermost(type).FullName} (the Windows layer)";
        }

        return null;
    }

    /// <summary>Every type, field and method the IL of <paramref name="method"/> names, in the order it names them.</summary>
    internal static IEnumerable<MemberInfo> MembersInBody(MethodBase method) => CoreScan.MembersInBody(method);
}
