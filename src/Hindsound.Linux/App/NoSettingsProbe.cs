using Hindsound.App;
using Hindsound.Ui.Window;

namespace Hindsound.Linux.App;

/// <summary>No window in the Linux app, so nothing for <c>--settings-probe</c> to count or press.</summary>
internal sealed class NoSettingsProbe : ISettingsProbe
{
    public uint DrawingObjects() => 0;

    public uint WindowObjects() => 0;

    public void Key(ISettingsWindow window, uint vk) => throw NoWindow();

    public void KeyInTextField(ISettingsWindow window, uint vk) => throw NoWindow();

    public void TypeInTextField(ISettingsWindow window, string text) => throw NoWindow();

    public void Activate(ISettingsWindow window) => throw NoWindow();

    public void ScaleChange(ISettingsWindow window, uint dpi) => throw NoWindow();

    public string ClientSize(ISettingsWindow window) => string.Empty;

    public string IconWidths(ISettingsWindow window) => string.Empty;

    public bool FolderDialogUsable() => false;

    private static NotSupportedException NoWindow() => new("the Linux app has no window");
}
