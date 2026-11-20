using System.Runtime.InteropServices;
using Hindsound.App;
using Hindsound.Input;
using Hindsound.Library;
using Hindsound.Settings;
using Hindsound.Ui;
using Hindsound.Ui.Window;
using Hindsound.Updates;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// The pages drawn through the portable canvas, with the features of each system: what a Linux desktop hides (one application,
/// the look and edge of the drawn notification, the confirmation sound), the words it says its own way, and the three ways its
/// shortcuts are shown. The Windows features are today's pages, and stay so.
/// </summary>
public sealed unsafe class PlatformPagesTests
{
    private const string DesktopCommand = "\"/opt/My Apps/hindsound\" --save";

    private static readonly PlatformFeatures Windows = PlatformFeatures.Windows;
    private static readonly PlatformFeatures X11 = PlatformFeatures.Linux(() => ShortcutMode.Capture);
    private static readonly PlatformFeatures Portal = PlatformFeatures.Linux(() => ShortcutMode.Portal);
    private static readonly PlatformFeatures Command = PlatformFeatures.Linux(() => ShortcutMode.Command, () => DesktopCommand);

    /// <summary>A text of fixed advance that keeps what it was asked to draw, line by line.</summary>
    private sealed class RecordingText : ICanvasText
    {
        public List<string> Drawn { get; } = [];

        public float Measure(FontSpec font, string text) => text.Length * 0.6f * MathF.Floor(font.Size + 0.5f);

        public float LineHeight(FontSpec font) => MathF.Round(1.25f * MathF.Floor(font.Size + 0.5f));

        public void Draw(Pixels target, FontSpec font, string text, float x, float top, uint rgb) => Drawn.Add(text);

        public void Dispose()
        {
        }
    }

    /// <summary>Counts the notification previews it is asked for, which a canvas of a desktop with its own notification draws none of.</summary>
    private sealed unsafe class CountingCanvas(uint* bits, int width, int height, ICanvasText text) : PixelCanvas(bits, width, height, 1, text)
    {
        public int Previews { get; set; }

        public override void Notification(NotificationContent content, float x, float y, uint accent, bool mirrored)
        {
            Previews++;
            base.Notification(content, x, y, accent, mirrored);
        }
    }

    private sealed class QuietBackend : ISettingsBackend
    {
        public void Applied(SettingsChange change)
        {
        }

        public void SaveNow()
        {
        }

        public void TogglePause()
        {
        }

        public void CopyLast()
        {
        }

        public void OpenFolder()
        {
        }

        public void ShowTestNotification()
        {
        }

        public void SuspendHotkeys()
        {
        }

        public IReadOnlyList<HotkeyResult> ApplyHotkeys(HotkeySettings hotkeys) => [];

        public IReadOnlyList<HotkeyResult> ResumeHotkeys() => [];
    }

    private sealed class RecordingHost : IWindowHost
    {
        public List<string> Copied { get; } = [];

        public void Invalidate()
        {
        }

        public void ShowEdit(Box box, string text, Face face, float size, bool resetText)
        {
        }

        public void HideEdit()
        {
        }

        public bool IsForeground => false;

        public void FocusEdit()
        {
        }

        public void CaptureMouse()
        {
        }

        public void ReleaseMouse()
        {
        }

        public void MeterWanted(bool wanted)
        {
        }

        public string? PickFolder(string startFolder) => null;

        public void CloseWindow()
        {
        }

        public void CopyText(string text) => Copied.Add(text);
    }

    /// <summary>A screen on a canvas of its own, as a window of the system would have.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly uint* _bits;
        private readonly RecordingText _text = new();

        public Rig(PlatformFeatures features, bool firstRun = false, AppSettings? settings = null)
        {
            _bits = (uint*)NativeMemory.AllocZeroed((nuint)(760 * 488), sizeof(uint));
            Canvas = new CountingCanvas(_bits, 760, 488, _text);
            Screen = new SettingsScreen(settings ?? new AppSettings().Normalize(), new QuietBackend(), Host, Canvas, firstRun, features)
            {
                NoNativeEdit = true,
            };
        }

