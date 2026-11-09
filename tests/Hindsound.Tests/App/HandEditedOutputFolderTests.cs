using System.IO;
using Hindsound.App;
using Hindsound.Settings;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.App;

/// <summary>
/// The output folder changed in the settings file by hand, while the app runs, is taken in when the window comes back to
/// the front. The app is built and never run, so what comes after the settings are taken in (the tray) stops at its catch.
/// </summary>
public sealed class HandEditedOutputFolderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private HindsoundApp Built()
    {
        var options = AppOptions.Parse(["--settings", _temp.Combine("settings.json"), "--no-mic", "--offscreen"]);
        return new HindsoundApp(options, new Journal(_temp.Combine("settings.log")))
        {
            OpenInstead = _ => { },
            LoadChoicesInstead = () => { },
        };
    }

    private void Edit(Action<AppSettings> change)
    {
        var edited = new AppSettings();
        change(edited);
        new SettingsStore(_temp.Combine("settings.json")).Save(edited);
    }

    [Fact]
    public void An_output_folder_edited_by_hand_keeps_the_old_one_in_the_list_of_folders_to_read()
    {
        var app = Built();
        var before = _temp.Combine("before");
        var after = _temp.Combine("after");
        app.SettingsNow.Export.OutputFolder = before;
        Edit(settings => settings.Export.OutputFolder = after);

        app.OnWindowActivated();

        Assert.Equal(after, app.SettingsNow.Export.OutputFolder);
        Assert.Equal([before], app.SettingsNow.Export.KnownClipFolders);
    }

    [Fact]
    public void The_list_written_in_the_file_stays_behind_the_old_folder()
    {
        var app = Built();
        var before = _temp.Combine("before");
        var older = _temp.Combine("older");
        app.SettingsNow.Export.OutputFolder = before;
        Edit(settings =>
        {
            settings.Export.OutputFolder = _temp.Combine("after");
            settings.Export.KnownClipFolders = [older];
        });

        app.OnWindowActivated();

        Assert.Equal([before, older], app.SettingsNow.Export.KnownClipFolders);
    }

    [Fact]
    public void Another_setting_edited_by_hand_leaves_the_list_as_the_file_has_it()
    {
        var app = Built();
        var folder = _temp.Combine("same");
        app.SettingsNow.Export.OutputFolder = folder;
        Edit(settings =>
        {
            settings.Export.OutputFolder = folder;
            settings.ClipSeconds = 45;
        });

        app.OnWindowActivated();

        Assert.Equal(45, app.SettingsNow.ClipSeconds);
        Assert.Null(app.SettingsNow.Export.KnownClipFolders);
    }
}
