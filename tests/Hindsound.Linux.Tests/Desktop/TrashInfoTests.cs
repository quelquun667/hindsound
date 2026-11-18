using System.Text;
using Hindsound.Linux.Desktop;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>The text of a trash: the .trashinfo, the way its Path is written, the date and the names. No file system, so these run everywhere.</summary>
public sealed class TrashInfoTests
{
    [Theory]
    [InlineData("/home/u/clips/clip.wav", "/home/u/clips/clip.wav")]
    [InlineData("/home/u/My Clips/a b.wav", "/home/u/My%20Clips/a%20b.wav")]
    [InlineData("/home/u/100%/a#b?c.wav", "/home/u/100%25/a%23b%3Fc.wav")]
    [InlineData("/home/u/caf\u00e9/\u97f3.wav", "/home/u/caf%C3%A9/%E9%9F%B3.wav")]
    [InlineData("/home/u/a~b_c-d.wav", "/home/u/a~b_c-d.wav")]
    [InlineData("clips/\U0001F3B5.wav", "clips/%F0%9F%8E%B5.wav")]
    public void ThePathIsPercentEncodedByBytes(string path, string written) => Assert.Equal(written, TrashInfo.EncodePath(path));

    [Fact]
    public void TheFileIsTheGroupThePathAndTheDateEachEndedByLf()
    {
        var text = TrashInfo.Content("/home/u/My Clips/a.wav", new DateTime(2026, 11, 18, 9, 5, 7));

        Assert.Equal("[Trash Info]\nPath=/home/u/My%20Clips/a.wav\nDeletionDate=2026-11-18T09:05:07\n", text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void TheDateIsLocalTimeWithNoZoneAndTwoDigitsEverywhere()
    {
        Assert.Equal("2026-01-02T03:04:05", TrashInfo.DeletionDate(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local)));
        Assert.Equal("2026-12-31T23:59:59", TrashInfo.DeletionDate(new DateTime(2026, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)));
    }

    [Fact]
    public void TheDateDoesNotFollowTheCulture()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");

            Assert.Equal("2026-11-18T09:05:07", TrashInfo.DeletionDate(new DateTime(2026, 11, 18, 9, 5, 7)));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    [Fact]
    public void AFreeNameIsKept() => Assert.Equal("clip.wav", TrashInfo.UniqueName("clip.wav", _ => false));

    [Fact]
    public void ATakenNameTakesANumberBeforeItsExtension()
    {
        var taken = new HashSet<string> { "clip.wav", "clip.2.wav" };

        Assert.Equal("clip.3.wav", TrashInfo.UniqueName("clip.wav", taken.Contains));
    }

    [Theory]
    [InlineData("README", "README.2")]
    [InlineData(".hidden", ".hidden.2")]
    [InlineData("a.tar.gz", "a.tar.2.gz")]
    [InlineData("trail.", "trail.2.")]
    public void TheNumberGoesBeforeTheLastExtensionOnly(string name, string second) =>
        Assert.Equal(second, TrashInfo.UniqueName(name, candidate => candidate == name));

    [Fact]
    public void ANameIsAskedOnlyOnceEach()
    {
        var asked = new List<string>();

        TrashInfo.UniqueName("a.wav", candidate => { asked.Add(candidate); return asked.Count < 3; });

        Assert.Equal(["a.wav", "a.2.wav", "a.3.wav"], asked);
    }

    [Fact]
    public void WhenEveryNameIsTakenThereIsNoName() => Assert.Null(TrashInfo.UniqueName("a.wav", _ => true));

    [Fact]
    public void ALongNameIsCutSoThatItsTrashInfoStillFits()
    {
        var name = new string('a', 250) + ".wav";

        var kept = TrashInfo.UniqueName(name, _ => false)!;

        Assert.EndsWith(".wav", kept);
        Assert.True(Encoding.UTF8.GetByteCount(kept + TrashInfo.InfoSuffix) <= 255);
        Assert.StartsWith("aaaa", kept);
    }

    [Fact]
    public void ACutNameStaysUniqueAndStillFitsWithItsNumber()
    {
        var name = new string('a', 250) + ".wav";
        var first = TrashInfo.UniqueName(name, _ => false)!;

        var second = TrashInfo.UniqueName(name, candidate => candidate == first)!;

        Assert.NotEqual(first, second);
        Assert.EndsWith(".2.wav", second);
        Assert.True(Encoding.UTF8.GetByteCount(second + TrashInfo.InfoSuffix) <= 255);
    }

    [Fact]
    public void ACutNeverSplitsACharacter()
    {
        // Each note is four bytes in UTF-8 and two chars in UTF-16: a cut by bytes or by chars could land inside one.
        var name = string.Concat(Enumerable.Repeat("\U0001F3B5", 70)) + ".wav";

        var kept = TrashInfo.UniqueName(name, _ => false)!;

        Assert.Equal(kept, new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(Encoding.UTF8.GetBytes(kept)));
        Assert.True(Encoding.UTF8.GetByteCount(kept + TrashInfo.InfoSuffix) <= 255);
        Assert.EndsWith("\U0001F3B5.wav", kept);
    }

    [Fact]
    public void AnExtensionTooLongToLeaveRoomForAStemGivesNoName() =>
        Assert.Null(TrashInfo.UniqueName("a." + new string('b', 250), _ => false));

    [Theory]
    [InlineData("/", null)]
    [InlineData("/a", "/")]
    [InlineData("/a/b", "/a")]
    [InlineData("/a/b/", "/a")]
    public void TheParentIsTheFolderAbove(string path, string? parent) => Assert.Equal(parent, TrashInfo.Parent(path));

    [Theory]
    [InlineData(null, "/home/u/clips", "a.wav", "/home/u/clips/a.wav")]
    [InlineData("/mnt/usb", "/mnt/usb/clips/live", "a b.wav", "clips/live/a b.wav")]
    [InlineData("/mnt/usb", "/mnt/usb", "a.wav", "a.wav")]
    [InlineData("/", "/data/clips", "a.wav", "data/clips/a.wav")]
    [InlineData("/", "/", "a.wav", "a.wav")]
    public void ThePathIsFullForTheHomeAndFromTheTopDirectoryForAnother(string? top, string folder, string name, string written) =>
        Assert.Equal(written, TrashInfo.OriginalPath(top, folder, name));
}
