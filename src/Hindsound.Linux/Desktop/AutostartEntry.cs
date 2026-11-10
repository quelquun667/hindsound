using System.Text;
using Hindsound.Linux.DBus;
using Hindsound.Settings;

namespace Hindsound.Linux.Desktop;

/// <summary>
/// Starting with the session, through a desktop entry in the autostart folder of the XDG specification:
/// $XDG_CONFIG_HOME/autostart, ~/.config/autostart by default. The program is the one <see cref="App.PlatformDefaults.ResolveLaunchPath"/>
/// gives, the AppImage itself rather than its mount. A failure to write throws, which the app reports as refused.
/// </summary>
internal sealed class AutostartEntry : IRunKey
{
    public const string FileName = DBusNames.AppId + ".desktop";

    private readonly string _folder;

    public AutostartEntry(Action<string> log)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), "autostart"))
    {
    }

    /// <param name="folder">The autostart folder: for a test, one of its own.</param>
    internal AutostartEntry(string folder) => _folder = folder;

    public string FilePath => Path.Combine(_folder, FileName);

    public RunEntry State(string? program)
    {
        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return RunEntry.None;
        }

        return AutostartFile.Read(text) is { } started
            ? program is not null && started == program ? RunEntry.ThisProgram : RunEntry.OtherProgram
            : RunEntry.None;
    }

    public void Enable(string program)
    {
        var text = AutostartFile.Text(program);
        Directory.CreateDirectory(_folder);

        // Whole or not at all: the session never reads half an entry.
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, FilePath, overwrite: true);
    }

    public void Disable()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing to remove.
        }
    }
}

/// <summary>The text of the autostart entry, and the program an entry starts. The rules are those of the Desktop Entry specification.</summary>
internal static class AutostartFile
{
    private const string Group = "[Desktop Entry]";

    /// <summary>The entry that starts <paramref name="program"/>. A path with a control character cannot be written in one: that throws.</summary>
    public static string Text(string program) =>
        string.Join('\n',
            Group,
            "Type=Application",
            "Name=Hindsound",
            "Exec=" + ExecOf(program),
            "Icon=" + DBusNames.AppId,
            "Terminal=false",
            "NoDisplay=true",
            "X-GNOME-Autostart-enabled=true",
            "");

    /// <summary>
    /// The Exec value: the path quoted, with '"', '`', '$' and '\' escaped by a backslash inside the quotes; then '%' doubled,
    /// and every backslash doubled again by the escape rule of a string value, which applies first when the file is read.
    /// </summary>
    public static string ExecOf(string program)
    {
        if (program.Any(char.IsControl))
        {
            throw new IOException("the path of the program cannot be written in a desktop entry");
        }

        var quoted = new StringBuilder("\"");
        foreach (var c in program)
        {
            if (c is '"' or '`' or '$' or '\\')
            {
                quoted.Append('\\');
            }

            quoted.Append(c == '%' ? "%%" : c.ToString());
        }

        quoted.Append('"');
        return quoted.ToString().Replace("\\", "\\\\", StringComparison.Ordinal);
    }

    /// <summary>
    /// The program an entry starts; null when it starts nothing, is switched off (Hidden=true, or the GNOME key at false),
    /// or cannot be read.
    /// </summary>
    public static string? Read(string text)
    {
        var inGroup = false;
        string? exec = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('['))
            {
                inGroup = line == Group;
                continue;
            }

            var equals = line.IndexOf('=');
            if (!inGroup || equals < 1)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            switch (key)
            {
                case "Hidden" when value == "true":
                case "X-GNOME-Autostart-enabled" when value == "false":
                    return null;

                case "Exec":
                    exec = value;
                    break;
            }
        }

        return exec is null ? null : ProgramOf(exec);
    }

    /// <summary>The first argument of an Exec value: the program.</summary>
    public static string? ProgramOf(string exec)
    {
        var value = Unescape(exec);
        if (value.Length == 0)
        {
            return null;
        }

        var program = new StringBuilder();
        if (value[0] != '"')
        {
            var end = value.IndexOf(' ');
            program.Append(end < 0 ? value : value[..end]);
        }
        else
        {
            var closed = false;
            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    program.Append(value[++i]);
                }
                else if (c == '"')
                {
                    closed = true;
                    break;
                }
                else
                {
                    program.Append(c);
                }
            }

            if (!closed)
            {
                return null;
            }
        }

        return program.Replace("%%", "%").ToString() is { Length: > 0 } path ? path : null;
    }

    // The escapes of a string value: \s, \n, \t, \r and \\.
    private static string Unescape(string value)
    {
        var plain = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length)
            {
                plain.Append(value[i]);
                continue;
            }

            var next = value[++i];
            plain.Append(next switch
            {
                's' => " ",
                'n' => "\n",
                't' => "\t",
                'r' => "\r",
                '\\' => "\\",
                _ => "\\" + next,
            });
        }

        return plain.ToString();
    }
}
