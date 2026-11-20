using System.Text;
using Hindsound.Linux.Desktop;
using Hindsound.Settings;

namespace Hindsound.Linux.Tests.Desktop;

/// <summary>
/// The autostart entry names its program in TryExec too: after an apt remove, or an AppImage deleted, the session skips the
/// entry instead of starting a program that is gone at each login. TryExec is a string value, not quoted as Exec is.
/// </summary>
public sealed class AutostartTryExecTests : IDisposable
{
    /// <summary>The entry as 0.6.0 to 0.8.0 wrote it, with no TryExec.</summary>
    private const string WrittenBefore =
        "[Desktop Entry]\nType=Application\nName=Hindsound\nExec=\"/opt/hindsound\"\nIcon=io.github.quelquun667.Hindsound\nTerminal=false\nNoDisplay=true\nX-GNOME-Autostart-enabled=true\n";

    private readonly string _folder = Directory.CreateTempSubdirectory("hindsound-tryexec-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Theory]
    [InlineData("/usr/lib/hindsound/hindsound", "/usr/lib/hindsound/hindsound")]
    [InlineData("/home/u/a b\\c/hindsound", "/home/u/a b\\\\c/hindsound")]
    [InlineData("/home/u/Hindsound ", "/home/u/Hindsound\\s")]
    [InlineData("/x/$y`z\"w%u", "/x/$y`z\"w%u")]
    public void The_entry_names_the_program_in_try_exec_as_a_string_value(string program, string value)
    {
        var lines = AutostartFile.Text(program).Split('\n');

        Assert.Equal("TryExec=" + value, lines[4]);
        Assert.StartsWith("Exec=", lines[3], StringComparison.Ordinal);
        Assert.Single(lines, line => line.StartsWith("TryExec=", StringComparison.Ordinal));

        // Read back as a reader of desktop entries does: the value trimmed, then its escapes undone.
        Assert.Equal(program, Unescape(lines[4]["TryExec=".Length..].Trim()));
        Assert.Equal(program, AutostartFile.Read(AutostartFile.Text(program)));
    }

    [Fact]
    public void A_path_with_a_control_character_is_no_string_value()
    {
        Assert.Throws<IOException>(() => AutostartFile.StringOf("/a\tb"));
    }

    [Fact]
    public void An_entry_written_before_try_exec_reads_as_it_did()
    {
        Assert.Equal("/opt/hindsound", AutostartFile.Read(WrittenBefore));

        var entry = new AutostartEntry(_folder);
        File.WriteAllText(entry.FilePath, WrittenBefore);
        Assert.Equal(RunEntry.ThisProgram, entry.State("/opt/hindsound"));
        Assert.Equal(RunEntry.OtherProgram, entry.State("/usr/lib/hindsound/hindsound"));

        entry.Enable("/opt/hindsound");
        Assert.Equal(RunEntry.ThisProgram, entry.State("/opt/hindsound"));
        Assert.Equal(WrittenBefore.Replace("\"\n", "\"\nTryExec=/opt/hindsound\n", StringComparison.Ordinal), File.ReadAllText(entry.FilePath));
    }

    private static string Unescape(string value)
    {
        var plain = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                plain.Append(value[++i] switch { 's' => ' ', '\\' => '\\', var other => other });
            }
            else
            {
                plain.Append(value[i]);
            }
        }

        return plain.ToString();
    }
}
