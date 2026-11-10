using Hindsound.Linux.DBus;
using Tmds.DBus.Protocol;

namespace Hindsound.Linux.Tests.DBus;

public sealed class DBusNamesTests
{
    [Theory]
    [InlineData("/", true)]
    [InlineData("/org/freedesktop/DBus", true)]
    [InlineData("/a_1/B2", true)]
    [InlineData("", false)]
    [InlineData("org/freedesktop", false)]
    [InlineData("/org/", false)]
    [InlineData("/org//DBus", false)]
    [InlineData("/org/free-desktop", false)]
    [InlineData("/org/free.desktop", false)]
    public void Object_paths_follow_the_specification(string path, bool valid) => Assert.Equal(valid, DBusNames.IsObjectPath(path));

    [Theory]
    [InlineData("org.freedesktop.Notifications", true)]
    [InlineData("org.kde.StatusNotifierItem", true)]
    [InlineData("a_b.C1", true)]
    [InlineData("Notifications", false)]
    [InlineData("org..Notifications", false)]
    [InlineData(".org.Notifications", false)]
    [InlineData("org.1Notifications", false)]
    [InlineData("org.free-desktop.X", false)]
    public void Interface_names_follow_the_specification(string name, bool valid) => Assert.Equal(valid, DBusNames.IsInterfaceName(name));

    [Theory]
    [InlineData(":1.42", true)]
    [InlineData(":1.42-a", true)]
    [InlineData("org.freedesktop.portal.Desktop", true)]
    [InlineData("org.free-desktop.X", true)]
    [InlineData("org.1x", false)]
    [InlineData("org", false)]
    [InlineData(".org.x", false)]
    [InlineData(":", false)]
    public void Bus_names_follow_the_specification(string name, bool valid) => Assert.Equal(valid, DBusNames.IsBusName(name));

    [Theory]
    [InlineData("Notify", true)]
    [InlineData("Get_All2", true)]
    [InlineData("", false)]
    [InlineData("2Notify", false)]
    [InlineData("a.b", false)]
    [InlineData("a-b", false)]
    public void Member_names_follow_the_specification(string name, bool valid) => Assert.Equal(valid, DBusNames.IsMemberName(name));

    [Fact]
    public void Names_longer_than_255_are_refused()
    {
        var longest = "a." + new string('b', 253);

        Assert.True(DBusNames.IsInterfaceName(longest));
        Assert.False(DBusNames.IsInterfaceName(longest + "b"));
        Assert.False(DBusNames.IsBusName(longest + "b"));
        Assert.False(DBusNames.IsMemberName(new string('m', 256)));
    }

    [Fact]
    public void The_id_of_the_app_is_a_bus_name_and_gives_an_object_path()
    {
        Assert.True(DBusNames.IsBusName(DBusNames.AppId));
        Assert.True(DBusNames.IsInterfaceName(DBusNames.AppId));
        Assert.Equal("/io/github/quelquun667/Hindsound", DBusNames.ObjectPathOf(DBusNames.AppId));
        Assert.True(DBusNames.IsObjectPath(DBusNames.ObjectPathOf(DBusNames.AppId)));
    }

    [Fact]
    public void A_portal_request_is_known_by_the_unique_name_and_the_token()
    {
        Assert.Equal("/org/freedesktop/portal/desktop/request/1_42/hindsound_ab12", DBusNames.PortalRequestPath(":1.42", "hindsound_ab12"));
        Assert.Throws<ArgumentException>(() => DBusNames.PortalRequestPath("org.freedesktop.portal.Desktop", "hindsound_ab12"));
        Assert.Throws<ArgumentException>(() => DBusNames.PortalRequestPath(":1.42", "bad-token"));
    }

    [Fact]
    public void Tokens_are_fit_for_a_path_and_differ()
    {
        var first = DBusNames.NewToken();
        var second = DBusNames.NewToken();

        Assert.True(DBusNames.IsToken(first));
        Assert.StartsWith("hindsound_", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_method_with_a_bad_name_is_refused_when_made()
    {
        Assert.Equal("org.freedesktop.DBus.GetId", new BusMethod("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetId").ToString());
        Assert.Throws<ArgumentException>(() => new BusMethod("org", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetId"));
        Assert.Throws<ArgumentException>(() => new BusMethod("org.freedesktop.DBus", "org/freedesktop/DBus", "org.freedesktop.DBus", "GetId"));
        Assert.Throws<ArgumentException>(() => new BusMethod("org.freedesktop.DBus", "/org/freedesktop/DBus", "DBus", "GetId"));
        Assert.Throws<ArgumentException>(() => new BusMethod("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "Get.Id"));
    }

    [Fact]
    public void A_signal_gives_the_rule_the_bus_matches_it_by()
    {
        var path = DBusNames.PortalRequestPath(":1.7", "hindsound_1");

        var rule = new BusSignal("org.freedesktop.portal.Desktop", path, "org.freedesktop.portal.Request", "Response").Rule().ToString();

        Assert.Contains("type=signal", rule, StringComparison.Ordinal);
        Assert.Contains("sender=org.freedesktop.portal.Desktop", rule, StringComparison.Ordinal);
        Assert.Contains("path=" + path, rule, StringComparison.Ordinal);
        Assert.Contains("interface=org.freedesktop.portal.Request", rule, StringComparison.Ordinal);
        Assert.Contains("member=Response", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signal_from_anyone_leaves_the_sender_and_the_path_out_of_its_rule()
    {
        var rule = new BusSignal(null, null, "org.freedesktop.DBus", "NameOwnerChanged").Rule().ToString();

        Assert.DoesNotContain("sender=", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("path=", rule, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new BusSignal("org", null, "org.freedesktop.DBus", "NameOwnerChanged"));
    }

    private sealed class Pinged : BusObject
    {
        public Pinged()
            : base("/io/github/quelquun667/Hindsound/Test") => On("io.github.quelquun667.Hindsound.Test", "Ping", _ => { });
    }

    [Fact]
    public void An_object_answers_only_the_methods_it_names()
    {
        var pinged = new Pinged();

        Assert.False(pinged.HandlesChildPaths);
        Assert.NotNull(pinged.Find("io.github.quelquun667.Hindsound.Test", "Ping"));
        Assert.Null(pinged.Find("io.github.quelquun667.Hindsound.Test", "Pong"));
        Assert.Null(pinged.Find("org.freedesktop.DBus.Properties", "Ping"));
        Assert.Null(pinged.Find(null, "Ping"));
    }
}