        public CountingCanvas Canvas { get; }

        public RecordingHost Host { get; } = new();

        public SettingsScreen Screen { get; }

        /// <summary>Every text the window draws now, in the order it draws them.</summary>
        public List<string> Texts()
        {
            _text.Drawn.Clear();
            Canvas.Previews = 0;
            Screen.Paint(Canvas);
            return [.. _text.Drawn];
        }

        public bool Has(string id) => Screen.WidgetById(id) is not null;

        public void Click(string id)
        {
            var box = Screen.WidgetById(id)!.Bounds;
            Screen.MouseDown(box.MidX, box.MidY);
            Screen.MouseUp(box.MidX, box.MidY);
        }

        public void Dispose() => NativeMemory.Free(_bits);
    }

    private static HotkeyResult[] Answers(string? save, HotkeyStatus status = HotkeyStatus.Registered) =>
        [.. Enum.GetValues<HotkeyAction>().Select(a => new HotkeyResult(a, a == HotkeyAction.SaveDefault ? save : "Ctrl+Alt+" + (char)('A' + (int)a), status, null))];

    // ---------------------------------------------------------------- Windows keeps today's pages

    [Fact]
    public void Windows_shows_every_choice_in_its_own_words()
    {
        using var rig = new Rig(Windows);
        rig.Screen.ShowSection(Section.General);
        var texts = rig.Texts();
        Assert.Contains("Start with Windows", texts);
        Assert.Contains("Notification position", texts);
        Assert.Contains("Confirmation sound", texts);
        Assert.Contains("Show test notification", texts);
        Assert.True(rig.Has("general.position") && rig.Has("general.sound") && rig.Has("general.test"));
        Assert.Equal(1, rig.Canvas.Previews);

        rig.Screen.ShowSection(Section.Audio);
        Assert.True(rig.Has("audio.record"));
        Assert.Contains("Record", rig.Texts());

        rig.Screen.ShowSection(Section.Hotkeys);
        Assert.True(rig.Has("hotkey." + HotkeyAction.SaveDefault) && rig.Has("hotkey.reset"));
        Assert.False(rig.Has(SettingsScreen.HotkeyCommandId));
    }

    [Fact]
    public void The_features_of_a_screen_made_with_none_are_those_of_Windows()
    {
        using var rig = new Rig(null!);
        Assert.Same(PlatformDefaults.Features ?? PlatformFeatures.Windows, rig.Screen.Features);
        Assert.Equal(ShortcutMode.Capture, rig.Screen.Features.Shortcuts());
        Assert.Equal("Recycle Bin", rig.Screen.Features.TrashName);
        Assert.Equal(NameBoxState.ClipReservedText, rig.Screen.Features.ReservedNameText);
    }

    [Fact]
    public void The_words_of_the_Windows_features_change_no_sentence()
    {
        Assert.Equal(NameBoxState.TwoFilesText, Windows.Words(NameBoxState.TwoFilesText));
        Assert.Equal(SettingsScreen.EditorSavedAside, Windows.Words(SettingsScreen.EditorSavedAside));
    }

    // ---------------------------------------------------------------- what a Linux desktop hides, and says its own way

    [Theory]
    [InlineData("Capture")]
    [InlineData("Portal")]
    [InlineData("Command")]
    public void Linux_hides_the_notification_look_and_the_sound_and_offers_the_single_app_in_every_mode(string modeName)
    {
        var mode = Enum.Parse<ShortcutMode>(modeName);
        var settings = new AppSettings().Normalize();
        settings.Audio.SourceApp = "Discord.exe";
        using var rig = new Rig(PlatformFeatures.Linux(() => mode), settings: settings);

        rig.Screen.ShowSection(Section.General);
        var general = rig.Texts();
        Assert.Equal(0, rig.Canvas.Previews);
        Assert.False(rig.Has("general.position"));
        Assert.False(rig.Has("general.sound"));
        Assert.DoesNotContain("Notification position", general);
        Assert.DoesNotContain("Confirmation sound", general);
        Assert.DoesNotContain(SettingsScreen.SoundHelp, general);

        // The desktop's notification is still shown by this button, and the toggles that still work stay.
        Assert.True(rig.Has("general.test"));
        Assert.True(rig.Has("general.notifications") && rig.Has("general.fullscreen") && rig.Has("general.size"));

        // One application is recorded alone as on Windows: the choice is there, and the output is greyed while it is set.
        rig.Screen.ShowSection(Section.Audio);
        var audio = rig.Texts();
        Assert.True(rig.Has("audio.record"));
        Assert.Contains("Record", audio);
        Assert.False(rig.Screen.WidgetById("audio.output")!.Enabled);
    }

    [Fact]
    public void Windows_greys_the_output_while_one_application_is_recorded()
    {
        var settings = new AppSettings().Normalize();
        settings.Audio.SourceApp = "Discord.exe";
        using var windows = new Rig(Windows, settings: settings);
        windows.Screen.ShowSection(Section.Audio);
        Assert.False(windows.Screen.WidgetById("audio.output")!.Enabled);
    }

    [Fact]
    public void Linux_says_the_session_and_the_Trash_and_no_Windows_word_on_the_settings_pages()
    {
        using var rig = new Rig(X11);
        var seen = new List<string>();
        foreach (var section in new[] { Section.Hotkeys, Section.Durations, Section.Audio, Section.Export, Section.General })
        {
            rig.Screen.ShowSection(section);
            seen.AddRange(rig.Texts());
        }

        Assert.Contains("Start with the session", seen);
        Assert.DoesNotContain("Start with Windows", seen);
        Assert.DoesNotContain(seen, text => text.Contains("Windows", StringComparison.Ordinal) || text.Contains("Explorer", StringComparison.Ordinal) || text.Contains("Recycle", StringComparison.Ordinal));
        Assert.Equal("Trash", X11.TrashName);
        Assert.Equal("The Trash can't take this clip. It stays where it is.", X11.Words(NameBoxState.CannotRecycleText));
        Assert.Equal("This clip is 2 files, the system sound and the microphone. Both files go to the Trash.", X11.Words(NameBoxState.TwoFilesText));
        Assert.Equal("The original is in the Trash. The edit was saved as a new clip.", X11.Words(SettingsScreen.EditorSavedAside));
        Assert.Equal("This name is reserved", NameBoxState.ClipRefusalText(ClipRenameOutcome.Reserved, X11));
        Assert.Equal("This name is reserved.", EditorSaveDialogState.MessageOf(ClipRenameOutcome.Reserved, X11));
        Assert.Equal(NameBoxState.ClipReservedText, NameBoxState.ClipRefusalText(ClipRenameOutcome.Reserved));
    }

    [Fact]
    public void The_first_launch_card_says_the_session_too()
    {
        using var rig = new Rig(X11, firstRun: true);
        var texts = rig.Texts();
        Assert.Contains("Start with the session", texts);
        Assert.DoesNotContain("Start with Windows", texts);
        Assert.True(rig.Has("run.startup"));

        using var windows = new Rig(Windows, firstRun: true);
        Assert.Contains("Start with Windows", windows.Texts());
    }

    // ---------------------------------------------------------------- the shortcuts, by mode

    [Fact]
    public void On_X11_a_key_is_captured_as_on_Windows()
    {
        using var rig = new Rig(X11);
        rig.Screen.ShowSection(Section.Hotkeys);
        foreach (var (action, _) in SettingsEditor.HotkeyRows)
        {
            Assert.True(rig.Has("hotkey." + action), action.ToString());
        }

        Assert.True(rig.Has("hotkey.reset"));
        Assert.False(rig.Has(SettingsScreen.HotkeyCommandId) || rig.Has(SettingsScreen.HotkeyCopyId) || rig.Has(SettingsScreen.HotkeyNoteId));
    }

    [Fact]
    public void Under_the_portal_the_page_shows_the_keys_the_desktop_chose_and_has_nothing_to_type_in()
    {
        using var rig = new Rig(Portal);
        rig.Screen.ShowSection(Section.Hotkeys);

        // Nothing has answered yet.
        Assert.Contains("Waiting for your desktop", rig.Texts());

        rig.Screen.SetHotkeyResults(Answers("Ctrl+Shift+S"));
        var texts = rig.Texts();
        foreach (var (action, _) in SettingsEditor.HotkeyRows)
        {
            Assert.False(rig.Has("hotkey." + action), action.ToString());
            Assert.True(rig.Has(SettingsScreen.HotkeyShownPrefix + action), action.ToString());
        }

        Assert.False(rig.Has("hotkey.reset"));
        Assert.True(rig.Has(SettingsScreen.HotkeyNoteId));
        Assert.Contains(SettingsScreen.DesktopKeysNote, string.Join(' ', texts));

        // Keys the app can read are drawn as keys; the first action is the main save one.
        Assert.Contains("Shift", texts);
        Assert.Contains("S", texts);
        Assert.DoesNotContain("Waiting for your desktop", texts);
        Assert.Equal(Enum.GetValues<HotkeyAction>().Length, texts.Count(t => t == "Ctrl"));
    }

    [Fact]
    public void Under_the_portal_words_the_app_cannot_read_are_shown_as_the_desktop_wrote_them()
    {
        using var rig = new Rig(Portal);
        rig.Screen.ShowSection(Section.Hotkeys);
        rig.Screen.SetHotkeyResults(Answers("Press <Super>F9 to save"));
        var texts = rig.Texts();
        Assert.Contains("Press <Super>F9 to save", texts);
        Assert.DoesNotContain("Super", texts);
    }

    [Fact]
    public void Under_the_portal_an_action_the_desktop_left_unbound_says_so()
    {
        using var rig = new Rig(Portal);
        rig.Screen.ShowSection(Section.Hotkeys);
        var results = Answers("Ctrl+Alt+H").Select(r => r.Action == HotkeyAction.Save5m ? r with { Gesture = null, Status = HotkeyStatus.Unavailable } : r).ToArray();
        rig.Screen.SetHotkeyResults(results);
        Assert.Contains("Not bound by your desktop", rig.Texts());

        rig.Screen.SetHotkeyResults(Answers("Ctrl+Alt+H", HotkeyStatus.Suspended));
        Assert.Contains("Waiting for your desktop", rig.Texts());
    }

    [Fact]
    public void The_save_button_names_the_key_of_the_desktop_and_none_where_there_is_none()
    {
        using var captured = new Rig(Windows);
        Assert.Contains("Ctrl+Shift+F10", captured.Texts());

        using var portal = new Rig(Portal);
        portal.Screen.SetHotkeyResults(Answers("Ctrl+Alt+Q"));
        Assert.Contains("Ctrl+Alt+Q", portal.Texts());
        Assert.DoesNotContain("Ctrl+Shift+F10", portal.Texts());

        using var waiting = new Rig(Portal);
        Assert.DoesNotContain("Ctrl+Shift+F10", waiting.Texts());

        using var command = new Rig(Command);
        Assert.DoesNotContain("Ctrl+Shift+F10", command.Texts());
        Assert.True(command.Has("band.save"));
    }

    [Fact]
    public void With_no_shortcut_at_all_the_page_gives_the_command_to_bind_and_a_button_that_copies_it()
    {
        using var rig = new Rig(Command);
        rig.Screen.ShowSection(Section.Hotkeys);
        var texts = rig.Texts();
        Assert.Contains(SettingsScreen.CommandIntro, string.Join(' ', texts));
        Assert.Contains(DesktopCommand, texts);
        Assert.Contains("Copy", texts);
        Assert.Contains(SettingsScreen.CommandNote, string.Join(' ', texts));
        Assert.True(rig.Has(SettingsScreen.HotkeyCommandId) && rig.Has(SettingsScreen.HotkeyCopyId));
        foreach (var (action, _) in SettingsEditor.HotkeyRows)
        {
            Assert.False(rig.Has("hotkey." + action) || rig.Has(SettingsScreen.HotkeyShownPrefix + action), action.ToString());
        }

        Assert.False(rig.Has("hotkey.reset"));

        rig.Click(SettingsScreen.HotkeyCopyId);
        Assert.Equal([DesktopCommand], rig.Host.Copied);
        Assert.Contains("Copied", rig.Texts());

        // Another page and back: the word of the button is the first again.
        rig.Screen.ShowSection(Section.Durations);
        rig.Screen.ShowSection(Section.Hotkeys);
        Assert.DoesNotContain("Copied", rig.Texts());
    }

    [Fact]
    public void A_program_that_cannot_be_named_in_a_command_gets_the_words_and_nothing_to_copy()
    {
        var unnamed = PlatformFeatures.Linux(() => ShortcutMode.Command, () => null);
        using var rig = new Rig(unnamed);
        rig.Screen.ShowSection(Section.Hotkeys);
        var texts = rig.Texts();
        var all = string.Join(' ', texts);
        Assert.Contains(SettingsScreen.CommandGenericIntro, all);
        Assert.DoesNotContain(SettingsScreen.CommandIntro, all);
        Assert.Contains(SettingsScreen.CommandNote, all);
        Assert.DoesNotContain("--save", texts);
        Assert.DoesNotContain("hindsound --save", all);
        // The band of the last clip has a Copy button of its own: the one of the command is told by its id.
        Assert.False(rig.Has(SettingsScreen.HotkeyCommandId) || rig.Has(SettingsScreen.HotkeyCopyId));
        Assert.Empty(rig.Host.Copied);

        // The first launch says the same, with nothing to copy either.
        using var first = new Rig(unnamed, firstRun: true);
        var firstTexts = first.Texts();
        Assert.Contains(SettingsScreen.CommandGenericIntro, string.Join(' ', firstTexts));
        Assert.DoesNotContain("hindsound --save", string.Join(' ', firstTexts));
        Assert.False(first.Has(SettingsScreen.HotkeyCommandId) || first.Has(SettingsScreen.HotkeyCopyId));
    }

    [Fact]
    public void The_copy_button_works_from_the_keyboard()
    {
        using var rig = new Rig(Command);
        rig.Screen.ShowSection(Section.Hotkeys);
        Assert.True(rig.Screen.TabTo(SettingsScreen.HotkeyCopyId));
        rig.Screen.Key(Vk.Enter, 0);
        Assert.Equal([DesktopCommand], rig.Host.Copied);
    }

    [Fact]
    public void The_default_command_is_the_bare_one()
    {
        Assert.Equal("hindsound --save", PlatformFeatures.Linux(() => ShortcutMode.Command).SaveCommand());
        Assert.Equal("hindsound --save", PlatformFeatures.DefaultSaveCommand);
    }

    [Fact]
    public void A_mode_known_only_later_is_read_again_at_each_build()
    {
        var mode = ShortcutMode.Portal;
        using var rig = new Rig(PlatformFeatures.Linux(() => mode));
        rig.Screen.ShowSection(Section.Hotkeys);
        Assert.True(rig.Has(SettingsScreen.HotkeyShownPrefix + HotkeyAction.SaveDefault));

        // The portal answered that it has nothing to give: the next result rebuilds the page as the command.
        mode = ShortcutMode.Command;
        rig.Screen.SetHotkeyResults(Answers(null, HotkeyStatus.Unavailable));
        Assert.True(rig.Has(SettingsScreen.HotkeyCopyId));
        Assert.False(rig.Has(SettingsScreen.HotkeyShownPrefix + HotkeyAction.SaveDefault));
    }

    // ---------------------------------------------------------------- the first launch card

    [Fact]
    public void The_first_launch_card_asks_for_a_key_to_type_only_where_one_can_be_typed()
    {
        using var windows = new Rig(Windows, firstRun: true);
        Assert.True(windows.Has("hotkey." + HotkeyAction.SaveDefault));
        Assert.Equal("hotkey." + HotkeyAction.SaveDefault, windows.Screen.FocusId);

        using var x11 = new Rig(X11, firstRun: true);
        Assert.True(x11.Has("hotkey." + HotkeyAction.SaveDefault));
        Assert.Equal("hotkey." + HotkeyAction.SaveDefault, x11.Screen.FocusId);

        using var portal = new Rig(Portal, firstRun: true);
        portal.Screen.SetHotkeyResults(Answers("Ctrl+Alt+Q"));
        Assert.False(portal.Has("hotkey." + HotkeyAction.SaveDefault));
        Assert.True(portal.Has(SettingsScreen.HotkeyShownPrefix + HotkeyAction.SaveDefault));
        Assert.Equal("run.next", portal.Screen.FocusId);
        Assert.Contains(SettingsScreen.DesktopKeysNote, string.Join(' ', portal.Texts()));

        using var command = new Rig(Command, firstRun: true);
        Assert.False(command.Has("hotkey." + HotkeyAction.SaveDefault));
        Assert.True(command.Has(SettingsScreen.HotkeyCopyId) && command.Has(SettingsScreen.HotkeyCommandId));
        Assert.Equal("run.next", command.Screen.FocusId);
        Assert.Contains(DesktopCommand, command.Texts());
        command.Click(SettingsScreen.HotkeyCopyId);
        Assert.Equal([DesktopCommand], command.Host.Copied);
    }

    [Fact]
    public void The_last_step_of_the_first_launch_names_what_to_press_by_mode()
    {
        static List<string> LastStep(PlatformFeatures features, HotkeyResult[]? answers)
        {
            using var rig = new Rig(features, firstRun: true);
            if (answers is not null)
            {
                rig.Screen.SetHotkeyResults(answers);
            }

            rig.Click("run.next");
            rig.Click("run.next");
            return rig.Texts();
        }

        var windows = LastStep(Windows, null);
        Assert.Contains(SettingsScreen.FirstRunSay, windows);
        Assert.Contains("Ctrl", windows);

        var portal = LastStep(Portal, Answers("Ctrl+Alt+Q"));
        Assert.Contains(SettingsScreen.FirstRunSay, portal);
        Assert.Contains("Alt", portal);

        var command = LastStep(Command, null);
        Assert.Contains(SettingsScreen.FirstRunSayCommand, command);
        Assert.DoesNotContain(SettingsScreen.FirstRunSay, command);
        Assert.DoesNotContain("Ctrl", command.Where(t => t != "Ctrl+V" && t != "Ctrl"));
    }

    // ---------------------------------------------------------------- the updates by the Linux channel

    [Fact]
    public void The_updates_of_a_desktop_that_cannot_install_only_tell()
    {
        using var rig = new Rig(Portal);
        var panel = new AnnouncingPanel();
        rig.Screen.SetUpdates(panel);
        rig.Screen.ShowUpdates();
        var texts = rig.Texts();
        Assert.True(rig.Has("updates.download"));
        Assert.False(rig.Has("updates.now"));
        Assert.False(rig.Has("updates.whatsnew"));
        Assert.Contains("Download", texts);
        Assert.Contains("Version 0.2.0 is available.", texts);
        Assert.Contains(SettingsScreen.GitHubNote, string.Join(' ', texts));
        Assert.DoesNotContain(texts, text => text.Contains("Windows", StringComparison.Ordinal));

        Assert.True(rig.Screen.TabTo("updates.download"));
        rig.Screen.Key(Vk.Enter, 0);
        Assert.Equal(1, panel.Opened);
    }

    private sealed class AnnouncingPanel : IUpdatesPanel
    {
        private static readonly UpdateVersion Installed = new(0, 1, 0), Next = new(0, 2, 0);

        public int Opened { get; private set; }

        public UpdatePanelView View { get; } = UpdatePanel.Compute(new UpdatePanelFacts
        {
            Installed = Installed,
            Mode = UpdateMode.Ask,
            Now = new DateTimeOffset(2026, 11, 6, 14, 32, 5, TimeSpan.Zero),
            LastCheck = new DateTimeOffset(2026, 11, 6, 14, 2, 0, TimeSpan.Zero),
            HighestSeen = Next,
            Manifest = Next,
            AnnouncesOnly = true,
        });

        public bool ClipBusy => false;

        public int BufferSeconds => 300;

        public void CheckNow()
        {
        }

        public void ConfirmUpdate(bool saveFirst)
        {
        }

        public void CancelUpdate()
        {
        }

        public void WhatsNew() => Opened++;

        public void SkipThisVersion()
        {
        }
    }

    // ---------------------------------------------------------------- every scene of the dump, with the features of each system

    [Theory]
    [InlineData("Capture")]
    [InlineData("Portal")]
    [InlineData("Command")]
    public void Every_scene_of_the_dump_draws_with_the_features_of_a_Linux_desktop(string modeName)
    {
        var mode = Enum.Parse<ShortcutMode>(modeName);
        var features = PlatformFeatures.Linux(() => mode, () => DesktopCommand);
        var scenes = new Dictionary<string, List<string>>();
        var texts = new RecordingText();
        uint* bits = null;
        try
        {
            SettingsDump.Render(1, (width, height, scale) =>
            {
                bits = (uint*)NativeMemory.AllocZeroed((nuint)(width * height), sizeof(uint));
                return new PixelCanvas(bits, width, height, scale, texts);
            }, (name, canvas) =>
            {
                Assert.True(texts.Drawn.Count > 0, name + ": no text drawn");
                scenes[name] = [.. texts.Drawn];
                texts.Drawn.Clear();
            }, features);
        }
        finally
        {
            NativeMemory.Free(bits);
        }

        Assert.True(scenes.Count >= 80, "the dump has its scenes: " + scenes.Count);

        // A desktop has no Windows word on any page of it, in any scene, whatever the page.
        foreach (var (name, drawn) in scenes)
        {
            Assert.DoesNotContain(drawn, text => text.Contains("Start with Windows", StringComparison.Ordinal) || text.Contains("Recycle", StringComparison.Ordinal) || text.Contains("Explorer", StringComparison.Ordinal) || text.Contains("Windows keeps", StringComparison.Ordinal));
            Assert.DoesNotContain("Notification position", drawn);
            Assert.DoesNotContain("Confirmation sound", drawn);
        }

        Assert.Contains("Start with the session", scenes["general@100"]);
        Assert.Contains("Trash", string.Join(' ', scenes["library-supprimer-clip@100"]));
        Assert.Contains("Both files go to the Trash.", string.Join(' ', scenes["library-supprimer-2-fichiers@100"]));

        var hotkeys = scenes["hotkeys@100"];
        switch (mode)
        {
            case ShortcutMode.Capture:
                Assert.Contains("Reset to defaults", hotkeys);
                Assert.DoesNotContain(SettingsScreen.DesktopKeysNote, string.Join(' ', hotkeys));
                break;
            case ShortcutMode.Portal:
                Assert.Contains(SettingsScreen.DesktopKeysNote, string.Join(' ', hotkeys));
                Assert.DoesNotContain("Reset to defaults", hotkeys);
                Assert.Contains("Not set", hotkeys);
                break;
            default:
                Assert.Contains(DesktopCommand, hotkeys);
                Assert.Contains(SettingsScreen.CommandIntro, string.Join(' ', hotkeys));
                Assert.DoesNotContain("Reset to defaults", hotkeys);
                break;
        }
    }

    [Fact]
    public void Every_scene_of_the_dump_with_the_Windows_features_keeps_the_Windows_words()
    {
        var scenes = new Dictionary<string, List<string>>();
        var texts = new RecordingText();
        uint* bits = null;
        try
        {
            SettingsDump.Render(1, (width, height, scale) =>
            {
                bits = (uint*)NativeMemory.AllocZeroed((nuint)(width * height), sizeof(uint));
                return new PixelCanvas(bits, width, height, scale, texts);
            }, (name, canvas) =>
            {
                scenes[name] = [.. texts.Drawn];
                texts.Drawn.Clear();
            }, Windows);
        }
        finally
        {
            NativeMemory.Free(bits);
        }

        Assert.Contains("Start with Windows", scenes["general@100"]);
        Assert.Contains("Notification position", scenes["general@100"]);
        Assert.Contains("Confirmation sound", scenes["general@100"]);
        Assert.Contains("Reset to defaults", scenes["hotkeys@100"]);
        Assert.Contains("Recycle Bin", string.Join(' ', scenes["library-supprimer-clip@100"]));
        Assert.Contains("Record", scenes["audio@100"]);
    }
}
