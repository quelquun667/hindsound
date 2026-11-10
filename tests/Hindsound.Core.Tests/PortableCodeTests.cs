using System.Reflection;
using System.Runtime.InteropServices;
using Hindsound.App;

namespace Hindsound.Tests;

/// <summary>
/// Hindsound.Core must reach nothing that only Windows has. The compiler's check (CA1416) stops a call that is not behind a
/// check of the system, but not one that is, and not a native library loaded by name; so every type of Hindsound.Core is read,
/// its signatures and the IL of its methods, on every system the tests run on.
/// </summary>
public class PortableCodeTests
{
    private static readonly Assembly Core = typeof(PlatformDefaults).Assembly;

    [Fact]
    public void The_core_reaches_nothing_that_only_Windows_has()
    {
        var portable = Core.GetTypes().Where(type => type.Namespace?.StartsWith("Hindsound", StringComparison.Ordinal) == true).ToList();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in portable)
        {
            foreach (var (where, reached) in CoreScan.Reached(type))
            {
                if (CoreScan.ForbiddenType(reached) is { } why)
                {
                    found.Add($"{CoreScan.Outermost(type).FullName}: {where} reaches {why}");
                }
            }

            foreach (var (where, why) in CoreScan.ForbiddenInBodies(type))
            {
                found.Add($"{CoreScan.Outermost(type).FullName}: {where} reaches {why}");
            }
        }

        Assert.True(portable.Count > 100, $"only {portable.Count} portable types: the scan reads the wrong assembly");
        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    [Fact]
    public void The_scan_flags_a_call_the_runtime_marks_as_Windows_only_even_behind_a_check()
    {
        var found = CoreScan.ForbiddenInBodies(typeof(Probes)).Select(pair => pair.Why).ToList();

        Assert.Contains(found, why => why.Contains("WindowsIdentity", StringComparison.Ordinal));
        Assert.Contains(found, why => why.Contains("Registry", StringComparison.Ordinal));
    }

    [Fact]
    public void The_scan_flags_a_native_library_loaded_by_hand()
    {
        var found = CoreScan.ForbiddenInBodies(typeof(Probes)).Select(pair => pair.Why).ToList();

        Assert.Contains(found, why => why.Contains("NativeLibrary", StringComparison.Ordinal));
        Assert.Contains(found, why => why.Contains("GetDelegateForFunctionPointer", StringComparison.Ordinal));
        Assert.Contains(found, why => why.Contains("GetTypeFromProgID", StringComparison.Ordinal));
        Assert.Contains(found, why => why.Contains("GetTypeFromCLSID", StringComparison.Ordinal));
    }

    [Fact]
    public void The_scan_flags_a_call_through_a_function_pointer()
    {
        var found = CoreScan.ForbiddenInBodies(typeof(Probes)).Select(pair => pair.Why).ToList();

        Assert.Contains(found, why => why.Contains("calli", StringComparison.Ordinal));
    }

    [Fact]
    public void The_scan_flags_a_type_the_runtime_marks_as_Windows_only()
    {
        Assert.NotNull(CoreScan.ForbiddenType(typeof(System.Security.Principal.WindowsIdentity)));
        Assert.NotNull(CoreScan.ForbiddenType(typeof(System.Security.Principal.WindowsIdentity[])));
        Assert.NotNull(CoreScan.ForbiddenType(typeof(List<System.Security.Principal.WindowsIdentity>)));
        Assert.NotNull(CoreScan.ForbiddenType(typeof(NativeLibrary)));
        Assert.Null(CoreScan.ForbiddenType(typeof(Thread)));
        Assert.Null(CoreScan.ForbiddenType(typeof(List<string>)));
    }

    [Fact]
    public void The_scan_lets_the_ordinary_calls_through()
    {
        var found = CoreScan.ForbiddenInBodies(typeof(Ordinary)).ToList();

        Assert.Empty(found);
    }

    /// <summary>What the scan must catch, written the way a mistake would be: none of it runs.</summary>
    private static unsafe class Probes
    {
        public static void Identity()
        {
            if (OperatingSystem.IsWindows())
            {
                _ = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            }
        }

        public static void Registry()
        {
            if (OperatingSystem.IsWindows())
            {
                _ = Microsoft.Win32.Registry.CurrentUser;
            }
        }

        public static void Library() => _ = NativeLibrary.TryLoad("user32.dll", out _);

        public static void Delegate() => _ = Marshal.GetDelegateForFunctionPointer<Action>(IntPtr.Zero);

        public static void Com()
        {
            if (OperatingSystem.IsWindows())
            {
                _ = Type.GetTypeFromProgID("Shell.Application");
                _ = Type.GetTypeFromCLSID(Guid.Empty);
            }
        }

        public static void Pointer()
        {
            delegate*<int> call = null;
            _ = call();
        }
    }

    /// <summary>Plain calls of the framework, with a check of the system on the one call that needs it.</summary>
    private static class Ordinary
    {
        public static void Calls()
        {
            var thread = new Thread(() => { });
            if (OperatingSystem.IsWindows())
            {
                thread.SetApartmentState(ApartmentState.STA);
            }

            _ = Path.Combine("a", "b");
            _ = new List<int>().Count;
        }
    }
}
