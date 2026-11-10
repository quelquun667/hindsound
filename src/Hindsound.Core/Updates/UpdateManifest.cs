using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hindsound.Updates;

/// <summary>A manifest that passed every check. Size and SHA-256 only protect against a damaged download, not a stolen account.</summary>
internal sealed record UpdateManifest(UpdateVersion Version, string Url, string Sha256, long Size, DateTimeOffset Published);

internal enum ManifestStatus
{
    Valid,
    TooLarge,
    NotJson,
    WrongProduct,
    BadVersion,
    BadUrl,
    BadSha256,
    BadSize,
    BadPublished,
}

/// <summary>The file as written: any field may be missing or wrong until <see cref="UpdateManifestReader"/> has seen it.</summary>
internal sealed class UpdateManifestFile
{
    public string? Product { get; set; }

    public string? Version { get; set; }

    public string? Url { get; set; }

    public string? Sha256 { get; set; }

    public long? Size { get; set; }

    public DateTimeOffset? Published { get; set; }
}

// Generated at build time: the published exe is trimmed and has no reflection-based serializer.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UpdateManifestFile))]
internal sealed partial class UpdateManifestJson : JsonSerializerContext;

/// <summary>Reads a manifest (hindsound-update-win-x64.json, hindsound-update-linux-x64.json), and refuses everything that is not exact.</summary>
internal static class UpdateManifestReader
{
    public const int MaxManifestBytes = 16 * 1024;

    // The Setup measures 8.2 MB (06/11/2026). Below 1 MB it cannot hold the 18 MB exe, above 100 MB something is wrong.
    public const long MinSetupBytes = 1024 * 1024;
    public const long MaxSetupBytes = 100 * 1024 * 1024;

    private const string Product = "Hindsound";
    private const int Sha256HexLength = 64;

    public static ManifestStatus Read(ReadOnlySpan<byte> json, out UpdateManifest? manifest) => Read(json, UpdateDelivery.Setup, out manifest);

    /// <summary>
    /// For <see cref="UpdateDelivery.AnnounceOnly"/>, the url must be exactly the page of the version's release, and the
    /// hash and size are not read: nothing is downloaded. The manifest then carries an empty hash and a size of zero.
    /// </summary>
    public static ManifestStatus Read(ReadOnlySpan<byte> json, UpdateDelivery delivery, out UpdateManifest? manifest)
    {
        manifest = null;
        if (json.Length > MaxManifestBytes)
        {
            return ManifestStatus.TooLarge;
        }

        UpdateManifestFile? file;
        try
        {
            file = JsonSerializer.Deserialize(json, UpdateManifestJson.Default.UpdateManifestFile);
        }
        catch (JsonException)
        {
            return ManifestStatus.NotJson;
        }

        if (file is null)
        {
            return ManifestStatus.NotJson;
        }

        if (file.Product != Product)
        {
            return ManifestStatus.WrongProduct;
        }

        if (!UpdateVersion.TryParse(file.Version, out var version))
        {
            return ManifestStatus.BadVersion;
        }

        if (delivery == UpdateDelivery.AnnounceOnly)
        {
            if (!UpdateUrlPolicy.IsReleasePage(file.Url, version))
            {
                return ManifestStatus.BadUrl;
            }

            if (file.Published is null)
            {
                return ManifestStatus.BadPublished;
            }

            manifest = new UpdateManifest(version, file.Url!, string.Empty, 0, file.Published.Value);
            return ManifestStatus.Valid;
        }

        if (!UpdateUrlPolicy.IsReleaseAsset(file.Url, version))
        {
            return ManifestStatus.BadUrl;
        }

        if (!IsSha256(file.Sha256))
        {
            return ManifestStatus.BadSha256;
        }

        if (file.Size is not (>= MinSetupBytes and <= MaxSetupBytes))
        {
            return ManifestStatus.BadSize;
        }

        if (file.Published is null)
        {
            return ManifestStatus.BadPublished;
        }

        manifest = new UpdateManifest(version, file.Url!, file.Sha256!, file.Size.Value, file.Published.Value);
        return ManifestStatus.Valid;
    }

    private static bool IsSha256(string? text)
    {
        if (text is null || text.Length != Sha256HexLength)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
