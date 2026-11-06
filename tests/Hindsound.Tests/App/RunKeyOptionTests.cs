using Hindsound.App;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>A test run with its own settings file has no way to the startup entry of the user. Nothing here reads or writes the registry.</summary>
public class RunKeyOptionTests
{
    [Fact]
    public void A_run_with_its_own_settings_file_gets_no_run_key()
    {
        Assert.Null(AppOptions.Parse(["--settings", "settings.json"]).RunKey());
    }

    [Fact]
    public void An_ordinary_run_gets_the_real_one()
    {
        Assert.IsType<RegistryRunKey>(AppOptions.Parse([]).RunKey());
    }
}
