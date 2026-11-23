using System.Reflection;
using Hindsound.App;
using Hindsound.Audio;
using Hindsound.Export;
using Hindsound.Ui;
using Hindsound.Updates;

namespace Hindsound.Tests.App;

/// <summary>
/// The notifications, the menu of the icon, the lines of the updates and the messages of the app in French: each family is
/// built in French, and a few French words are there while the English ones are not.
/// </summary>
public sealed class LanguageNoticesTests
{
    private const string Nbsp = "\u00A0";
    private static readonly TimeSpan Thirty = TimeSpan.FromSeconds(30);

    private static NotificationContent Clip(double seconds = 30, bool silent = false, bool rescued = false, bool wav = false, bool copyWanted = true, bool copied = true) =>
        NotificationStates.ForClip(new ClipResult(["clip.mp3"], TimeSpan.FromSeconds(seconds), 1, silent, [], rescued, wav), Thirty, copyWanted, copied, 0xFF00FF00);

    private static void Says(NotificationContent content, string title, string body)
    {
        Assert.Equal(title, content.Title);
        Assert.Equal(body, content.Body);
    }

    [Fact]
    public void A_clip_saved_says_so_in_French_with_the_paste_keys_unchanged()
    {
        using var scope = Tr.Use(UiLanguage.French);

        var saved = Clip();
        Says(saved, "Clip enregistré (0:30)", "Copié. Colle");
        Assert.Equal(["Ctrl", "V"], saved.Keys);
        Says(Clip(12), "Clip enregistré (0:12 sur 0:30)", "Copié. Colle");
        Says(Clip(copyWanted: false, copied: false), "Clip enregistré (0:30)", "Dans ton dossier de clips.");
        Says(Clip(copied: false), "Clip enregistré (0:30)", "Non copié. Icône > Copier le dernier clip.");
        Says(Clip(wav: true), "Clip enregistré (0:30)", "Enregistré en WAV. Colle");
        Says(Clip(wav: true, copyWanted: false, copied: false), "Clip enregistré (0:30)", "Enregistré en WAV. Échec du MP3.");
        Says(Clip(silent: true), "Clip enregistré (0:30)", "Aucun son. Vérifie Réglages > Audio.");
        Says(Clip(silent: true, copied: false), "Clip enregistré (0:30)", "Aucun son, et non copié");
    }

    [Fact]
    public void A_clip_kept_in_the_rescue_folder_says_so_in_French_and_names_the_folder_as_the_system_does()
    {
        using var scope = Tr.Use(UiLanguage.French);
        var folder = Path.Combine("Hindsound", "Rescued");

        Says(Clip(rescued: true), "Dossier de sortie inaccessible", "Clip gardé dans " + folder);
        Says(Clip(rescued: true, copied: false), "Dossier de sortie inaccessible", "Gardé dans " + folder + ". Non copié");
        Assert.Equal("Clip gardé dans " + folder, NotificationStates.RescuedBody);
    }

