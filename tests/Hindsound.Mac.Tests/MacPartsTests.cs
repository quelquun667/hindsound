using System.Text;
using Hindsound.App;
using Hindsound.Library;
using Hindsound.Mac;
using Hindsound.Mac.App;
using Hindsound.Mac.Window;
using Hindsound.Settings;
using Hindsound.Unix.App;
using Hindsound.Unix.Export;
using Hindsound.Unix.Window;

namespace Hindsound.Mac.Tests;

/// <summary>What the Mac app decides without asking macOS: names, folders, features, the place of the single instance. On any system.</summary>
public sealed class MacPartsTests
{
    [Fact]
    public void Each_library_imported_by_its_Linux_name_is_a_dylib_of_the_bundle_on_macOS()
    {
        Assert.Equal("libSDL3.0.dylib", UnixLibraries.MacFiles[SdlLibrary.SystemName]);
        Assert.Equal("libfreetype.6.dylib", UnixLibraries.MacFiles["libfreetype.so.6"]);
        Assert.Equal("libharfbuzz.0.dylib", UnixLibraries.MacFiles["libharfbuzz.so.0"]);
        Assert.Equal("libmp3lame.0.dylib", UnixLibraries.MacFiles[LameLibrary.SystemName]);
        Assert.Equal(4, UnixLibraries.MacFiles.Count);
    }

