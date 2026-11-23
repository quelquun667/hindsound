using Hindsound.App;
using Hindsound.Input;
using Hindsound.Library;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Ui.Window;

/// <summary>
/// French texts made in one place and shown in another: the refusal of a name with a bad character, the tray tip of a refused
/// hotkey, the name of the computer's sound, and the key of the clip menu.
/// </summary>
public sealed class FrenchBetweenAreasTests
{
    [Fact]
    public void A_clip_name_with_a_bad_character_is_refused_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);

        var text = NameBoxState.ClipRefusalText(ClipRenameOutcome.BadCharacters);

        Assert.StartsWith("Un nom de fichier ne peut pas contenir", text);
    }

    [Fact]
    public void The_tray_tip_of_a_refused_hotkey_speaks_the_language_of_now_not_the_one_of_its_registration()
    {
        var taken = new HotkeyResult(HotkeyAction.CopyLast, "Ctrl+F9", HotkeyStatus.InUse, HotkeyResult.InUseMessage);
        using var scope = Tr.Use(UiLanguage.French);

        var tip = StatusTransitions.TrayOf(null, [taken], false).Tip;

        Assert.Equal("Hindsound - Ctrl+F9 : Raccourci déjà pris par une autre application", tip);
    }

    [Fact]
    public void The_capture_choice_says_the_whole_computer_and_the_editor_lane_says_its_sound()
    {
        using var scope = Tr.Use(UiLanguage.French);

        Assert.Equal("Tout le Mac", PlatformFeatures.WholeMachineOf("Mac"));
        Assert.Equal("Tout le PC", PlatformFeatures.Windows.WholeMachine);
        Assert.Equal("Tout le PC", SettingsEditor.RecordChoices([], null)[0].Label);
        Assert.Equal("Tout le PC à partir d'ici", WaveView.MarkerText(null));
        Assert.Equal("Son du Mac", EditorWave.LaneName(0, "Mac"));
        Assert.Equal("Son du PC", PlatformFeatures.MachineTrackOf("PC"));
    }

    [Fact]
    public void The_clip_menu_names_the_delete_key_as_a_French_keyboard_prints_it()
    {
        using (Tr.Use(UiLanguage.English))
        {
            Assert.Equal("Del", ClipMenu.KeyOf(ClipMenuItem.Delete));
        }

        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Suppr", ClipMenu.KeyOf(ClipMenuItem.Delete));
        Assert.Equal("F2", ClipMenu.KeyOf(ClipMenuItem.Rename));
    }
}
