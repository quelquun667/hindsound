using System.Text.RegularExpressions;
using Hindsound.App;

namespace Hindsound.Tests.App;

/// <summary>
/// The languages of the Windows session, read on the machine the tests run on. A fake cannot say that the calls and the
/// fallback work: each is called here, and each has to give at least one tag that looks like a language.
/// </summary>
public sealed partial class WindowsLanguagesTests
{
    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex Tag();

    private static void AssertWellFormed(IReadOnlyList<string> tags, string from)
    {
        Assert.NotEmpty(tags);
        Assert.All(tags, tag => Assert.True(Tag().IsMatch(tag), $"{from} gave \"{tag}\", which is not a language tag"));
    }

    [Fact]
    public void The_preferred_list_of_the_session_has_a_well_formed_tag()
    {
        AssertWellFormed(WindowsLanguages.FromPreferred(), "GetUserPreferredUILanguages");
    }

    [Fact]
    public void The_fallback_to_the_default_language_of_the_interface_has_a_well_formed_tag()
    {
        AssertWellFormed(WindowsLanguages.FromDefault(), "GetUserDefaultUILanguage");
    }

    [Fact]
    public void The_list_the_app_reads_is_never_empty_on_a_real_session()
    {
        AssertWellFormed(WindowsLanguages.Preferred(), "Preferred");
    }
}
