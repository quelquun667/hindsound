using Hindsound.Linux.Desktop;
using Hindsound.Ui;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>The two buttons of the desktop's own notification are words on the screen: they follow the language, and the keys of the actions do not.</summary>
public sealed class NotificationLabelsInFrenchTests
{
    [Fact]
    public void The_labels_of_the_actions_are_in_French_and_their_keys_stay_the_same()
    {
        using var scope = Tr.Use(UiLanguage.French);
        var content = new NotificationContent(NotificationKind.Silent, "Clip enregistré (0:30)", "Copié. Colle", ["Ctrl", "V"], 0, NotificationVisual.Bars, 0, []) { ClipPath = "/home/u/a.mp3" };

        var request = NotificationRequest.For(content, 0, markup: false);

        Assert.Equal(["default", "Ouvrir", "show-folder", "Afficher dans le dossier"], request.Actions);
        Assert.Equal("Copié. Colle Ctrl+V", NotificationRequest.BodyOf(content));
    }
}
