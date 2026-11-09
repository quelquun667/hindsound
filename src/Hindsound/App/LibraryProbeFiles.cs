using System.Buffers.Binary;
using Hindsound.Library;
using Hindsound.Settings;

namespace Hindsound.App;

/// <summary>
/// The clips of <c>--settings-probe</c>: small files, in a folder made for the run and removed after it, so a measure of the
/// Library page never reads or adds to a real folder of clips.
/// </summary>
internal static class LibraryProbeFiles
{
    public const int Count = ClipScanner.MaxLines;

    private const string Pattern = "probe-*.wav";
    private const int SampleRate = 8000, DataBytes = 800, HeaderBytes = 44;

    /// <summary>
    /// The folder of a run, next to its settings file. Null where it would not be the probe's alone: no settings file given,
    /// the real settings of the app, or a folder already there.
    /// </summary>
    public static string? FolderFor(string? settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            return null;
        }

        var full = Path.GetFullPath(settingsPath);
        var real = Path.GetDirectoryName(SettingsStore.DefaultPath);
        if (string.Equals(Path.GetDirectoryName(full), real, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var folder = Path.ChangeExtension(full, ".library-probe");
        return Directory.Exists(folder) ? null : folder;
    }

    /// <summary>Fills the folder with clips of 0.1 s, real enough for their length to be read, one minute apart: the higher the number, the newer.</summary>
    public static void Create(string folder, int count = Count)
    {
        Directory.CreateDirectory(folder);
        var wave = Wave();
        var oldest = DateTime.UtcNow.AddMinutes(-count);
        for (var i = 0; i < count; i++)
        {
            var path = PathOf(folder, i);
            File.WriteAllBytes(path, wave);
            File.SetLastWriteTimeUtc(path, oldest.AddMinutes(i));
        }
    }

    public static string PathOf(string folder, int number) => Path.Combine(folder, $"probe-{number:D4}.wav");

    /// <summary>Removes what <see cref="Create"/> made, and the folder if nothing else is in it.</summary>
    public static void Remove(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, Pattern))
        {
            File.Delete(file);
        }

        if (!Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }
    }

    private static byte[] Wave()
    {
        var bytes = new byte[HeaderBytes + DataBytes];
        var span = bytes.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 8);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], DataBytes);
        return bytes;
    }
}
