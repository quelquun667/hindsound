using Hindsound.Input;
using Hindsound.Linux.Input;

namespace Hindsound.Linux.Tests.Input;

/// <summary>The words a Linux desktop is given for the shortcuts, in the language of the moment: pure logic, runs everywhere.</summary>
public sealed class FrenchShortcutTextsTests
{
    [Fact]
    public void The_portal_descriptions_follow_the_language()
    {
        Assert.Equal("Save the last minute", PortalBindings.DescriptionOf(HotkeyAction.Save1m));

        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Enregistrer un clip", PortalBindings.DescriptionOf(HotkeyAction.SaveDefault));
        Assert.Equal("Enregistrer la dernière minute", PortalBindings.DescriptionOf(HotkeyAction.Save1m));
        Assert.Equal("Pause ou reprise", PortalBindings.DescriptionOf(HotkeyAction.TogglePause));
        Assert.Equal("Enregistrer un clip et le modifier", PortalBindings.DescriptionOf(HotkeyAction.SaveAndEdit));
        Assert.All(Enum.GetValues<HotkeyAction>(), action => Assert.DoesNotContain("Save", PortalBindings.DescriptionOf(action)));
    }

    [Fact]
    public void The_notice_of_a_desktop_with_no_shortcut_follows_the_language()
    {
        Assert.StartsWith("No global shortcut on this desktop.", HotkeyChoice.NoticeTextFor("/opt/hindsound"));

        using var scope = Tr.Use(UiLanguage.French);
        Assert.EndsWith("associe cette commande à une touche\u00A0: " + HotkeyChoice.SaveCommandFor("/opt/hindsound"), HotkeyChoice.NoticeTextFor("/opt/hindsound"));
        Assert.Equal(HotkeyChoice.NoticeGenericText, HotkeyChoice.NoticeTextFor("relative/path"));
        Assert.StartsWith("Pas de raccourci global", HotkeyChoice.NoticeGenericText);
    }
}
