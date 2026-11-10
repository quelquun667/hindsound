using System.IO;

namespace Hindsound.Settings;

/// <summary>The rules shared by the files the user keeps: a write never leaves half a file, a kept copy never replaces another.</summary>
internal static class AtomicFile
{
    /// <summary>
    /// Writes the text atomically: temporary file in the same folder, then replace. A failure throws, with the file as it
    /// was and no temporary file left behind.
    /// </summary>
    public static void Write(string path, string text)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(folder);

        var temp = Path.Combine(folder, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, text);
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>Never an earlier .bak: settings.json.bak, then settings.json.1.bak, .2.bak, ...</summary>
    public static string FreeBackupPath(string path)
    {
        var target = path + ".bak";
        for (var i = 1; File.Exists(target); i++)
        {
            target = $"{path}.{i}.bak";
        }

        return target;
    }
}
