using Hindsound.App;
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
    public void A_clip_that_falls_back_to_wav_fails_the_proof()
    {
        using var temp = new TempFolder();
        var folder = temp.Combine("proof");

        var code = Mp3Proof.Write(folder, new Journal(temp.Combine("proof.log")), (_, _, _) => throw new DllNotFoundException("no LAME"));

        Assert.Equal(Mp3Proof.ExitFellBackToWav, code);
        Assert.EndsWith(".wav", Assert.Single(Directory.GetFiles(folder, "*", SearchOption.AllDirectories)), StringComparison.Ordinal);
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
