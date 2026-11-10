using Hindsound.Linux.DBus;

namespace Hindsound.Linux.Tests.DBus;

/// <summary>A call that fails again and again, as the icon's registration does every 2 s without a watcher: one line, not one per try.</summary>
public sealed class CallFailureLogTests
{
    private static readonly BusMethod Register = new("org.kde.StatusNotifierWatcher", "/StatusNotifierWatcher", "org.kde.StatusNotifierWatcher", "RegisterStatusNotifierItem");
    private static readonly BusMethod Notify = new("org.freedesktop.Notifications", "/org/freedesktop/Notifications", "org.freedesktop.Notifications", "Notify");
    private static readonly BusMethod GetOfPortal = new("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop", "org.freedesktop.DBus.Properties", "Get");
    private static readonly BusMethod GetOfOther = new("org.kde.StatusNotifierWatcher", "/StatusNotifierWatcher", "org.freedesktop.DBus.Properties", "Get");
    private const string Unknown = "org.freedesktop.DBus.Error.ServiceUnknown";

    [Fact]
    public void The_same_failure_of_a_method_is_said_once()
    {
        var log = new List<string>();
        var failures = new CallFailureLog(log.Add);

        for (var i = 0; i < 60; i++)
        {
            failures.Failed(Register, Unknown);
        }

        Assert.Equal(["org.kde.StatusNotifierWatcher.RegisterStatusNotifierItem failed: " + Unknown], log);
    }

    [Fact]
    public void A_new_error_another_method_or_another_peer_is_said()
    {
        var log = new List<string>();
        var failures = new CallFailureLog(log.Add);

        failures.Failed(Register, Unknown);
        failures.Failed(Notify, Unknown);
        failures.Failed(Register, "org.freedesktop.DBus.Error.NoReply");
        failures.Failed(GetOfPortal, Unknown);
        failures.Failed(GetOfOther, Unknown);

        Assert.Equal(5, log.Count);
        Assert.Equal("org.freedesktop.DBus.Properties.Get failed: " + Unknown, log[4]);
    }

    [Fact]
    public void A_success_lets_the_next_failure_be_said_again()
    {
        var log = new List<string>();
        var failures = new CallFailureLog(log.Add);

        failures.Failed(Notify, Unknown);
        failures.Succeeded(Notify);
        failures.Failed(Notify, Unknown);
        failures.Failed(Notify, Unknown);

        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void The_session_bus_carries_one_log_of_failures_that_writes_to_its_log()
    {
        var log = new List<string>();
        using var bus = new SessionBus(() => null, log.Add);

        bus.Failures.Failed(Notify, Unknown);
        bus.Failures.Failed(Notify, Unknown);

        Assert.Equal(["org.freedesktop.Notifications.Notify failed: " + Unknown], log);
    }
}
