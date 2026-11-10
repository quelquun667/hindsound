using Hindsound.Settings;

namespace Hindsound.Tests.Settings;

public class StartupRegistrationTests
{
    private sealed class FakeRunKey : IRunKeyValues
    {
        public Dictionary<string, string> Values { get; } = [];

        public bool Throw { get; set; }

        public string? GetValue(string name) => Values.GetValueOrDefault(name);

        public void SetValue(string name, string value)
        {
            if (Throw) throw new UnauthorizedAccessException();
            Values[name] = value;
        }

        public void DeleteValue(string name)
        {
            if (Throw) throw new UnauthorizedAccessException();
            Values.Remove(name);
        }
    }

    private const string Exe = @"C:\Apps\Hindsound\Hindsound.exe";

    [Fact]
    public void Enable_WritesTheQuotedExePath()
    {
        var key = new FakeRunKey();
        var startup = new StartupRegistration(new RegistryRunKey(key), Exe);

        Assert.True(startup.Enable());

        Assert.Equal("\"C:\\Apps\\Hindsound\\Hindsound.exe\"", key.Values["Hindsound"]);
        Assert.True(startup.IsEnabled());
    }

    [Fact]
    public void Disable_RemovesOnlyOurValue()
    {
        var key = new FakeRunKey();
        key.Values["Other"] = "x";
        var startup = new StartupRegistration(new RegistryRunKey(key), Exe);
        startup.Enable();

        Assert.True(startup.Disable());

        Assert.False(startup.IsEnabled());
        Assert.Equal(["Other"], key.Values.Keys);
    }

    [Fact]
    public void Disable_WhenNothingIsRegistered_Succeeds()
    {
        Assert.True(new StartupRegistration(new RegistryRunKey(new FakeRunKey()), Exe).Disable());
    }

    [Fact]
    public void StalePath_IsNotEnabled_AndApplyRewritesIt()
    {
        var key = new FakeRunKey();
        key.Values["Hindsound"] = "\"D:\\Old\\Hindsound.exe\"";
        var startup = new StartupRegistration(new RegistryRunKey(key), Exe);

        Assert.False(startup.IsEnabled());
        Assert.True(startup.Apply(true));
        Assert.Equal("\"C:\\Apps\\Hindsound\\Hindsound.exe\"", key.Values["Hindsound"]);
    }

    [Fact]
    public void Apply_FollowsTheSetting()
    {
        var key = new FakeRunKey();
        var startup = new StartupRegistration(new RegistryRunKey(key), Exe);

        startup.Apply(true);
        Assert.True(startup.IsEnabled());
        startup.Apply(false);
        Assert.Empty(key.Values);
    }

    [Fact]
    public void RegistryRefusal_BecomesFalse()
    {
        var startup = new StartupRegistration(new RegistryRunKey(new FakeRunKey { Throw = true }), Exe);

        Assert.False(startup.Enable());
        Assert.False(startup.Disable());
    }

    [Fact]
    public void WithoutAnExePath_NothingIsWritten()
    {
        var key = new FakeRunKey();
        var startup = new StartupRegistration(new RegistryRunKey(key), "   ");

        Assert.False(startup.Enable());
        Assert.False(startup.IsEnabled());
        Assert.Empty(key.Values);
    }

    [Fact]
    public void DefaultPath_IsTheCurrentProcess()
    {
        var key = new FakeRunKey();
        var startup = new StartupRegistration(new RegistryRunKey(key));

        Assert.True(startup.Enable());
        Assert.Equal($"\"{Environment.ProcessPath}\"", key.Values["Hindsound"]);
    }
}
