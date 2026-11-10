using System.Runtime.Versioning;
using Hindsound.Linux.DBus;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.DBus;

/// <summary>A service missing from the CI's session bus, called again and again: the log says it once.</summary>
[Trait("Category", "Linux")]
[SupportedOSPlatform("linux")]
public sealed class CallFailuresOnLinuxTests
{
    private static readonly BusMethod Missing = new("io.github.quelquun667.Hindsound.Nobody", "/io/github/quelquun667/Hindsound/Nobody", "io.github.quelquun667.Hindsound.Nobody", "Ping");

    [Fact]
    public async Task A_missing_service_called_three_times_is_one_line_of_the_log()
    {
        var log = new List<string>();
        using var bus = new SessionBus(() => DBusAddress.Session, line =>
        {
            lock (log)
            {
                log.Add(line);
            }
        });

        for (var i = 0; i < 3; i++)
        {
            var answer = await bus.CallAsync(Missing, null, null);
            Assert.False(answer.Ok);
            Assert.Equal("org.freedesktop.DBus.Error.ServiceUnknown", answer.Error);
        }

        lock (log)
        {
            Assert.Equal(["io.github.quelquun667.Hindsound.Nobody.Ping failed: org.freedesktop.DBus.Error.ServiceUnknown"], log.Where(line => line.Contains("failed", StringComparison.Ordinal)));
        }
    }
}
