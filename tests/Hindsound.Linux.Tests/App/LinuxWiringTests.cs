using System.Runtime.CompilerServices;
using Hindsound.App;
using Hindsound.Input;
using Hindsound.Linux.Input;
using Hindsound.Linux.Window;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Linux.Tests.App;

/// <summary>
/// The pieces the Linux app hands the pages: the mode of the shortcuts each layer gives, the command the Copy button puts
/// on the clipboard, and the dump of the pages on a bitmap of its own with a text it shares.
/// </summary>
public sealed class LinuxWiringTests
{
    [Fact]
    public void Each_layer_of_the_shortcuts_gives_the_pages_their_mode()
    {
        // Only the type of the layer counts: an object made without its constructor opens nothing.
        var portal = (IHotkeyNative)RuntimeHelpers.GetUninitializedObject(typeof(PortalHotkeys));
        var x11 = (IHotkeyNative)RuntimeHelpers.GetUninitializedObject(typeof(KeyByKeyHotkeys));

        Assert.Equal(ShortcutMode.Portal, HotkeyChoice.ModeOf(portal, pending: false));
        Assert.Equal(ShortcutMode.Portal, HotkeyChoice.ModeOf(portal, pending: true));
        Assert.Equal(ShortcutMode.Capture, HotkeyChoice.ModeOf(x11, pending: false));
        Assert.Equal(ShortcutMode.Portal, HotkeyChoice.ModeOf(null, pending: true));
        Assert.Equal(ShortcutMode.Command, HotkeyChoice.ModeOf(null, pending: false));
    }

    [Theory]
    [InlineData("/home/you/Applications/Hindsound.AppImage", "/home/you/Applications/Hindsound.AppImage --save")]
    [InlineData("/home/you/My apps/Hindsound.AppImage", "\"/home/you/My apps/Hindsound.AppImage\" --save")]
    public void The_command_to_copy_is_the_one_the_notice_names(string program, string command)
    {
        Assert.Equal(command, HotkeyChoice.SaveCommandFor(program));
        Assert.EndsWith(": " + command, HotkeyChoice.NoShortcutTextFor(program), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("hindsound")]
    [InlineData("/home/you/a&b/Hindsound.AppImage")]
    public void A_path_the_notice_cannot_write_gives_no_command(string? program)
    {
        Assert.Null(HotkeyChoice.SaveCommandFor(program));
        Assert.Equal(HotkeyChoice.NoShortcutGenericText, HotkeyChoice.NoShortcutTextFor(program));
    }

    [Fact]
    public void The_dump_writes_every_page_with_one_text_it_leaves_open()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hindsound-linux-dump-" + Guid.NewGuid().ToString("N"));
        var text = new KeptText();
        var log = new List<string>();
        try
        {
            var code = LinuxDump.Write(folder, 1.0, text, log.Add);

            Assert.True(code == 0, string.Join("\n", log));
            var pictures = Directory.GetFiles(folder, "*.png");
            Assert.True(pictures.Length >= 40, pictures.Length + " pictures");
            Assert.All(pictures, path => Assert.True(new FileInfo(path).Length > 100, path));
            Assert.False(text.Disposed);
            Assert.True(text.Drawn > 0);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void A_dump_that_cannot_write_its_folder_fails_and_says_so()
    {
        var file = Path.GetTempFileName();
        var log = new List<string>();
        try
        {
            // A file where the folder should be: no page can be written under it.
            Assert.Equal(3, LinuxDump.Write(Path.Combine(file, "pages"), 1.0, new KeptText(), log.Add));
            Assert.Contains(log, line => line.StartsWith("dump failed: ", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class KeptText : ICanvasText
    {
        public bool Disposed { get; private set; }

        public int Drawn { get; private set; }

        public float Measure(FontSpec font, string text) => text.Length * font.Size * 0.5f;

        public float LineHeight(FontSpec font) => MathF.Round(font.Size * 1.25f);

        public void Draw(Pixels target, FontSpec font, string text, float x, float top, uint rgb)
        {
            Drawn++;
            target.FillRect((int)x, (int)top, (int)x + 1, (int)top + 1, rgb);
        }

        public void Dispose() => Disposed = true;
    }
}
