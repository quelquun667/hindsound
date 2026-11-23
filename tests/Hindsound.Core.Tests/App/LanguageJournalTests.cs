using System.Reflection;
using Hindsound.App;
using Hindsound.Export;
using Hindsound.Ui;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.App;

/// <summary>
/// The journal is read by people helping with a problem: it stays in English whatever the language of the interface. The app
/// runs whole on a fake shell, in French, and what it writes about a notification and about the tray is read back.
/// </summary>
public sealed class LanguageJournalTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static List<string> JournalOfAFrenchRun(out NotificationContent content)
    {
        using var scope = Tr.Use(UiLanguage.French);
        using var rig = new ShellRunRig();
        var shown = NotificationStates.ForClip(new ClipResult(["clip.mp3"], TimeSpan.FromSeconds(30), 720_000, false, [], false), TimeSpan.FromSeconds(30), false, false, 0xFF00FF00);
        rig.Run(r =>
        {
            Assert.True(Tr.Set(UiLanguage.French) || Tr.French);
            var app = r.App!;
            typeof(HindsoundApp).GetField("_engineFailed", Hidden)!.SetValue(app, true);
            typeof(HindsoundApp).GetMethod("RefreshTray", Hidden)!.Invoke(app, []);
            typeof(HindsoundApp).GetMethod("PresentNow", Hidden)!.Invoke(app, [shown, false, null]);
        });
        content = shown;
        return rig.Log();
    }

    [Fact]
    public void The_line_of_the_tray_names_its_tip_in_English_while_the_interface_is_in_French()
    {
        var log = JournalOfAFrenchRun(out _);

        Assert.Contains(log, line => line.EndsWith("tray: Error, Hindsound - No audio device", StringComparison.Ordinal));
        Assert.DoesNotContain(log, line => line.Contains("Aucun", StringComparison.Ordinal));
    }

    [Fact]
    public void The_line_of_a_notification_names_its_kind_and_none_of_its_words()
    {
        var log = JournalOfAFrenchRun(out var content);

        Assert.Contains(log, line => line.EndsWith($"notification {content.Kind}", StringComparison.Ordinal));
        Assert.DoesNotContain(log, line => line.Contains(content.Title, StringComparison.Ordinal) || line.Contains(content.Body, StringComparison.Ordinal));
    }

    [Fact]
    public void The_notice_of_the_last_clip_is_made_when_the_first_launch_card_asks_for_it_in_the_language_of_that_moment()
    {
        using var scope = Tr.Use(UiLanguage.English);
        var clip = new LastClipInfo(TimeSpan.FromSeconds(30), DateTime.Now, 720_000)
        {
            NoticeMaker = () => NotificationStates.ForClip(new ClipResult(["clip.mp3"], TimeSpan.FromSeconds(30), 720_000, false, [], false), TimeSpan.FromSeconds(30), false, false, 0xFF00FF00) with
            {
                Body = Tr.Of("In the folder", "Dans le dossier"),
            },
        };

        Assert.Equal("In the folder", clip.Notice!.Body);
        Tr.Set(UiLanguage.French);
        Assert.Equal("Dans le dossier", clip.Notice!.Body);
    }
}
