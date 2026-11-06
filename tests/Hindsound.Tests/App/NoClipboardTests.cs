using Hindsound.App;
using Hindsound.Settings;

namespace Hindsound.Tests.App;

/// <summary>The whole way from the command line and the file to the attempt at the clipboard.</summary>
public sealed class NoClipboardTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-noclipboard-").FullName;

    private string Path => System.IO.Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_option_is_read_and_off_by_default()
    {
        Assert.True(AppOptions.Parse(["--no-clipboard"]).NoClipboard);
        Assert.False(AppOptions.Parse([]).NoClipboard);
    }

    [Fact]
    public void A_file_that_asks_for_the_copy_gets_no_attempt_at_the_start_or_after_a_read_and_stays_intact()
    {
        File.WriteAllText(Path, """{"export":{"copyToClipboard":true}}""");
        var before = File.ReadAllBytes(Path);
        var options = AppOptions.Parse(["--settings", Path, "--no-clipboard"]);
        var store = new SettingsStore(options.SettingsPath);

        var settings = store.Load();

        Assert.True(settings.Export.CopyToClipboard);
        Assert.False(SettingsMapping.CopyWanted(settings, options));
        Assert.Equal(before, File.ReadAllBytes(Path));

        File.WriteAllText(Path, """{"export":{"copyToClipboard":true,"fileNameTemplate":"other"}}""");
        var read = store.Read();

        Assert.True(read.Settings!.Export.CopyToClipboard);
        Assert.False(SettingsMapping.CopyWanted(read.Settings, options));

        // Copy last clip does not ask the settings: it goes straight to this.
        var attempts = 0;
        bool? answer = null;
        SettingsMapping.CopyToClipboard(options, () => { attempts++; return true; }, (_, step) => step(), copied => answer = copied);

        Assert.Equal(0, attempts);
        Assert.False(answer);
    }

    [Fact]
    public void Without_the_option_the_settings_decide_and_the_copy_is_attempted()
    {
        var settings = new AppSettings();
        var options = new AppOptions();

        Assert.True(SettingsMapping.CopyWanted(settings, options));
        settings.Export.CopyToClipboard = false;
        Assert.False(SettingsMapping.CopyWanted(settings, options));

        var attempts = 0;
        bool? answer = null;
        SettingsMapping.CopyToClipboard(options, () => { attempts++; return true; }, (_, step) => step(), copied => answer = copied);

        Assert.Equal(1, attempts);
        Assert.True(answer);
    }
}
