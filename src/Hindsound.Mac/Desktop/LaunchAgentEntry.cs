using System.Text;
using System.Xml;
using System.Xml.Linq;
using Hindsound.Settings;

namespace Hindsound.Mac.Desktop;

/// <summary>
/// Opening at login, through a launch agent of the user: ~/Library/LaunchAgents/io.github.quelquun667.hindsound.plist, which
/// launchd reads at each login and, with <c>RunAtLoad</c>, starts the program of the bundle in the user's graphic session.
/// Chosen over <c>SMAppService.mainApp</c>: for an app signed ad hoc that service answers <c>notFound</c> (seen by the probe
/// of the runner, macOS 26.7.2), its registration waits for an approval in System Settings, and it never says which copy
/// of the app it starts, where the settings must know whether the entry starts this one. A failure to write throws, which
/// the app reports as refused.
/// </summary>
internal sealed class LaunchAgentEntry : IRunKey
{
    /// <summary>The label of the job, the identifier of the bundle.</summary>
    public const string Label = "io.github.quelquun667.hindsound";

    public const string FileName = Label + ".plist";

    private const string NoFolder = "No home folder, so no LaunchAgents folder.";

    private readonly string? _folder;

    public LaunchAgentEntry()
        : this(FolderIn(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
    {
    }

    /// <param name="folder">The LaunchAgents folder: for a test, one of its own. Null when the home folder is unknown.</param>
    internal LaunchAgentEntry(string? folder) => _folder = folder;

    /// <summary>The file of the entry. Throws when there is no folder for it.</summary>
    public string FilePath => Path.Combine(_folder ?? throw new IOException(NoFolder), FileName);

    /// <summary>~/Library/LaunchAgents of a home folder; none for a relative or empty one.</summary>
    internal static string? FolderIn(string? home) =>
        home is { Length: > 1 } && home[0] == '/' ? home.TrimEnd('/') + "/Library/LaunchAgents" : null;

    public RunEntry State(string? program)
    {
        if (_folder is null)
        {
            return RunEntry.None;
        }

        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return RunEntry.None;
        }

        return LaunchAgentFile.Read(text) is { } started
            ? program is not null && started == program ? RunEntry.ThisProgram : RunEntry.OtherProgram
            : RunEntry.None;
    }

    public void Enable(string program)
    {
        var text = LaunchAgentFile.Text(program);
        Directory.CreateDirectory(_folder ?? throw new IOException(NoFolder));

        // Whole or not at all: launchd never reads half an entry.
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, FilePath, overwrite: true);
    }

    public void Disable()
    {
        if (_folder is null)
        {
            return;
        }

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

/// <summary>The property list of the launch agent, and the program an entry starts.</summary>
internal static class LaunchAgentFile
{
    /// <summary>
    /// The entry that starts <paramref name="program"/> at login, in the graphic session only (<c>Aqua</c>), once: no
    /// <c>KeepAlive</c>, so Quit in the menu stays quit. A path with a control character cannot be written: that throws.
    /// </summary>
    public static string Text(string program)
    {
        if (program.Any(char.IsControl))
        {
            throw new IOException("the path of the program cannot be written in a launch agent");
        }

        var plist = new XElement("plist", new XAttribute("version", "1.0"),
            new XElement("dict",
                new XElement("key", "Label"), new XElement("string", LaunchAgentEntry.Label),
                new XElement("key", "ProgramArguments"), new XElement("array", new XElement("string", program)),
                new XElement("key", "RunAtLoad"), new XElement("true"),
                new XElement("key", "LimitLoadToSessionType"), new XElement("string", "Aqua"),
                new XElement("key", "ProcessType"), new XElement("string", "Interactive")));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + plist.ToString(SaveOptions.None).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>
    /// The program an entry starts: the first of its ProgramArguments, else its Program. Null when it starts nothing, is
    /// switched off (<c>Disabled</c> true), is another job, or cannot be read.
    /// </summary>
    public static string? Read(string text)
    {
        XElement? dict;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(text), settings);
            dict = XDocument.Load(reader).Root?.Element("dict");
        }
        catch (XmlException)
        {
            return null;
        }

        if (dict is null)
        {
            return null;
        }

        var values = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var key in dict.Elements("key"))
        {
            if (key.ElementsAfterSelf().FirstOrDefault() is { } value)
            {
                values.TryAdd(key.Value, value);
            }
        }

        if (!values.TryGetValue("Label", out var label) || label.Value != LaunchAgentEntry.Label
            || values.TryGetValue("Disabled", out var disabled) && disabled.Name == "true")
        {
            return null;
        }

        var program = values.TryGetValue("ProgramArguments", out var arguments) && arguments.Name == "array"
            ? arguments.Elements("string").FirstOrDefault()?.Value
            : values.TryGetValue("Program", out var single) && single.Name == "string" ? single.Value : null;
        return string.IsNullOrEmpty(program) ? null : program;
    }
}
