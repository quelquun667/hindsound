using Hindsound.Settings;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// A full folder path is a Windows spelling here (a drive letter, a %VARIABLE%): on another system the same text is a relative
/// path, so this is read on Windows.
/// </summary>
public class SettingsEditorFolderTests
{
    private readonly List<SettingsChange> _changes = [];

    private SettingsEditor Editor(AppSettings? settings = null) =>
        new((settings ?? new AppSettings()).Normalize(), _changes.Add);

    [Theory]
    [InlineData(@"D:\Clips", true)]
    [InlineData(@"%USERPROFILE%\Clips", true)]
    [InlineData("", false)]
    [InlineData("Clips", false)]
    public void Only_a_full_folder_path_is_kept(string path, bool kept)
    {
        var editor = Editor();

        var check = editor.SetFolder(path);

        Assert.Equal(kept, check.Ok);
        Assert.Equal(kept ? path : ExportSettings.DefaultFolder, editor.Settings.Export.OutputFolder);
    }
}
