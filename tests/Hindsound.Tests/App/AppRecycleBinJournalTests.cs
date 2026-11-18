using System.IO;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Library;
using Hindsound.Tests.Export;
using Hindsound.Tests.Ui.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>The Recycle Bin the shell makes for the Library and the editor is told the log of the app, and no other.</summary>
public sealed class AppRecycleBinJournalTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ScreenRig _rig = new();

    public void Dispose()
    {
        _rig.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void The_log_the_shell_gives_the_recycle_bin_is_the_log_of_the_app()
    {
        var shell = new BinShell();
        var log = _temp.Combine("settings.log");
        // Not off screen: off screen, no bin of the shell is made.
        var options = AppOptions.Parse(["--settings", _temp.Combine("settings.json"), "--no-mic"]);
        var app = new HindsoundApp(options, new Journal(log), shell: shell)
        {
            OpenInstead = _ => { },
            PostInstead = _ => { },
            LibraryFoldersInstead = () => new ClipScanRequest { OutputFolder = _temp.Combine("clips"), RescueFolder = _temp.Combine("rescued") },
        };

        app.WireScreen(_rig.Screen);

        Assert.NotNull(shell.BinJournal);
        shell.BinJournal!("recycle bin probe line");
        Assert.True(
            SpinWait.SpinUntil(() => File.Exists(log) && Journal.ReadShared(log).Any(line => line.Contains("recycle bin probe line", StringComparison.Ordinal)), TimeSpan.FromSeconds(10)),
            "the line of the bin never reached the log of the app");
    }

    /// <summary>A shell that makes only what the Library and the editor ask for, and keeps the log it gave the bin.</summary>
    private sealed class BinShell : IAppShell
    {
        public Action<string>? BinJournal { get; private set; }

        public IAudioChoices AudioChoices => throw new NotSupportedException();

        public ISettingsProbe Probe => throw new NotSupportedException();

        public IAppLoop NewLoop() => throw new NotSupportedException();

        public INotificationView NewNotification(IAppLoop loop, bool offscreen) => throw new NotSupportedException();

        public ITrayIcon NewTray(IAppLoop loop) => throw new NotSupportedException();

        public ISessionEnd SessionEnd(IAppLoop loop) => throw new NotSupportedException();

        public IClipboardFiles Clipboard(IAppLoop loop) => throw new NotSupportedException();

        public ISettingsWindow? NewSettingsWindow(SettingsWindowRequest request) => null;

        public IFolderShell NewFolderShell() => new OffscreenFolderShell(_ => { });

        public IPreviewPlayer NewPreviewPlayer() => throw new NotSupportedException();

        public IRecycleBin NewRecycleBin(Action<string> journal)
        {
            BinJournal = journal;
            return NoRecycleBin.Instance;
        }
    }
}
