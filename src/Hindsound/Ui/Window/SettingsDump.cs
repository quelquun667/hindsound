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

    /// <summary>A memory that holds what the picture needs and never changes, with no engine and no capture behind it.</summary>
    private sealed class SampleNow(Audio.WaveFacts facts) : INowPanel
    {
        public Audio.WaveFacts WaveFacts => facts;
    }

    /// <summary>A list of made-up clips with no folder behind it: nothing here reads the disk, and no real name reaches a picture.</summary>
    private sealed class SampleLibrary(Library.ClipLines? lines, IReadOnlyDictionary<string, int> seconds) : ILibraryPanel
    {
        private const string Folder = @"C:\Clips\", RescueFolder = @"C:\Clips\Rescued\";
        private const long BytesPerSecond = 24_000;

        public static readonly SampleLibrary NotRead = new(null, new Dictionary<string, int>());

        public Library.ClipLines? Lines => lines;

        /// <summary>Made-up collections, for the pictures of the menu and the stars. Null leaves the list as it was before them.</summary>
        public Library.LibraryData? Index { get; init; }

        /// <summary>What the made-up index reads as: a file in trouble shows its sentence in place of the line.</summary>
        public Library.LibraryStatus IndexStatus { get; set; } = Library.LibraryStatus.Loaded;

        /// <summary>The labels of a clip, by the path of its main file.</summary>
        public Dictionary<string, Library.ClipLabels> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Library.ClipLabels LabelsOf(Library.ClipGroup line) => Labels.TryGetValue(line.Main.Path, out var labels) ? labels : new(false, []);

        /// <summary>The library of the pictures: three collections, two stars, and three clips in the first collection.</summary>
        public static SampleLibrary Labeled(params (string Name, int Seconds, bool TwoFiles, bool Rescued)[] clips) => Labeled(0, clips);

        /// <param name="leftOut">How many clips the cap left out of the list.</param>
        /// <param name="clips">As for <see cref="Of"/>.</param>
        public static SampleLibrary Labeled(int leftOut, (string Name, int Seconds, bool TwoFiles, bool Rescued)[] clips)
        {
            var sample = Of(leftOut, clips);
            var labeled = new SampleLibrary(sample.Lines, sample.Seconds)
            {
                Index = new Library.LibraryData
                {
                    Collections = [new() { Id = "c1", Name = "pets" }, new() { Id = "c2", Name = "Sam" }, new() { Id = "c3", Name = "Alex" }],
                },
            };
            var rows = sample.Lines!.Lines;
            for (var i = 0; i < rows.Count; i++)
            {
                labeled.Labels[rows[i].Main.Path] = new Library.ClipLabels(i is 0 or 3, i is 1 or 3 or 5 ? ["c1"] : []);
            }

            return labeled;
        }

        private IReadOnlyDictionary<string, int> Seconds => seconds;

        /// <param name="leftOut">How many clips the cap left out of the list.</param>
        /// <param name="clips">A name without its extension, its seconds, and its two kinds of line.</param>
        public static SampleLibrary Of(int leftOut, params (string Name, int Seconds, bool TwoFiles, bool Rescued)[] clips)
        {
            var known = new Dictionary<string, int>();
            var rows = new List<Library.ClipGroup>(clips.Length);
            foreach (var (name, length, twoFiles, rescued) in clips)
            {
                var folder = rescued ? RescueFolder : Folder;
                var size = length * BytesPerSecond;
                var files = twoFiles
                    ? new[] { new Library.ClipFile($"{folder}{name}_system.mp3", size / 2, default), new Library.ClipFile($"{folder}{name}_mic.mp3", size / 2, default) }
                    : [new Library.ClipFile($"{folder}{name}.mp3", size, default)];
                known[files[0].Path] = length;
                rows.Add(new Library.ClipGroup(files));
            }

            return new SampleLibrary(new Library.ClipLines(rows, leftOut), known);
        }

        public TimeSpan? LengthOf(Library.ClipGroup line) => seconds.TryGetValue(line.Main.Path, out var length) ? TimeSpan.FromSeconds(length) : null;

        public bool IsRescued(Library.ClipGroup line) => line.Main.Path.StartsWith(RescueFolder, StringComparison.Ordinal);

        public void Copy(Library.ClipGroup line)
        {
        }

        public void ShowInFolder(Library.ClipGroup line)
        {
        }

        public void ReadAgain()
        {
        }
    }

    /// <summary>What the engine reports after a pause: it closes both captures, so the band says the microphone is not captured.</summary>
    internal static Audio.AudioEngineStatus PausedStatus(Audio.AudioEngineStatus open) =>
        open with { Activity = Audio.CaptureActivity.Paused, SystemCapturing = false, MicCapturing = false };

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

        // The four pictures of the README: the side bar they show is the one of the window, with Now and Library above the settings.
        static void WithNow(SettingsScreen s, Section page)
        {
            s.SetNow(new SampleNow(default));
            s.SetLibrary(SampleLibrary.NotRead);
            s.ShowSection(page);
        }

        Shot("hotkeys", (s, _) => WithNow(s, Section.Hotkeys));
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
        Shot("durations", (s, _) => WithNow(s, Section.Durations));
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
            WithNow(s, Section.Audio);
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
        Shot("general", (s, _) => WithNow(s, Section.General));
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

        // The Now page. The same names as its mock-ups, and made-up samples through real tracks and real lanes.
        const int rate = Audio.InternalFormat.SampleRate, memory = NowSamples.MemoryFrames, slicesPerSecond = rate / Audio.WaveLane.FramesPerSlice;
        var voice = NowSamples.Voice(NowSamples.Slices, 11);
        var weakMic = NowSamples.WeakMic(NowSamples.Slices, 23);
        var voiceTrack = NowSamples.Track(voice);
        var micTrack = NowSamples.Track(weakMic);
        var musicTrack = NowSamples.Track(NowSamples.LoudMusic(NowSamples.Slices, 37));
        var justStartedVoice = NowSamples.Track(voice.AsSpan(voice.Length - 12 * slicesPerSecond));
        var justStartedMic = NowSamples.Track(weakMic.AsSpan(weakMic.Length - 12 * slicesPerSecond));

        // An app that closed, a mic that was lost: the track stops, and the window goes on without it.
        var closedApp = NowSamples.Track(voice.AsSpan(0, voice.Length - 60 * slicesPerSecond));
        var lostMic = NowSamples.Track(weakMic.AsSpan(0, weakMic.Length - 40 * slicesPerSecond));

        static Audio.WaveFacts Memory(Audio.SourceTrack? system, Audio.SourceTrack? mic, int seconds = memory / rate, params Audio.SourceChange[] changes) =>
            new(system, mic, 0, (long)seconds * rate, memory, changes);

        static void Wave(SettingsScreen s, Audio.WaveFacts facts)
        {
            s.SetNow(new SampleNow(facts));
            s.SetLibrary(SampleLibrary.NotRead);
            for (var i = 0; i < 12; i++)
            {
                s.WaveTick();
            }
        }

        var starting = sampleStatus with { Buffered = TimeSpan.FromSeconds(12) };
        Shot("now-demarrage", (s, _) =>
        {
            s.UpdateStatus(starting);
            s.SetLastClip(null);
            Wave(s, Memory(justStartedVoice, justStartedMic, 12));
        });
        Shot("now-plein", (s, _) => Wave(s, Memory(voiceTrack, micTrack)));
        Shot("now-musique", (s, _) => Wave(s, Memory(musicTrack, micTrack)));
        Shot("now-app", (s, _) =>
        {
            s.Editor.SetSourceApp("Discord.exe");
            s.UpdateStatus(sampleStatus with { SystemDeviceName = "Discord.exe", OwnSoundsExcluded = false, SourceApp = "Discord.exe" });
            Wave(s, Memory(voiceTrack, micTrack, changes: new Audio.SourceChange(memory - 5L * rate, "Discord.exe")));
        });
        Shot("now-app-apres-app", (s, _) =>
        {
            s.Editor.SetSourceApp("Spotify.exe");
            s.UpdateStatus(sampleStatus with { SystemDeviceName = "Spotify.exe", OwnSoundsExcluded = false, SourceApp = "Spotify.exe" });
            Wave(s, Memory(voiceTrack, micTrack, changes: new Audio.SourceChange(memory - 60L * rate, "Spotify.exe", "Discord.exe")));
        });
        Shot("now-app-apres-app-long", (s, _) =>
        {
            s.SetChoices([new AudioDeviceChoice("out-1", "Speakers")], [new AudioDeviceChoice("mic-1", "Headset Microphone")], [new AudioAppChoice("SpaceTruckersOnline2.exe", "SpaceTruckersOnline2")]);
            s.Editor.SetSourceApp("SpaceTruckersOnline2.exe");
            s.UpdateStatus(sampleStatus with { OwnSoundsExcluded = false, SourceApp = "SpaceTruckersOnline2.exe" });
            Wave(s, Memory(voiceTrack, micTrack, changes: new Audio.SourceChange(memory - 60L * rate, "SpaceTruckersOnline2.exe", "MidnightRacersDeluxeEdition.exe")));
        });
        Shot("now-retour-pc", (s, _) => Wave(s, Memory(voiceTrack, micTrack, changes: new Audio.SourceChange(memory - 270L * rate, null))));
        Shot("now-micro-coupe", (s, _) =>
        {
            s.UpdateStatus(sampleStatus with { MicEnabled = false, MicCapturing = false });
            Wave(s, Memory(voiceTrack, null));
        });
        Shot("now-micro-perdu", (s, _) =>
        {
            s.UpdateStatus(sampleStatus with { MicCapturing = false });
            Wave(s, Memory(voiceTrack, lostMic));
        });
        Shot("now-pause", (s, _) =>
        {
            s.UpdateStatus(PausedStatus(sampleStatus));
            Wave(s, Memory(voiceTrack, micTrack));
        });
        Shot("now-app-absente", (s, _) =>
        {
            s.Editor.SetSourceApp("Discord.exe");
            s.UpdateStatus(sampleStatus with { SourceApp = "Discord.exe", SourceAppMissing = true, OwnSoundsExcluded = false });
            Wave(s, Memory(closedApp, micTrack));
        });
        Shot("now-sans-peripherique", (s, _) =>
        {
            s.UpdateStatus(sampleStatus with { Activity = Audio.CaptureActivity.NoDevice, SystemCapturing = false, MicCapturing = false, Buffered = TimeSpan.Zero });
            s.SetLastClip(null);
            Wave(s, Memory(null, null, 0));
        });
        Shot("now-nom-long", (s, _) =>
        {
            s.SetChoices([new AudioDeviceChoice("out-1", "Speakers")], [new AudioDeviceChoice("mic-1", "Headset Microphone")], [new AudioAppChoice("SpaceTruckersOnline2.exe", "SpaceTruckersOnline2")]);
            s.Editor.SetSourceApp("SpaceTruckersOnline2.exe");
            s.UpdateStatus(starting with { OwnSoundsExcluded = false, SourceApp = "SpaceTruckersOnline2.exe" });
            s.SetLastClip(null);
            Wave(s, Memory(justStartedVoice, justStartedMic, 12, new Audio.SourceChange(7L * rate, "SpaceTruckersOnline2.exe")));
        });
        Shot("now-list", (s, _) =>
        {
            Wave(s, Memory(voiceTrack, micTrack));
            s.Key(Vk.Tab, 0);
            s.Key(Vk.Space, 0);
            s.WaveTick();
        }, focus: true);

        // The Library page. The same names as its mock-ups, made-up clips and collections.
        (string, int, bool, bool)[] clips =
        [
            ("Hindsound_Discord_2026-11-08_21-14-03", 30, false, false),
            ("Hindsound_Discord_2026-11-08_20-47-51", 30, false, false),
            ("Hindsound_2026-11-08_19-02-16", 60, false, false),
            ("Hindsound_Discord_2026-11-07_23-31-40", 15, false, false),
            ("Hindsound_2026-11-07_22-10-08", 300, true, false),
            ("Hindsound_Discord_2026-11-06_18-55-27", 30, false, false),
            ("Hindsound_2026-11-05_21-40-12", 30, false, false),
        ];
        const string chosenClip = @"C:\Clips\Hindsound_Discord_2026-11-08_20-47-51.mp3";

        static void ShowClips(SettingsScreen s, SampleLibrary library)
        {
            s.SetNow(new SampleNow(default));
            s.SetLibrary(library);
            s.ShowSection(Section.Library);
        }

        Shot("library-liste", (s, _) =>
        {
            ShowClips(s, SampleLibrary.Labeled(clips));
            s.SelectClip(chosenClip);
        });

        // The menu open on the first collection, reached the way the keyboard reaches it. The clip is chosen once there: on the
        // way, Favorites does not hold it, and a view without the chosen clip lets it go.
        Shot("library-filtre", (s, _) =>
        {
            ShowClips(s, SampleLibrary.Labeled(clips));
            s.TabTo(SettingsScreen.ClipFilterId);
            s.Key(Vk.Down, 0);
            s.Key(Vk.Down, 0);
            s.SelectClip(chosenClip);
            s.Key(Vk.Enter, 0);
        });
        Shot("library-collection-vide", (s, _) =>
        {
            ShowClips(s, SampleLibrary.Labeled(clips));
            s.TabTo(SettingsScreen.ClipFilterId);
            s.Key(Vk.End, 0);
        });
        Shot("library-clip-absent", (s, _) =>
        {
            ShowClips(s, SampleLibrary.Labeled(clips));
            s.SelectClip(chosenClip);
            s.ClipGestureEnded(chosenClip, ShellOutcome.Gone);
        });
        Shot("library-vide", (s, _) => ShowClips(s, SampleLibrary.Of(0)));

        // A clip played, on a chosen output where the capture can hear it: Stop in place of Play, and the sentence beside the menu.
        Shot("library-ecoute", (s, _) =>
        {
            var library = SampleLibrary.Labeled(clips);
            ShowClips(s, library);
            s.UpdateStatus(sampleStatus with { OwnSoundsExcluded = false });
            s.SelectClip(chosenClip);
            s.PreviewChanged(new Library.PreviewChange(Library.PreviewState.Playing, library.Lines!.Lines.First(line => line.Main.Path == chosenClip)));
        });

        // The chosen clip could not be played: Play again, and the reason in place of its name.
        Shot("library-ecoute-erreur", (s, _) =>
        {
            var library = SampleLibrary.Labeled(clips);
            ShowClips(s, library);
            s.SelectClip(chosenClip);
            s.PreviewChanged(new Library.PreviewChange(Library.PreviewState.Error, library.Lines!.Lines.First(line => line.Main.Path == chosenClip)));
        });

        // The end of 2,000 lines, reached the way the user reaches it: the last row, then up to the one the picture shows chosen.
        Shot("library-plafond", (s, _) =>
        {
            var older = Enumerable.Range(0, Library.ClipScanner.MaxLines - 6).Select(i => ($"Hindsound_{i:0000}", 30, false, false));
            var last = clips.Take(6).Select((clip, i) => i == 3 ? clip with { Item4 = true } : clip);
            var library = SampleLibrary.Labeled(500, [.. older, .. last]);

            // The labels of the sample go to the first rows: the ones the picture shows are the last six.
            var rows = library.Lines!.Lines;
            library.Labels.Clear();
            for (var i = 0; i < 6; i++)
            {
                library.Labels[rows[rows.Count - 6 + i].Main.Path] = new Library.ClipLabels(i is 0 or 3, i is 1 or 3 or 5 ? ["c1"] : []);
            }

            ShowClips(s, library);
            s.TabTo(SettingsScreen.ClipListId);
            s.Key(Vk.End, 0);
            s.Key(Vk.Up, 0);
            s.Key(Vk.Up, 0);
        });
        Shot("library-nom-long", (s, _) =>
        {
            var name = "Hindsound_" + new string('W', 120);
            ShowClips(s, SampleLibrary.Of(0, [(name, 30, false, false), .. clips.Skip(1)]));
            s.SelectClip($@"C:\Clips\{name}.mp3");
        });

        // The line of collections, its +N, and the box of a collection in each of its states. Made-up names only.
        static SampleLibrary Eight((string, int, bool, bool)[] clips)
        {
            var library = SampleLibrary.Labeled(clips);
            foreach (var (id, name) in new[] { ("c4", "Best of the group chat"), ("c5", "trips"), ("c6", "games"), ("c7", "music"), ("c8", "cats") })
            {
                library.Index!.Collections.Add(new() { Id = id, Name = name });
            }

            library.Labels[chosenClip] = new Library.ClipLabels(false, ["c1", "c4", "c7"]);
            return library;
        }

        static void Chosen(SettingsScreen s, SampleLibrary library)
        {
            ShowClips(s, library);
            s.SelectClip(chosenClip);
        }

        static SampleLibrary InTrouble((string, int, bool, bool)[] clips, Library.LibraryStatus status)
        {
            var library = SampleLibrary.Labeled(clips);
            library.IndexStatus = status;
            return library;
        }

        Shot("library-8-collections", (s, _) => Chosen(s, Eight(clips)));
        Shot("library-plus-liste", (s, _) =>
        {
            Chosen(s, Eight(clips));
            s.TabTo(ChipLine.ChipsId);
            s.Key(Vk.End, 0);
            s.Key(Vk.Enter, 0);
        }, focus: true);
        Shot("library-nouvelle-collection", (s, _) =>
        {
            Chosen(s, SampleLibrary.Labeled(clips));
            s.TabTo(ChipLine.NewId);
            s.Key(Vk.Enter, 0);
            s.EditChanged("dogs");
        }, focus: true);
        Shot("library-nom-pris", (s, _) =>
        {
            Chosen(s, SampleLibrary.Labeled(clips));
            s.TabTo(ChipLine.NewId);
            s.Key(Vk.Enter, 0);
            s.EditChanged("pets");
        }, focus: true);
        Shot("library-nom-vide", (s, _) =>
        {
            Chosen(s, SampleLibrary.Labeled(clips));
            s.TabTo(ChipLine.NewId);
            s.Key(Vk.Enter, 0);
        }, focus: true);
        Shot("library-supprimer", (s, _) =>
        {
            ShowClips(s, SampleLibrary.Labeled(clips));
            s.TabTo(SettingsScreen.ClipFilterId);
            s.Key(Vk.Down, 0);
            s.Key(Vk.Down, 0);
            s.SelectClip(chosenClip);
            s.TabTo(CollectionActions.DeleteId);
            s.Key(Vk.Enter, 0);
        }, focus: true);
        Shot("library-erreur-fichier", (s, _) => Chosen(s, InTrouble(clips, Library.LibraryStatus.Corrupt)));
        Shot("library-erreur-lecture", (s, _) => Chosen(s, InTrouble(clips, Library.LibraryStatus.Unreadable)));
        Shot("library-version-recente", (s, _) => Chosen(s, InTrouble(clips, Library.LibraryStatus.TooNew)));
        Shot("library-recommencer", (s, _) =>
        {
            Chosen(s, InTrouble(clips, Library.LibraryStatus.Corrupt));
            s.TabTo(SettingsScreen.StartOverId);
            s.Key(Vk.Enter, 0);
        }, focus: true);

        return FolderPicker.SelfCheck() ? 0 : 3;
    }
}
