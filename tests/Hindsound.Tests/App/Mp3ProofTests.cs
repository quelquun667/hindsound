using Hindsound.App;
using Hindsound.Export;
using Hindsound.Tests.Export;

namespace Hindsound.Tests.App;

/// <summary><c>--prove-mp3</c>: what the release workflow runs on the installed exe.</summary>
public class Mp3ProofTests
{
    [Fact]
    public void The_option_takes_its_folder()
    {
        Assert.Equal("proof", AppOptions.Parse(["--no-mic", "--prove-mp3", "proof"]).ProveMp3Folder);
        Assert.Null(AppOptions.Parse(["--prove-mp3"]).ProveMp3Folder);
        Assert.Null(AppOptions.Parse([]).ProveMp3Folder);
    }

    [Fact]
    public void An_option_after_a_folder_option_is_not_taken_for_its_folder()
    {
        var options = AppOptions.Parse(["--prove-mp3", "--no-mic"]);
        Assert.Null(options.ProveMp3Folder);
        Assert.True(options.NoMic);

        Assert.Null(AppOptions.Parse(["--dump-notifications", "--no-mic"]).DumpNotificationsFolder);
        Assert.Null(AppOptions.Parse(["--dump-settings", "--no-mic"]).DumpSettingsFolder);
    }

    [Fact]
    public void One_second_of_sine_comes_out_as_one_mp3()
    {
        using var temp = new TempFolder();
        var folder = temp.Combine("proof");

        Assert.Equal(0, Mp3Proof.Write(folder, new Journal(temp.Combine("proof.log"))));

        var file = Assert.Single(Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
        Assert.EndsWith(".mp3", file, StringComparison.Ordinal);
        Assert.True(new FileInfo(file).Length > 0);
    }

    [Fact]
    public void A_relative_folder_starts_at_the_current_directory()
    {
        using var temp = new TempFolder();

        var code = Mp3Proof.Write(Path.Combine("rel", "a", "b"), new Journal(temp.Combine("proof.log")), openMp3: null, baseDirectory: temp.Path);

        Assert.Equal(0, code);
        var clip = Assert.Single(Directory.GetFiles(temp.Path, "*.mp3", SearchOption.AllDirectories));
        Assert.Equal(temp.Combine("rel", "a", "b"), Path.GetDirectoryName(clip));
    }

    [Fact]
    public void A_clip_that_falls_back_to_wav_fails_the_proof()
    {
        using var temp = new TempFolder();
        var folder = temp.Combine("proof");

        var code = Mp3Proof.Write(folder, new Journal(temp.Combine("proof.log")), (_, _, _) => throw new DllNotFoundException("no LAME"));

        Assert.Equal(Mp3Proof.ExitFellBackToWav, code);
        Assert.EndsWith(".wav", Assert.Single(Directory.GetFiles(folder, "*", SearchOption.AllDirectories)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_rescue_folder_stays_inside_the_folder_given()
    {
        using var temp = new TempFolder();
        var folder = Path.GetFullPath(temp.Combine("proof"));

        var rescue = Mp3Proof.RescueFolderFor(folder);

        Assert.False(Path.GetRelativePath(folder, rescue).StartsWith("..", StringComparison.Ordinal));
        Assert.NotEqual(folder, rescue);
        Assert.NotEqual(Path.GetFullPath(ClipSaver.DefaultRescueFolder), rescue);
    }

    [Fact]
    public void A_clip_saved_in_the_rescue_folder_fails_the_proof()
    {
        using var temp = new TempFolder();
        var folder = temp.Combine("proof");
        var file = temp.Combine("a-file");
        File.WriteAllText(file, "not a folder");

        // The clip is told to go to a file: it cannot, and goes to the rescue folder of the proof, never to the real one.
        var code = Mp3Proof.Write(folder, new Journal(temp.Combine("proof.log")), openMp3: null, exportFolder: file);

        Assert.Equal(Mp3Proof.ExitFailed, code);
        var clip = Assert.Single(Directory.GetFiles(temp.Path, "*.mp3", SearchOption.AllDirectories));
        Assert.Equal(Mp3Proof.RescueFolderFor(Path.GetFullPath(folder)), Path.GetDirectoryName(clip));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_fails_the_proof()
    {
        using var temp = new TempFolder();
        var file = temp.Combine("a-file");
        File.WriteAllText(file, "not a folder");

        Assert.NotEqual(0, Mp3Proof.Write(file, new Journal(temp.Combine("proof.log"))));
    }
}
