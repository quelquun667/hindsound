using Hindsound.Input;
using Hindsound.Settings;

namespace Hindsound.Ui.Window;

/// <summary><c>--dump-settings</c>: each page and the first-launch card as a PNG file, with no window at all.</summary>
internal static unsafe class SettingsDump
{
    private sealed class QuietHost : IWindowHost
    {
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
    }

    private sealed class SampleBackend : ISettingsBackend
    {
        public HotkeyStatus NextStatus { get; set; } = HotkeyStatus.Registered;

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

        public IReadOnlyList<HotkeyResult> ApplyHotkeys(HotkeySettings hotkeys) => Results();

        public IReadOnlyList<HotkeyResult> ResumeHotkeys() => Results();

        private List<HotkeyResult> Results() =>
            [.. Enum.GetValues<HotkeyAction>().Select(a => new HotkeyResult(a, null, NextStatus, NextStatus == HotkeyStatus.InUse ? HotkeyResult.InUseMessage : null))];
    }

    /// <summary>An updater that does nothing: it only gives the pictures a section, a card and the first-launch line.</summary>
    private sealed class SamplePanel(Updates.UpdatePanelView view, bool clipBusy = false) : Updates.IUpdatesPanel
    {
        private static readonly Updates.UpdateVersion Installed = new(0, 1, 0), Next = new(0, 2, 0);

        public Updates.UpdatePanelView View => view;

        public bool ClipBusy => clipBusy;

        public int BufferSeconds => 300;

        public static SamplePanel Ready(bool clipBusy = false) => new(Updates.UpdatePanel.Compute(Facts(Updates.UpdateMode.Auto) with
        {
            LastCheck = new DateTimeOffset(2026, 11, 6, 14, 2, 0, TimeSpan.Zero),
            HighestSeen = Next,
            Manifest = Next,
            Downloaded = Next,
        }), clipBusy);

        public static SamplePanel Downloading() => new(Updates.UpdatePanel.Compute(Facts(Updates.UpdateMode.Ask) with
        {
            LastCheck = new DateTimeOffset(2026, 11, 6, 14, 2, 0, TimeSpan.Zero),
            HighestSeen = Next,
            Manifest = Next,
            Pass = Updates.UpdatePassKind.Download,
            PassVersion = Next,
            ClickStands = true,
        }));

        private static Updates.UpdatePanelFacts Facts(Updates.UpdateMode mode) =>
            new() { Installed = Installed, Mode = mode, Now = new DateTimeOffset(2026, 11, 6, 14, 32, 5, TimeSpan.Zero) };

        public void CheckNow()
        {
        }

        public void ConfirmUpdate(bool saveFirst)
        {
        }

        public void CancelUpdate()
        {
        }

        public void WhatsNew()
        {
        }

        public void SkipThisVersion()
        {
        }
    }

