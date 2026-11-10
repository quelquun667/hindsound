using System.Reflection;
using System.Runtime.Versioning;
using Hindsound.App;

namespace Hindsound.Tests;

/// <summary>
/// Hindsound.Core is the code that must build and run on any system. These checks read only that assembly, so they hold
/// wherever the tests run; Hindsound.Tests reads every method body of it against the Windows layer as well.
/// </summary>
public class CoreBoundaryTests
{
    private static readonly Assembly Core = typeof(PlatformDefaults).Assembly;

    [Fact]
    public void The_core_is_built_for_any_system()
    {
        Assert.Equal("Hindsound.Core", Core.GetName().Name);
        Assert.Empty(Core.GetCustomAttributes<SupportedOSPlatformAttribute>());
        Assert.Empty(Core.GetCustomAttributes<TargetPlatformAttribute>());
    }

    [Fact]
    public void The_core_references_no_Windows_assembly()
    {
        // Microsoft.Win32.Primitives is not among them: it holds SafeHandle and Win32Exception, which every system has.
        string[] windowsOnly =
        [
            "Hindsound", "NAudio.Wasapi", "NAudio.WinMM", "NAudio.WinForms", "NAudio.Asio", "NAudio.Midi",
            "Microsoft.Win32.Registry", "Microsoft.Win32.SystemEvents", "System.Drawing", "System.Windows.Forms",
        ];

        var referenced = Core.GetReferencedAssemblies().Select(name => name.Name!).ToList();
        var found = referenced.Where(name => windowsOnly.Contains(name) || name.StartsWith("System.Drawing.", StringComparison.Ordinal)).ToList();

        Assert.Contains("NAudio.Core", referenced);
        Assert.Empty(found);
    }

    [Fact]
    public void The_one_native_dependency_of_the_core_is_NAudio_Lame()
    {
        // Known and kept for now: the MP3 encoder (Encoders.OpenMp3) is NAudio.Lame, which loads libmp3lame.32.dll or
        // libmp3lame.64.dll, and only Windows has those. On another system an MP3 clip fails at that load and falls back to
        // WAV. The step that builds the other system moves the MP3 writer behind PlatformDefaults; until then, any other
        // reference outside the framework and NAudio.Core fails here instead of slipping in.
        string[] allowed = ["NAudio.Core", "NAudio.Lame"];

        var outside = Core.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name != "netstandard" && name != "mscorlib" && !name.StartsWith("System.", StringComparison.Ordinal) && !name.StartsWith("Microsoft.", StringComparison.Ordinal))
            .Where(name => !allowed.Contains(name))
            .ToList();

        Assert.Contains("NAudio.Lame", Core.GetReferencedAssemblies().Select(name => name.Name));
        Assert.Empty(outside);
    }

    [Fact]
    public void The_core_declares_no_P_Invoke()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var found = Core.GetTypes()
            .SelectMany(type => type.GetMethods(all).Select(method => (Type: type, Method: method)))
            .Where(pair => (pair.Method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .Select(pair => $"{pair.Type.FullName}.{pair.Method.Name}")
            .ToList();

        Assert.Empty(found);
    }
}