    [Fact]
    public void Linux_keeps_the_names_of_today()
    {
        Assert.Equal("libSDL3.so.0", SdlLibrary.SystemName);
        Assert.Equal("libmp3lame.so.0", LameLibrary.SystemName);
        Assert.Equal("libfreetype.so.6", UnixLibraries.FreeType);
        Assert.Equal("libharfbuzz.so.0", UnixLibraries.HarfBuzz);
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Equal(SdlLibrary.ProgramCandidates, SdlLibrary.LoadCandidates);
            Assert.Equal(LameLibrary.ProgramCandidates, LameLibrary.LoadCandidates);
            Assert.Equal("libSDL3.so.0", UnixLibraries.FileFor(SdlLibrary.SystemName));
        }
    }

    [Fact]
    public void A_dylib_is_looked_for_in_the_Frameworks_of_the_bundle_then_next_to_the_program_never_by_bare_name()
    {
        var program = Path.Combine(Path.GetTempPath(), "Hindsound.app", "Contents", "MacOS");

        var candidates = UnixLibraries.MacCandidates(program, "libSDL3.0.dylib");

        Assert.Equal(
            [Path.Combine(Path.GetTempPath(), "Hindsound.app", "Contents", "Frameworks", "libSDL3.0.dylib"), Path.Combine(program, "libSDL3.0.dylib")],
            candidates);
        Assert.All(candidates, candidate => Assert.True(Path.IsPathRooted(candidate)));
    }

    [Fact]
    public void The_features_of_macOS_turn_off_what_is_not_written_yet()
    {
        var features = PlatformFeatures.Mac(() => "/Applications/Hindsound.app/Contents/MacOS/hindsound --save");

        Assert.False(features.SingleApp);
        Assert.False(features.NotificationLook);
        Assert.False(features.TestNotification);
        Assert.False(features.ConfirmSound);
        Assert.Equal(ShortcutMode.Command, features.Shortcuts());
        Assert.Equal("Open at login", features.StartupLabel);
        Assert.Equal("Trash", features.TrashName);
        Assert.Equal("Move it to the Trash", features.Words("Move it to the Recycle Bin"));
        Assert.Equal("/Applications/Hindsound.app/Contents/MacOS/hindsound --save", features.SaveCommand());
    }

    [Theory]
    [InlineData("/Applications/Hindsound.app/Contents/MacOS/hindsound", "/Applications/Hindsound.app/Contents/MacOS/hindsound --save")]
    [InlineData("/Users/you/My Apps/Hindsound.app/Contents/MacOS/hindsound", null)]
    [InlineData("/Users/you/a'b/hindsound", null)]
    [InlineData("hindsound", null)]
    [InlineData(null, null)]
    public void The_save_command_is_the_program_of_the_bundle_when_a_shell_reads_its_path_as_it_is(string? program, string? expected) =>
        Assert.Equal(expected, MacPlatform.SaveCommand(program));

    [Fact]
    public void On_macOS_the_settings_go_to_Application_Support_and_the_log_to_Library_Logs()
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        string? Resolve(Environment.SpecialFolder folder) => folder == Environment.SpecialFolder.UserProfile ? home : null;

        Assert.Equal(Environment.SpecialFolder.LocalApplicationData, UserFolders.OnThisSystem(Environment.SpecialFolder.ApplicationData, mac: true));
        Assert.Equal(Environment.SpecialFolder.ApplicationData, UserFolders.OnThisSystem(Environment.SpecialFolder.ApplicationData, mac: false));
        Assert.Equal(Environment.SpecialFolder.MyMusic, UserFolders.OnThisSystem(Environment.SpecialFolder.MyMusic, mac: true));
        Assert.Equal(Path.Combine(home, "Library", "Logs", "Hindsound", "hindsound.log"), UserFolders.MacLogs(true, Resolve, "Hindsound", "hindsound.log"));
        Assert.Null(UserFolders.MacLogs(false, Resolve, "Hindsound", "hindsound.log"));
    }

    [Fact]
    public void The_single_instance_of_macOS_lives_in_TMPDIR_else_in_Library_Caches()
    {
        static Func<string, string?> Env(string? tmp) => name => name == InstancePlace.MacTemporaryVariable ? tmp : null;
        const string Tmp = "/var/folders/zz/abcdefghijklmnopqrstuvwxyz0000gn/T/";

        var inTmp = InstancePlace.ForMac(Env(Tmp), "/Users/you", "k");
        var inCaches = InstancePlace.ForMac(Env(null), "/Users/you", "k");

        Assert.NotNull(inTmp);
        Assert.Equal("/var/folders/zz/abcdefghijklmnopqrstuvwxyz0000gn/T", inTmp.Folder);
        Assert.False(inTmp.OwnFolder);
        Assert.Equal(inTmp.Folder + "/" + InstancePlace.FileNameFor("k") + ".sock", inTmp.Socket);
        Assert.NotNull(inCaches);
        Assert.Equal("/Users/you/Library/Caches/Hindsound", inCaches.Folder);
        Assert.True(inCaches.OwnFolder);
        Assert.Null(InstancePlace.ForMac(Env("relative"), null, "k"));
    }

    [Fact]
    public void A_socket_path_of_macOS_holds_103_bytes_at_most()
    {
        var name = InstancePlace.FileNameFor("k") + ".sock";
        string FolderFor(int socketBytes) => "/" + new string('a', socketBytes - "/".Length - "/".Length - name.Length);
        Func<string, string?> Tmp(string folder) => variable => variable == InstancePlace.MacTemporaryVariable ? folder : null;

        var fits = InstancePlace.ForMac(Tmp(FolderFor(InstancePlace.MaxMacSocketBytes)), null, "k");

        Assert.NotNull(fits);
        Assert.Equal(103, Encoding.UTF8.GetByteCount(fits.Socket));
        Assert.Null(InstancePlace.ForMac(Tmp(FolderFor(InstancePlace.MaxMacSocketBytes + 1)), null, "k"));
        Assert.Equal(107, InstancePlace.MaxSocketBytes);
    }

    [Fact]
    public void Folders_that_differ_only_by_case_are_one_folder_where_the_system_ignores_case()
    {
        var root = Path.Combine(Path.GetTempPath(), "Clips");

        var same = string.Equals(ClipFolders.Key(Path.Combine(root, "Valorant")), ClipFolders.Key(Path.Combine(root, "VALORANT")), PathRules.Comparison);

        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), same);
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), PathRules.IgnoresCase);
    }

    [Fact]
    public void The_fonts_of_a_bundle_are_in_its_Resources()
    {
        var bundle = Path.Combine(Path.GetTempPath(), "hindsound-bundle-" + Guid.NewGuid().ToString("N"), "Hindsound.app", "Contents");
        var program = Path.Combine(bundle, "MacOS");
        Directory.CreateDirectory(program);
        try
        {
            Assert.Equal(Path.Combine(program, "fonts"), MacText.FontDirectory(null, program));
            Directory.CreateDirectory(Path.Combine(bundle, "Resources"));
            Assert.Equal(Path.Combine(bundle, "Resources", "fonts"), MacText.FontDirectory(null, program));
            Assert.Equal("ci", MacText.FontDirectory("ci", program));
            Assert.Equal("window: SDL from the bundle, fonts from the bundle",
                MacWindowSupport.FoundLine("/x/Hindsound.app/Contents/Frameworks/libSDL3.0.dylib", program, null));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(bundle))!, recursive: true);
        }
    }

    [Fact]
    public void Between_windows_the_loop_waits_in_its_idle_pump_and_a_post_wakes_it()
    {
        var idle = new CountingPump();
        using var loop = new UnixLoop(nowMs: null, idle: idle);
        var ran = new List<int>();
        var thread = Environment.CurrentManagedThreadId;

        var poster = new Thread(() =>
        {
            while (Volatile.Read(ref idle.Waits) == 0)
            {
                Thread.Sleep(1);
            }

            loop.Post(() => ran.Add(Environment.CurrentManagedThreadId));
            loop.Post(loop.Quit);
        });
        poster.Start();
        loop.Run();
        poster.Join();

        Assert.Equal([thread], ran);
        Assert.True(idle.Wakes >= 1);
    }

    [Fact]
    public void A_window_pump_takes_over_from_the_idle_one_and_gives_it_back()
    {
        var idle = new CountingPump();
        var window = new CountingPump();
        using var loop = new UnixLoop(nowMs: null, idle: idle);

        loop.Pump = window;
        loop.RunOnce(0);
        loop.Pump = null;
        loop.RunOnce(0);

        Assert.Equal(1, window.Waits);
        Assert.Equal(1, idle.Waits);
        Assert.Equal(1, idle.Wakes);
        Assert.Equal(1, window.Wakes);
    }

    private sealed class CountingPump : ILoopPump
    {
        public int Waits;
        public int Wakes;
        private readonly SemaphoreSlim _woken = new(0);

        public void Wait(int timeoutMs)
        {
            Interlocked.Increment(ref Waits);
            _woken.Wait(timeoutMs == Timeout.Infinite ? 5000 : timeoutMs);
        }

        public void Wake()
        {
            Interlocked.Increment(ref Wakes);
            _woken.Release();
        }
    }
}
