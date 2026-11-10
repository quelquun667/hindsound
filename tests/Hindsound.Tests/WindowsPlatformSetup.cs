using System.Runtime.CompilerServices;
using Hindsound.App;

namespace Hindsound.Tests;

/// <summary>
/// The tests never run the entry point, which installs the Windows layer: this does it as the test assembly loads, so a
/// test that builds the app or an engine without fakes gets the same real parts as a launch.
/// </summary>
internal static class WindowsPlatformSetup
{
    [ModuleInitializer]
    internal static void Install() => WindowsPlatform.Install();
}