    public static int Write(string folder, double scale)
    {
        Directory.CreateDirectory(folder);
        var host = new QuietHost();
        var suffix = "@" + (int)Math.Round(scale * 100);
        var width = (int)Math.Round(SettingsScreen.WindowWidth * scale);
        var height = (int)Math.Round(SettingsScreen.WindowHeight * scale);
        using var canvas = new GdiCanvas(width, height, scale);
        var sampleStatus = new Audio.AudioEngineStatus(Audio.CaptureActivity.Capturing, true, true, true, "Speakers", "Headset", true, TimeSpan.FromMinutes(5), 86_688_000);

        void Shot(string name, Action<SettingsScreen, SampleBackend>? prepare = null, bool firstRun = false, bool focus = false)
        {
            var backend = new SampleBackend();
            var settings = new AppSettings().Normalize();
            var screen = new SettingsScreen(settings, backend, host, canvas, firstRun)
            {
                ShowFocus = focus,
                NoNativeEdit = true,
                Clock = () => new DateTime(2026, 11, 6, 14, 32, 5),
            };
            screen.UpdateStatus(sampleStatus);
            screen.SetLastClip(new LastClipInfo(TimeSpan.FromSeconds(30), new DateTime(2026, 11, 6, 14, 32, 0), 720_000));
            screen.SetChoices(
                [new AudioDeviceChoice("out-1", "Speakers (Realtek High Definition Audio)"), new AudioDeviceChoice("out-2", "Headset Earphone")],
                [new AudioDeviceChoice("mic-1", "Headset Microphone")],
                [new AudioAppChoice("Discord.exe", "Discord"), new AudioAppChoice("chrome.exe", "Google Chrome")]);
            prepare?.Invoke(screen, backend);
            screen.Paint(canvas);
            PngFile.Write(Path.Combine(folder, name + suffix + ".png"), canvas.Bits, canvas.Width, canvas.Height);
        }

        Shot("hotkeys");
        Shot("band-filling", (s, _) => s.UpdateStatus(sampleStatus with { Buffered = TimeSpan.FromSeconds(12) }));
        Shot("band-empty", (s, _) => s.UpdateStatus(sampleStatus with { Buffered = TimeSpan.Zero }));
        Shot("band-longest", (s, _) =>
        {
            s.Editor.SetBufferSeconds(AppSettings.MaxBufferSeconds);
            s.UpdateStatus(sampleStatus with { Buffered = TimeSpan.FromSeconds(1790), MicCapturing = false, MemoryBytes = 522_000_000 });
        });
        Shot("hotkeys-listening", (s, _) =>
        {
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Enter, 0);
        }, focus: true);
        Shot("hotkeys-listening-dimred", (s, _) =>
        {
            s.Editor.SetAccentHex("#BF2828");
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Enter, 0);
        }, focus: true);
        Shot("durations-grey", (s, _) =>
        {
            s.Editor.SetAccentHex("#646464");
            s.ShowSection(Section.Durations);
        });
        Shot("hotkeys-errors", (s, b) =>
        {
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Enter, 0);
            b.NextStatus = HotkeyStatus.InUse;
            s.Key('H', HotkeyGesture.ModControl | HotkeyGesture.ModAlt);
            b.NextStatus = HotkeyStatus.Registered;
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Enter, 0);
            s.Key(0x79, HotkeyGesture.ModControl | HotkeyGesture.ModShift);
        }, focus: true);
        Shot("durations", (s, _) => s.ShowSection(Section.Durations));
        Shot("durations-custom", (s, _) =>
        {
            s.ShowSection(Section.Durations);
            s.Key(Vk.Tab, 0);
            s.Key(Vk.End, 0);
            s.Key(Vk.Tab, 0);
            s.EditChanged("nine");
        }, focus: true);
        Shot("audio", (s, _) => s.ShowSection(Section.Audio));
        Shot("audio-app", (s, _) =>
        {
            s.Editor.SetSourceApp("Discord.exe");
            s.UpdateStatus(sampleStatus with { SystemDeviceName = "Discord.exe", OwnSoundsExcluded = false, SourceApp = "Discord.exe" });
            s.ShowSection(Section.Audio);
        });
        Shot("audio-app-missing", (s, _) =>
        {
            s.Editor.SetSourceApp("RobloxPlayerBeta.exe");
            s.UpdateStatus(sampleStatus with { SystemCapturing = false, SystemDeviceName = null, OwnSoundsExcluded = false, SourceApp = "RobloxPlayerBeta.exe", SourceAppMissing = true });
            s.ShowSection(Section.Audio);
        });
        Shot("audio-list", (s, _) =>
        {
            s.ShowSection(Section.Audio);
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Space, 0);
        }, focus: true);
        Shot("export", (s, _) => s.ShowSection(Section.Export));
        Shot("general", (s, _) => s.ShowSection(Section.General));
        Shot("general-bottom", (s, _) =>
        {
            s.ShowSection(Section.General);
            s.ScrollTo(float.MaxValue);
        });
        Shot("general-toodark", (s, _) =>
        {
            s.ShowSection(Section.General);
            s.Key(Vk.Tab, 0);
            s.Key(Vk.End, 0);
            s.Key(Vk.Tab, 0);
            s.EditChanged("#202020");
        }, focus: true);
        Shot("general-cyan", (s, _) =>
        {
            s.ShowSection(Section.General);
            s.Editor.SetAccentPreset(Settings.AccentPreset.Cyan);
        });
        var ready = SamplePanel.Ready();
        Shot("general-updates", (s, _) =>
        {
            s.SetUpdates(ready);
            s.ShowUpdates();
        });
        Shot("general-updates-copy", (s, _) =>
        {
            s.SetUpdates(new SamplePanel(Updates.UpdatePanel.Inactive(new Updates.UpdateVersion(0, 1, 0))));
            s.ShowUpdates();
        });
        Shot("update-card", (s, _) =>
        {
            s.SetUpdates(ready);
            s.ShowUpdateCard();
        }, focus: true);
        Shot("update-card-clip", (s, _) =>
        {
            s.SetUpdates(SamplePanel.Ready(clipBusy: true));
            s.ShowUpdateCard();
        }, focus: true);
        Shot("update-card-downloading", (s, _) =>
        {
            s.SetUpdates(SamplePanel.Downloading());
            s.ShowUpdateCard();
        }, focus: true);
        Shot("firstrun-1-updates", (s, _) => s.SetUpdates(ready), firstRun: true);
        Shot("firstrun-3-updates", (s, _) =>
        {
            s.SetUpdates(ready);
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Enter, 0);
            s.Key(Vk.Enter, 0);
        }, firstRun: true);
        Shot("firstrun-1", firstRun: true);
        Shot("firstrun-2", (s, _) =>
        {
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Enter, 0);
        }, firstRun: true);
        Shot("firstrun-3", (s, _) =>
        {
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Enter, 0);
            s.Key(Vk.Enter, 0);
        }, firstRun: true);
        Shot("firstrun-3-done", (s, _) =>
        {
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Enter, 0);
            s.Key(Vk.Enter, 0);
            s.SetLastClip(new LastClipInfo(TimeSpan.FromSeconds(30), DateTime.Now, 720_000));
        }, firstRun: true);
        Shot("firstrun-3-not-copied", (s, _) =>
        {
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Enter, 0);
            s.Key(Vk.Enter, 0);
            var clip = new Hindsound.Export.ClipResult(["clip.mp3"], TimeSpan.FromSeconds(30), 720_000, false, [], false);
            s.SetLastClip(new LastClipInfo(clip.Duration, DateTime.Now, clip.SizeBytes) { Notice = NotificationStates.ForClip(clip, clip.Duration, true, false, Theme.Amber) });
        }, firstRun: true);

        return FolderPicker.SelfCheck() ? 0 : 3;
    }
}
