using System.Text;
using Hindsound.Updates;

namespace Hindsound.Tests.Updates;

public class UpdateManifestTests
{
    private const string GoodUrl = "https://github.com/quelquun667/hindsound/releases/download/v1.2.3/Hindsound-1.2.3-Setup.exe";
    private static readonly string GoodSha = new('a', 64);

    private static string Json(
        string product = "\"Hindsound\"",
        string version = "\"1.2.3\"",
        string url = "\"" + GoodUrl + "\"",
        string? sha256 = null,
        string size = "8200000",
        string published = "\"2026-11-06T12:00:00Z\"") =>
        $"{{\"product\":{product},\"version\":{version},\"url\":{url},\"sha256\":{sha256 ?? "\"" + GoodSha + "\""},\"size\":{size},\"published\":{published}}}";

    private static ManifestStatus Read(string json) => UpdateManifestReader.Read(Encoding.UTF8.GetBytes(json), out _);

    [Fact]
    public void AnExactManifestIsRead()
    {
        var status = UpdateManifestReader.Read(Encoding.UTF8.GetBytes(Json()), out var manifest);

        Assert.Equal(ManifestStatus.Valid, status);
        Assert.Equal(new UpdateVersion(1, 2, 3), manifest!.Version);
        Assert.Equal(GoodUrl, manifest.Url);
        Assert.Equal(GoodSha, manifest.Sha256);
        Assert.Equal(8_200_000, manifest.Size);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero), manifest.Published);
    }

    [Fact]
    public void AFieldItDoesNotKnowDoesNotLockOutOldInstalls()
    {
        var json = Json().Insert(1, "\"notes\":\"new field of a later version\",");

        Assert.Equal(ManifestStatus.Valid, Read(json));
    }

    [Fact]
    public void ExactlySixteenKilobytesIsAllowedAndOneMoreIsNot()
    {
        var bare = Json();
        var filler = new string(' ', UpdateManifestReader.MaxManifestBytes - bare.Length);

        Assert.Equal(ManifestStatus.Valid, Read(bare + filler));
        Assert.Equal(ManifestStatus.TooLarge, Read(bare + filler + " "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"product\":\"Hindsound\"")]
    public void WhatIsNotAManifestIsRefused(string json)
    {
        Assert.Contains(Read(json), new[] { ManifestStatus.NotJson, ManifestStatus.WrongProduct });
    }

    [Fact]
    public void ACommentOrATrailingCommaIsNotJson()
    {
        Assert.Equal(ManifestStatus.NotJson, Read("/* x */" + Json()));
        Assert.Equal(ManifestStatus.NotJson, Read(Json().TrimEnd('}') + ",}"));
    }

    [Theory]
    [InlineData("\"hindsound\"")]
    [InlineData("\"HINDSOUND\"")]
    [InlineData("\"Hindsound \"")]
    [InlineData("\"Other\"")]
    [InlineData("null")]
    public void AnotherProductIsRefused(string product)
    {
        Assert.Equal(ManifestStatus.WrongProduct, Read(Json(product: product)));
    }

    [Theory]
    [InlineData("\"1.2\"")]
    [InlineData("\"1.2.3.4\"")]
    [InlineData("\"v1.2.3\"")]
    [InlineData("\"1.2.3-beta\"")]
    [InlineData("\"1.2.3+abc\"")]
    [InlineData("\"01.2.3\"")]
    [InlineData("\"1.2.x\"")]
    [InlineData("\" 1.2.3\"")]
    [InlineData("\"1.2.3 \"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void AnythingButXYZIsRefused(string version)
    {
        Assert.Equal(ManifestStatus.BadVersion, Read(Json(version: version)));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("\"abc\"")]
    [InlineData("\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")] // 63
    [InlineData("\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")] // 65
    [InlineData("\"gaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")] // 64, not hex
    public void AMalformedHashIsRefused(string sha256)
    {
        Assert.Equal(ManifestStatus.BadSha256, Read(Json(sha256: sha256)));
    }

    [Fact]
    public void AnUpperCaseHashIsAHash()
    {
        Assert.Equal(ManifestStatus.Valid, Read(Json(sha256: "\"" + new string('A', 64) + "\"")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1")]
    [InlineData("1048575")]
    [InlineData("104857601")]
    [InlineData("9007199254740993")]
    [InlineData("null")]
    public void ANullOrAbsurdSizeIsRefused(string size)
    {
        Assert.Equal(ManifestStatus.BadSize, Read(Json(size: size)));
    }

    [Theory]
    [InlineData("1048576")]
    [InlineData("104857600")]
    public void TheSizeLimitsThemselvesAreAllowed(string size)
    {
        Assert.Equal(ManifestStatus.Valid, Read(Json(size: size)));
    }

    [Theory]
    [InlineData("\"8200000\"")]
    [InlineData("8200000.5")]
    [InlineData("99999999999999999999999")]
    public void ASizeThatIsNotAnIntegerIsNotAManifest(string size)
    {
        Assert.Equal(ManifestStatus.NotJson, Read(Json(size: size)));
    }

    [Fact]
    public void AMissingPublishedDateIsRefused()
    {
        Assert.Equal(ManifestStatus.BadPublished, Read(Json(published: "null")));
    }

    [Fact]
    public void AGarbagePublishedDateIsRefused()
    {
        Assert.Equal(ManifestStatus.NotJson, Read(Json(published: "\"yesterday\"")));
    }

    [Theory]
    [InlineData("http://github.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // no TLS
    [InlineData("HTTPS://github.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // upper-case scheme
    [InlineData("https://GitHub.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // upper-case host
    [InlineData("https://github.com/Quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // upper-case owner
    [InlineData("https://github.com/quelquun667/Hindsound/releases/download/v1.2.3/a.exe")] // upper-case repository
    [InlineData("https://github.com/quelquun667/hindsound/releases/Download/v1.2.3/a.exe")] // upper-case path
    [InlineData("https://evil.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // other host
    [InlineData("https://evil.github.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // subdomain
    [InlineData("https://github.com.evil.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // host as a prefix
    [InlineData("https://github.com@evil.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // user info
    [InlineData("https://evil.com/https://github.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // prefix in the path
    [InlineData("https://github.com:443/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // port
    [InlineData("https://github.com:8443/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // port
    [InlineData("https://github.com/quelquun667/hindsound-evil/releases/download/v1.2.3/a.exe")] // repository as a prefix
    [InlineData("https://github.com/evil/hindsound/releases/download/v1.2.3/a.exe")] // other owner
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/../../../../evil/x/releases/download/v1.2.3/a.exe")] // climbs out
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/../../../../../evil/a.exe")] // climbs out later
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/..\\..\\a.exe")] // backslashes
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/%2e%2e/%2e%2e/a.exe")] // escaped dots
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/%2e%2e%2fa.exe")] // escaped separator
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/@evil.com/a.exe")] // @ after the prefix
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/a@evil.exe")] // @ in the name
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe?x=1")] // query
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe#x")] // fragment
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3//a.exe")] // empty segment
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/x/a.exe")] // one more segment
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe/b.exe")] // one more after a good name
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/a.exe")] // one segment less
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.2/a.exe")] // an older release: rollback
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/1.2.3/a.exe")] // tag without v
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/a.msi")] // not a Setup
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/.exe")] // no name
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/..exe")] // dots
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/a b.exe")] // space
    [InlineData(" https://github.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // leading space
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe ")] // trailing space
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/а.exe")] // Cyrillic letter
    [InlineData("https://github。com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")] // ideographic dot
    [InlineData("")]
    public void ATrappedUrlIsRefused(string url)
    {
        var escaped = url.Replace("\\", "\\\\");

        Assert.Equal(ManifestStatus.BadUrl, Read(Json(url: "\"" + escaped + "\"")));
        Assert.False(UpdateUrlPolicy.IsReleaseAsset(url, new UpdateVersion(1, 2, 3)));
    }

    [Fact]
    public void ANullUrlIsRefused()
    {
        Assert.Equal(ManifestStatus.BadUrl, Read(Json(url: "null")));
    }

    [Theory]
    [InlineData("https://github.com/quelquun667/hindsound/releases/latest/download/hindsound-update-win-x64.json")]
    [InlineData("https://github.com/quelquun667/hindsound/releases/download/v1.2.3/a.exe")]
    [InlineData("https://objects.githubusercontent.com/a/b?sig=c%2Fd")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1/2?sp=r&sig=x")]
    public void TheHopsToGitHubAreAllowed(string url)
    {
        Assert.True(UpdateUrlPolicy.IsAllowedHop(url, out var uri));
        Assert.NotNull(uri);
    }

    [Theory]
    [InlineData("http://github.com/a")]
    [InlineData("http://github.com:443/a")]
    [InlineData("https://github.com/a\\b")]
    [InlineData("HTTP://github.com/a")]
    [InlineData("ftp://github.com/a")]
    [InlineData("https://evil.com/a")]
    [InlineData("https://evil.github.com/a")]
    [InlineData("https://github.com.evil.com/a")]
    [InlineData("https://evilgithub.com/a")]
    [InlineData("https://githubusercontent.com/a")]
    [InlineData("https://evil.githubusercontent.com/a")]
    [InlineData("https://github.com@evil.com/a")]
    [InlineData("https://evil.com@github.com/a")]
    [InlineData("https://user:password@github.com/a")]
    [InlineData("https://github.com:8443/a")]
    [InlineData("https://github.com./a")]
    [InlineData("https://github.com\\@evil.com/a")]
    [InlineData("https://127.0.0.1/a")]
    [InlineData("github.com/a")]
    [InlineData("/a")]
    [InlineData("")]
    [InlineData(null)]
    public void TheOtherHopsAreBlocked(string? url)
    {
        Assert.False(UpdateUrlPolicy.IsAllowedHop(url, out var uri));
        Assert.Null(uri);
    }

    [Theory]
    [InlineData("0.0.9", 0, 0, 9)]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("999999999.999999999.999999999", 999999999, 999999999, 999999999)]
    public void VersionsAreRead(string text, int major, int minor, int patch)
    {
        Assert.True(UpdateVersion.TryParse(text, out var version));
        Assert.Equal(new UpdateVersion(major, minor, patch), version);
        Assert.Equal(text, version.ToString());
    }

    [Theory]
    [InlineData("1000000000.0.0")]
    [InlineData("1..3")]
    [InlineData(".2.3")]
    [InlineData("1.2.")]
    [InlineData("-1.2.3")]
    [InlineData("+1.2.3")]
    [InlineData("١.2.3")] // Arabic digit
    [InlineData(null)]
    public void OtherVersionsAreNot(string? text)
    {
        Assert.False(UpdateVersion.TryParse(text, out _));
    }

    [Fact]
    public void VersionsCompareByNumberNotByText()
    {
        Assert.True(new UpdateVersion(0, 0, 10) > new UpdateVersion(0, 0, 9));
        Assert.True(new UpdateVersion(0, 2, 0) > new UpdateVersion(0, 1, 99));
        Assert.True(new UpdateVersion(2, 0, 0) > new UpdateVersion(1, 99, 99));
        Assert.True(new UpdateVersion(1, 2, 3) >= new UpdateVersion(1, 2, 3));
        Assert.True(new UpdateVersion(1, 2, 3) <= new UpdateVersion(1, 2, 3));
        Assert.True(new UpdateVersion(1, 2, 2) < new UpdateVersion(1, 2, 3));
    }
}
