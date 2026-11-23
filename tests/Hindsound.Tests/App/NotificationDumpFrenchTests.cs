using Hindsound.App;
using Hindsound.Ui;

namespace Hindsound.Tests.App;

/// <summary>The samples of a refused save hotkey say what the app says, in the language of the dump.</summary>
public class NotificationDumpFrenchTests
{
    [Fact]
    public void The_refused_save_hotkey_samples_are_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);

        var samples = NotificationDump.Samples(NotificationPalette.Lime);

        var taken = Assert.Single(samples, s => s.Name == "on-taken").Content.Body;
        var notWorking = Assert.Single(samples, s => s.Name == "on-taken-long").Content.Body;
        Assert.EndsWith("Raccourci déjà pris", taken);
        Assert.EndsWith("Raccourci inactif", notWorking);
        Assert.DoesNotContain("hotkey", taken + notWorking);
    }
}