    [Fact]
    public void The_statuses_of_the_capture_are_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);

        Says(NotificationStates.NoDevice(), "Aucun périphérique audio", "Rien n'est capturé");
        Says(NotificationStates.Paused(), "Capture en pause", "Plus rien n'est capturé");
        Says(NotificationStates.Paused(75), "Capture en pause", "1:15 en mémoire. Plus rien n'est capturé");
        Says(NotificationStates.DeviceChanged("Casque"), "Périphérique audio changé", "Capture sur Casque");
        Says(NotificationStates.Saving(Thirty), "Enregistrement du clip (0:30)", "Un instant");
        Says(NotificationStates.ClosingAfterClip(), "Enregistrement de ton clip", "Hindsound se ferme quand c'est fini");
        Says(NotificationStates.NothingToSave(), "Rien à enregistrer pour l'instant", "Aucun son n'a encore été capturé");
        Says(NotificationStates.SaveFailed(), "Clip non enregistré", "La raison est dans hindsound.log");
        Says(NotificationStates.StillSaving(), "Ton dernier clip s'enregistre encore", "Réessaie dans un instant");
        Says(NotificationStates.LastClipGone(), "Dernier clip introuvable", "Il a été déplacé ou supprimé");
        Says(NotificationStates.MicLost(), "Micro perdu", "Les clips sont sans voix jusqu'à son retour");
        Says(NotificationStates.MicBack(), "Micro de retour", "Les clips ont de nouveau ta voix");
    }

    [Fact]
    public void The_reminder_of_the_capture_is_in_French_with_its_keys_and_its_memory()
    {
        using var scope = Tr.Use(UiLanguage.French);

        var full = NotificationStates.CaptureOn(300, "Ctrl+Shift+F10", keptSeconds: 300);
        Says(full, "Capture active", "5:00 en mémoire. Enregistre" + Nbsp + ":");
        Assert.Equal(["Ctrl", "Shift", "F10"], full.Keys);
        Assert.Equal("Jusqu'à 5:00 en mémoire", NotificationStates.CaptureOn(300, null).Body);
        Assert.Equal("Remplissage, jusqu'à 5:00", NotificationStates.CaptureOn(300, null, keptSeconds: 0).Body);
        Assert.Equal("2:00 sur 5:00 en mémoire", NotificationStates.CaptureOn(300, null, keptSeconds: 120).Body);
        Assert.Equal("Mis à jour vers 0.2.0", NotificationStates.CaptureOn(300, null, updatedTo: "0.2.0").Title);
    }

    [Fact]
    public void The_save_hotkey_problem_and_the_tray_tips_are_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);

        Assert.Equal("Hindsound - Aucun périphérique audio", StatusTransitions.TrayOf(null, [], false, engineFailed: true).Tip);
        Assert.Equal("Hindsound - Un clip a un problème", StatusTransitions.TrayOf(null, [], true).Tip);
        Assert.Equal("Hindsound - Fichier de réglages non lu" + Nbsp + ": valeurs par défaut, micro coupé", StatusTransitions.TrayOf(null, [], false, onDefaults: true).Tip);
        Assert.Equal("Hindsound - enregistrement du clip, puis fermeture", StatusTransitions.TrayOf(null, [], false, closingAfterClip: true).Tip);
        var refused = new[] { new Hindsound.Input.HotkeyResult(Hindsound.Input.HotkeyAction.SaveDefault, "Ctrl+Shift+F10", Hindsound.Input.HotkeyStatus.InUse, "taken") };
        Assert.Equal("Raccourci déjà pris", StatusTransitions.SaveHotkeyProblem(refused));
        Assert.Equal("Jusqu'à 0:30 en mémoire. Raccourci déjà pris", StatusTransitions.CaptureOnNow(new Hindsound.Settings.AppSettings { BufferSeconds = 30 }, refused).Body);
    }

    [Fact]
    public void The_notices_about_the_settings_and_the_updates_are_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);

        Says(NotificationStates.SettingsDamaged(true), "Fichier de réglages endommagé", "Valeurs par défaut, micro coupé. Copie en .bak");
        Says(NotificationStates.SettingsDamaged(false), "Fichier de réglages endommagé", "Valeurs par défaut, micro coupé");
        Says(NotificationStates.SettingsUnreadable(), "Fichier de réglages illisible", "Valeurs par défaut, micro coupé, en attendant");
        Says(NotificationStates.SettingsHasError(), "Erreur dans le fichier de réglages", "Les réglages actuels sont gardés");
        Says(NotificationStates.SettingsNotSaved(), "Réglages non enregistrés", "Changement perdu au prochain démarrage");
        Says(NotificationStates.StartupNotChanged(), "Changement impossible", "«" + Nbsp + "Lancer avec Windows" + Nbsp + "» reste comme avant");
        Says(NotificationStates.UpdateReady("0.2.0", 0), "Mise à jour prête (0.2.0)", "S'installe à la fermeture. Clique pour voir.");
        Says(NotificationStates.UpdateAvailable("0.2.0", 0), "Mise à jour disponible (0.2.0)", "Clique pour voir les nouveautés.");
        Says(NotificationStates.UpdateFailed("0.1.0", true), "Échec de la mise à jour", "Toujours en 0.1.0. Nouvel essai plus tard.");
        Says(NotificationStates.UpdateFailed("0.1.0", false), "Échec de la mise à jour", "Toujours en 0.1.0. Va dans Réglages > Général.");
        Says(ReleaseNotice.Content(new UpdateVersion(0, 2, 0), 0), "Hindsound 0.2.0 est disponible", "Clique pour la télécharger.");
    }

    [Fact]
    public void The_menu_of_the_icon_is_in_French_whatever_the_state()
    {
        using var scope = Tr.Use(UiLanguage.French);

        var menu = TrayMenu.Items(paused: false, hasLastClip: true, closingAfterClip: false, updateText: "Mettre à jour vers 0.2.0...", buffered: TimeSpan.FromSeconds(10));
        Assert.Equal(
            ["Ouvrir Hindsound", "Bibliothèque", "Enregistrer 30" + Nbsp + "s (seulement 0:10)", "Enregistrer 15" + Nbsp + "s (seulement 0:10)", "Enregistrer 1" + Nbsp + "min (seulement 0:10)",
                "Enregistrer 5" + Nbsp + "min (seulement 0:10)", string.Empty, "Copier le dernier clip", "Mettre la capture en pause", "Ouvrir le dossier des clips", "Réglages", string.Empty,
                "Mettre à jour vers 0.2.0...", "Quitter"],
            menu.Select(item => item.Text));
        Assert.Equal("Reprendre la capture", TrayMenu.Items(paused: true, hasLastClip: false, closingAfterClip: false).Single(item => item.Command == TrayCommand.TogglePause).Text);
        Assert.Equal(["Quitter"], TrayMenu.Items(paused: false, hasLastClip: false, closingAfterClip: true).Select(item => item.Text));
        Assert.Equal(
            ["Enregistrer un clip", string.Empty, "Mettre la capture en pause", "Ouvrir le dossier des clips", "Lancer avec la session", string.Empty, "Quitter"],
            TrayMenu.WindowlessItems(paused: false, startsWithSession: true, clipSeconds: 30, closingAfterClip: false).Select(item => item.Text));
    }

    [Fact]
    public void The_lines_of_the_updates_section_are_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);
        var installed = new UpdateVersion(0, 1, 0);
        var next = new UpdateVersion(0, 2, 0);
        var now = new DateTimeOffset(2026, 11, 7, 18, 30, 0, TimeSpan.FromHours(1));
        var facts = new UpdatePanelFacts { Installed = installed, Mode = UpdateMode.Auto, Now = now, LastCheck = new DateTimeOffset(2026, 11, 7, 14, 2, 0, TimeSpan.FromHours(1)) };

        Assert.Equal("Cette copie n'a pas été installée" + Nbsp + ": elle ne se met pas à jour toute seule.", UpdatePanel.Inactive(installed).StatusLine);
        Assert.Equal("À jour. Vérifié aujourd'hui 14:02", UpdatePanel.Compute(facts).StatusLine);
        Assert.Equal("À jour. Vérifié hier 14:02", UpdatePanel.Compute(facts with { Now = now.AddDays(1) }).StatusLine);
        Assert.Equal("À jour. Vérifié le 2026-11-07", UpdatePanel.Compute(facts with { Now = now.AddDays(5) }).StatusLine);
        Assert.Equal("Mises à jour désactivées.", UpdatePanel.Compute(facts with { Mode = UpdateMode.Off, LastCheck = null }).StatusLine);
        Assert.Equal("Pas encore vérifié.", UpdatePanel.Compute(facts with { LastCheck = null }).StatusLine);
        Assert.Equal("Vérification...", UpdatePanel.Compute(facts with { Pass = UpdatePassKind.Check }).StatusLine);
        Assert.Equal("Téléchargement de 0.2.0...", UpdatePanel.Compute(facts with { Pass = UpdatePassKind.Download, PassVersion = next }).StatusLine);
        Assert.Equal("La version 0.2.0 est disponible.", UpdatePanel.Compute(facts with { Manifest = next, Mode = UpdateMode.Ask }).StatusLine);
        Assert.Equal("La version 0.2.0 est disponible. Choisis Me demander pour la télécharger.", UpdatePanel.Compute(facts with { Manifest = next, Mode = UpdateMode.Off }).StatusLine);
        Assert.Equal("La version 0.2.0 est prête. Elle s'installe à la fermeture de Hindsound.", UpdatePanel.Compute(facts with { Manifest = next, Downloaded = next }).StatusLine);
        Assert.Equal("La version 0.2.0 est ignorée.", UpdatePanel.Compute(facts with { Manifest = next, Skipped = next }).StatusLine);
        Assert.Equal("Vérification impossible. Nouvel essai demain.", UpdatePanel.Compute(facts with { Problem = UpdateProblem.CheckFailed }).StatusLine);
        Assert.Equal("Échec du téléchargement. Nouvel essai demain.", UpdatePanel.Compute(facts with { Problem = UpdateProblem.DownloadFailed }).StatusLine);
        Assert.Equal("Mise en pause par un jeu ou un clip. Nouvel essai plus tard.", UpdatePanel.Compute(facts with { Problem = UpdateProblem.Paused }).StatusLine);
        Assert.Equal("Pas maintenant" + Nbsp + ": un jeu ou un clip est en cours.", UpdatePanel.Compute(facts with { Manifest = next, Card = UpdateCardNote.NotNow }).CardLine);
        Assert.Equal("Échec du téléchargement. Rien n'a changé.", UpdatePanel.Compute(facts with { Manifest = next, Card = UpdateCardNote.DownloadFailed }).CardLine);
        Assert.Equal("Lancement de la mise à jour...", UpdatePanel.RunMessage(RunStatus.Started));
        Assert.Equal("Le fichier de mise à jour a disparu. Utilise Vérifier maintenant.", UpdatePanel.RunMessage(RunStatus.SetupMissing));
    }

    [Fact]
    public void The_refusal_to_start_an_update_names_the_system_that_refused_in_every_language()
    {
        var linux = PlatformFeatures.Linux(() => ShortcutMode.Command);
        var mac = PlatformFeatures.Mac(() => null);

        Assert.Equal("Windows refused to start the update.", UpdatePanel.RunMessage(RunStatus.StartFailed, PlatformFeatures.Windows));
        Assert.Equal("The system refused to start the update.", UpdatePanel.RunMessage(RunStatus.StartFailed, linux));
        Assert.Equal("The system refused to start the update.", UpdatePanel.RunMessage(RunStatus.StartFailed, mac));

        using var scope = Tr.Use(UiLanguage.French);
        Assert.Equal("Windows a refusé de lancer la mise à jour.", UpdatePanel.RunMessage(RunStatus.StartFailed, PlatformFeatures.Windows));
        Assert.Equal("Le système a refusé de lancer la mise à jour.", UpdatePanel.RunMessage(RunStatus.StartFailed, linux));
    }

    [Fact]
    public void The_reason_given_to_Windows_at_the_end_of_the_session_is_in_French()
    {
        using var scope = Tr.Use(UiLanguage.French);

        Assert.Equal("Enregistrement de ton clip", SessionEndBlock.Reason);
    }

    [Fact]
    public void The_preview_of_the_notification_and_the_notice_of_the_last_clip_follow_the_language_when_they_are_made()
    {
        using var scope = Tr.Use(UiLanguage.English);
        Assert.Equal("Clip saved (0:30)", Clip().Title);

        Tr.Set(UiLanguage.French);
        Assert.Equal("Clip enregistré (0:30)", Clip().Title);
    }

    [Fact]
    public void The_journal_line_of_a_notice_that_was_replaced_while_waiting_names_its_kind_and_none_of_its_words()
    {
        using var scope = Tr.Use(UiLanguage.French);
        var lines = new List<string>();
        var pending = new PendingNotices(lines.Add);
        var first = NotificationStates.SaveFailed();

        pending.AddClip(first, important: true, 0);
        pending.AddClip(NotificationStates.LastClipGone(), important: true, 1);

        Assert.Equal(["notification SaveFailed replaced while waiting for the desktop"], lines);
        Assert.DoesNotContain(lines, line => line.Contains(first.Title, StringComparison.Ordinal) || line.Contains(first.Body, StringComparison.Ordinal));
    }

    [Fact]
    public void The_journal_line_of_a_notification_that_waits_for_the_desktop_names_its_kind_and_none_of_its_words()
    {
        using var scope = Tr.Use(UiLanguage.French);
        using var rig = new ShellRunRig { PretendGameSeconds = 60 };
        var waiting = NotificationStates.NoDevice();
        rig.Run(r =>
        {
            Assert.True(Tr.Set(UiLanguage.French) || Tr.French);
            typeof(HindsoundApp).GetMethod("ShowStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(r.App!, [waiting, null]);
        });

        var log = rig.Log();
        Assert.Contains(log, line => line.EndsWith("notification NoDevice waits for the desktop", StringComparison.Ordinal));
        Assert.DoesNotContain(log, line => line.Contains(waiting.Title, StringComparison.Ordinal) || line.Contains(waiting.Body, StringComparison.Ordinal));
    }
}
