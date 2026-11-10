using Hindsound.Export;
using Hindsound.Ui.Window;

namespace Hindsound.Tests.Export;

/// <summary>The characters a file name cannot hold are the same on every system: a clip may be written to a drive that Windows reads.</summary>
public class FileNameTemplateInvalidCharsTests
{
    [Fact]
    public void The_forbidden_characters_are_those_of_Windows()
    {
        char[] windows = [.. Enumerable.Range(0, 32).Select(code => (char)code), '"', '<', '>', '|', ':', '*', '?', '\\', '/'];

        Assert.Equal(windows.Order(), FileNameTemplate.InvalidChars.Order());
    }

    [Fact]
    public void On_Windows_they_are_the_ones_the_runtime_reports()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(Path.GetInvalidFileNameChars().Order(), FileNameTemplate.InvalidChars.Order());
    }

    [Theory]
    [InlineData("Re: Zero", "Re Zero")]
    [InlineData("a*b?c\"d<e>f|g", "abcdefg")]
    [InlineData("back\\slash", "backslash")]
    public void A_name_keeps_none_of_them(string app, string expected)
    {
        Assert.Equal(expected, FileNameTemplate.Sanitize(app));
    }

    [Theory]
    [InlineData("{app}\\x")]
    [InlineData("{app}:x")]
    [InlineData("{app}*")]
    public void A_template_that_holds_one_is_refused(string template)
    {
        Assert.False(SettingsEditor.CheckTemplate(template).Ok);
    }
}
