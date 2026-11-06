using Hindsound.Settings;

namespace Hindsound.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HindsoundTests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Defaults_MatchThePlan()
    {
        var s = new AppSettings();

        Assert.Equal("Ctrl+Shift+F10", s.Hotkeys.SaveDefault);
        Assert.Null(s.Hotkeys.Save15s);
        Assert.Null(s.Hotkeys.CopyLast);
        Assert.Equal(30, s.ClipSeconds);
        Assert.Equal(300, s.BufferSeconds);
        Assert.Equal("default", s.Audio.OutputDeviceId);
        Assert.Equal("mix", s.Audio.Mode);
        Assert.Equal("mp3", s.Export.Format);
        Assert.Equal(192, s.Export.Mp3Bitrate);
        Assert.Equal("loudness", s.Export.Normalization);
        Assert.Equal(-16, s.Export.TargetLufs);
        Assert.Equal(-1, s.Export.PeakCeilingDb);
        Assert.Equal("%USERPROFILE%\\Music\\Hindsound", s.Export.OutputFolder);
        Assert.Equal("Hindsound_{app}_{date}_{time}", s.Export.FileNameTemplate);
        Assert.True(s.Export.CopyToClipboard);
        Assert.False(s.General.StartWithWindows);
        Assert.Equal("#D4F53C", s.General.AccentColor);
        Assert.Equal("right", s.General.NotificationPosition);
        Assert.Equal("auto", s.General.FullscreenGames);
        Assert.Equal("whenNoOverlay", s.General.ConfirmSound);
    }

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var store = new SettingsStore(FilePath);

        var s = store.Load();

        Assert.Equal(SettingsLoadStatus.Missing, store.LastStatus);
        Assert.Equal(300, s.BufferSeconds);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void RoundTrip_KeepsEveryValue()
    {
        var store = new SettingsStore(FilePath);
        var s = new AppSettings
        {
            ClipSeconds = 60,
            BufferSeconds = 600,
            Hotkeys = { SaveDefault = "Ctrl+Alt+H", Save15s = "F9", Save1m = "Ctrl+1", Save5m = "Shift+F5", TogglePause = "Alt+P", CopyLast = "Ctrl+Shift+C" },
            Audio = { OutputDeviceId = "dev-1", MicDeviceId = "disabled", Mode = "multitrack", SystemGainDb = -3.5, MicGainDb = 6 },
            Export = { Format = "wav", Mp3Bitrate = 320, Normalization = "peak", TargetLufs = -14, PeakCeilingDb = -2, OutputFolder = "D:\\Clips", FileNameTemplate = "clip_{date}", CopyToClipboard = false },
            General = { StartWithWindows = false, Notifications = false, AccentColor = "#35D6FF", NotificationPosition = "left", FullscreenGames = "soundOnly", ConfirmSound = "never" },
        };

        store.Save(s);
        var back = store.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, store.LastStatus);
        Assert.Equal(60, back.ClipSeconds);
        Assert.Equal(600, back.BufferSeconds);
        Assert.Equal("Ctrl+Alt+H", back.Hotkeys.SaveDefault);
        Assert.Equal("F9", back.Hotkeys.Save15s);
        Assert.Equal("Ctrl+1", back.Hotkeys.Save1m);
        Assert.Equal("Shift+F5", back.Hotkeys.Save5m);
        Assert.Equal("Alt+P", back.Hotkeys.TogglePause);
        Assert.Equal("Ctrl+Shift+C", back.Hotkeys.CopyLast);
        Assert.Equal("dev-1", back.Audio.OutputDeviceId);
        Assert.Equal("disabled", back.Audio.MicDeviceId);
        Assert.Equal("multitrack", back.Audio.Mode);
        Assert.Equal(-3.5, back.Audio.SystemGainDb);
        Assert.Equal(6, back.Audio.MicGainDb);
        Assert.Equal("wav", back.Export.Format);
        Assert.Equal(320, back.Export.Mp3Bitrate);
        Assert.Equal("peak", back.Export.Normalization);
        Assert.Equal(-14, back.Export.TargetLufs);
        Assert.Equal(-2, back.Export.PeakCeilingDb);
        Assert.Equal("D:\\Clips", back.Export.OutputFolder);
        Assert.Equal("clip_{date}", back.Export.FileNameTemplate);
        Assert.False(back.Export.CopyToClipboard);
        Assert.False(back.General.StartWithWindows);
        Assert.False(back.General.Notifications);
        Assert.Equal("#35D6FF", back.General.AccentColor);
        Assert.Equal("left", back.General.NotificationPosition);
        Assert.Equal("soundOnly", back.General.FullscreenGames);
        Assert.Equal("never", back.General.ConfirmSound);
    }

    [Fact]
    public void Save_WritesCamelCaseKeysAndNoTemporaryFile()
    {
        var store = new SettingsStore(FilePath);

        store.Save(new AppSettings());
        store.Save(new AppSettings());

        var text = File.ReadAllText(FilePath);
        Assert.Contains("\"saveDefault\": \"Ctrl+Shift+F10\"", text);
        Assert.Contains("\"save15s\": null", text);
        Assert.Contains("\"mp3Bitrate\": 192", text);
        Assert.Contains("\"accentColor\": \"#D4F53C\"", text);
        Assert.Equal([FilePath], Directory.GetFiles(_folder));
    }

    [Fact]
    public void BrokenFile_GivesDefaultsAndIsKeptAsBak()
    {
        const string broken = "{ \"clipSeconds\": 60, \"hotkeys\": ";
        File.WriteAllText(FilePath, broken);
        var store = new SettingsStore(FilePath);

        var s = store.Load();

        Assert.Equal(SettingsLoadStatus.Corrupt, store.LastStatus);
        Assert.Equal(30, s.ClipSeconds);
        Assert.Equal(FilePath + ".bak", store.LastBackupPath);
        Assert.Equal(broken, File.ReadAllText(FilePath + ".bak"));
    }

    [Fact]
    public void SecondBrokenFile_DoesNotOverwriteTheFirstBak()
    {
        var store = new SettingsStore(FilePath);
        File.WriteAllText(FilePath, "first broken");
        store.Load();
        File.WriteAllText(FilePath, "second broken");
        store.Load();

        Assert.Equal("first broken", File.ReadAllText(FilePath + ".bak"));
        Assert.Equal("second broken", File.ReadAllText(FilePath + ".1.bak"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("{ \"clipSeconds\": \"thirty\" }")]
    public void UnreadableContent_CountsAsBroken(string content)
    {
        File.WriteAllText(FilePath, content);
        var store = new SettingsStore(FilePath);

        store.Load();

        Assert.Equal(SettingsLoadStatus.Corrupt, store.LastStatus);
        Assert.Equal(content, File.ReadAllText(FilePath + ".bak"));
    }

    private FileStream Lock() => new(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

    [Fact]
    public void LockedFile_GivesDefaultsAndStaysWhereItIs()
    {
        const string good = "{ \"clipSeconds\": 60 }";
        File.WriteAllText(FilePath, good);
        var store = new SettingsStore(FilePath);

        AppSettings s;
        using (Lock())
        {
            s = store.Load();
        }

        Assert.Equal(SettingsLoadStatus.Unreadable, store.LastStatus);
        Assert.Null(store.LastBackupPath);
        Assert.Equal(30, s.ClipSeconds);
        Assert.Equal(good, File.ReadAllText(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(_folder));
    }

    [Fact]
    public void LoadPatiently_LooksAgainUntilTheLockIsGone()
    {
        File.WriteAllText(FilePath, "{ \"clipSeconds\": 60 }");
        var store = new SettingsStore(FilePath);
        var held = Lock();
        var waits = 0;

        var s = store.LoadPatiently(3, () =>
        {
            if (++waits == 2)
            {
                held.Dispose();
            }
        });

        Assert.Equal(2, waits);
        Assert.Equal(SettingsLoadStatus.Loaded, store.LastStatus);
        Assert.Equal(60, s.ClipSeconds);
    }

    [Fact]
    public void LoadPatiently_GivesUpAfterTheLastTry()
    {
        File.WriteAllText(FilePath, "{ \"clipSeconds\": 60 }");
        var store = new SettingsStore(FilePath);
        var waits = 0;

        using (Lock())
        {
            store.LoadPatiently(3, () => waits++);
        }

        Assert.Equal(2, waits);
        Assert.Equal(SettingsLoadStatus.Unreadable, store.LastStatus);
    }

    [Fact]
    public void LoadPatiently_DoesNotWaitForABrokenFile()
    {
        File.WriteAllText(FilePath, "not json");
        var store = new SettingsStore(FilePath);
        var waits = 0;

        store.LoadPatiently(3, () => waits++);

        Assert.Equal(0, waits);
        Assert.Equal(SettingsLoadStatus.Corrupt, store.LastStatus);
    }

    [Fact]
    public void Read_BrokenFile_ChangesNothing()
    {
        const string broken = "{ \"clipSeconds\": \"30\", \"audio\": { \"micDeviceId\": \"disabled\" } }";
        File.WriteAllText(FilePath, broken);
        var store = new SettingsStore(FilePath);

        var read = store.Read();

        Assert.Equal(SettingsLoadStatus.Corrupt, read.Status);
        Assert.Null(read.Settings);
        Assert.Equal(broken, File.ReadAllText(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(_folder));
    }

    [Fact]
    public void Read_LockedFile_IsUnreadable()
    {
        File.WriteAllText(FilePath, "{ \"clipSeconds\": 60 }");
        var store = new SettingsStore(FilePath);

        using (Lock())
        {
            var read = store.Read();

            Assert.Equal(SettingsLoadStatus.Unreadable, read.Status);
            Assert.Null(read.Settings);
        }

        Assert.Equal([FilePath], Directory.GetFiles(_folder));
    }

    [Fact]
    public void Read_MissingFile_IsMissingAndCreatesNothing()
    {
        var read = new SettingsStore(FilePath).Read();

        Assert.Equal(SettingsLoadStatus.Missing, read.Status);
        Assert.Null(read.Settings);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Read_MissingFolder_IsMissing()
    {
        var read = new SettingsStore(Path.Combine(_folder, "not there", "settings.json")).Read();

        Assert.Equal(SettingsLoadStatus.Missing, read.Status);
    }

    [Fact]
    public void Read_GoodFile_GivesTheNormalizedSettings()
    {
        File.WriteAllText(FilePath, "{ \"clipSeconds\": -5 }");

        var read = new SettingsStore(FilePath).Read();

        Assert.Equal(SettingsLoadStatus.Loaded, read.Status);
        Assert.Equal(1, read.Settings!.ClipSeconds);
    }

    [Fact]
    public void KeepCopyOfBrokenFile_LeavesTheFileAndNeverOverwritesABak()
    {
        var store = new SettingsStore(FilePath);
        File.WriteAllText(FilePath + ".bak", "older");
        File.WriteAllText(FilePath, "broken");

        var copy = store.KeepCopyOfBrokenFile();

        Assert.Equal(FilePath + ".1.bak", copy);
        Assert.Equal("broken", File.ReadAllText(FilePath));
        Assert.Equal("broken", File.ReadAllText(copy!));
        Assert.Equal("older", File.ReadAllText(FilePath + ".bak"));
    }

    [Fact]
    public void KeepCopyOfBrokenFile_SaysSoWhenTheFileCannotBeCopied()
    {
        File.WriteAllText(FilePath, "broken");
        var store = new SettingsStore(FilePath);

        using (Lock())
        {
            Assert.Null(store.KeepCopyOfBrokenFile());
        }

        Assert.Equal([FilePath], Directory.GetFiles(_folder));
    }

    [Fact]
    public void MissingKeys_TakeTheirDefaults()
    {
        File.WriteAllText(FilePath, "{ \"clipSeconds\": 120, \"export\": { \"format\": \"wav\" }, \"general\": null }");
        var store = new SettingsStore(FilePath);

        var s = store.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, store.LastStatus);
        Assert.Equal(120, s.ClipSeconds);
        Assert.Equal(300, s.BufferSeconds);
        Assert.Equal("wav", s.Export.Format);
        Assert.Equal(192, s.Export.Mp3Bitrate);
        Assert.Equal("Ctrl+Shift+F10", s.Hotkeys.SaveDefault);
        Assert.Equal("#D4F53C", s.General.AccentColor);
        Assert.True(s.General.Notifications);
    }

    [Fact]
    public void OutOfRangeValues_AreBroughtBackIntoRange()
    {
        File.WriteAllText(FilePath, """
            {
              "clipSeconds": -5,
              "bufferSeconds": 999999,
              "audio": { "systemGainDb": 500, "micGainDb": -500, "mode": "weird" },
              "export": { "mp3Bitrate": 100, "targetLufs": 10, "peakCeilingDb": -90, "format": "ogg", "normalization": "LOUDNESS" },
              "general": { "accentColor": "#101010", "notificationPosition": "middle" },
              "hotkeys": { "saveDefault": "nonsense", "save1m": "Ctrl+Nope" }
            }
            """);

        var s = new SettingsStore(FilePath).Load();

        Assert.Equal(1, s.ClipSeconds);
        Assert.Equal(1800, s.BufferSeconds);
        Assert.Equal(24, s.Audio.SystemGainDb);
        Assert.Equal(-60, s.Audio.MicGainDb);
        Assert.Equal("mix", s.Audio.Mode);
        Assert.Equal(192, s.Export.Mp3Bitrate);
        Assert.Equal(-5, s.Export.TargetLufs);
        Assert.Equal(-20, s.Export.PeakCeilingDb);
        Assert.Equal("mp3", s.Export.Format);
        Assert.Equal("loudness", s.Export.Normalization);
        Assert.Equal("#D4F53C", s.General.AccentColor);
        Assert.Equal("right", s.General.NotificationPosition);
        Assert.Equal("Ctrl+Shift+F10", s.Hotkeys.SaveDefault);
        Assert.Null(s.Hotkeys.Save1m);
    }

    [Theory]
    [InlineData(120, 60, null, null, null, 120)]
    [InlineData(30, 10, null, null, null, 30)]
    [InlineData(30, 100, "F9", null, null, 100)]
    [InlineData(30, 100, null, null, "F9", 300)]
    [InlineData(30, 100, null, "F9", null, 100)]
    [InlineData(30, 20, null, "F9", null, 60)]
    public void Buffer_IsNeverShorterThanTheLongestClip(int clip, int buffer, string? s15, string? s1m, string? s5m, int expected)
    {
        var s = new AppSettings { ClipSeconds = clip, BufferSeconds = buffer, Hotkeys = { Save15s = s15, Save1m = s1m, Save5m = s5m } };

        Assert.Equal(expected, s.Normalize().BufferSeconds);
    }
}
