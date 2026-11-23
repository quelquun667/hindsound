using Hindsound.Settings;

namespace Hindsound.Ui;

/// <summary>
/// Which language the interface takes, from the setting and the languages the user prefers, and what each system says those
/// are. Pure: the culture of the process is never read, as the Linux and Mac builds run with the invariant one.
/// </summary>
internal static class UiLanguages
{
    /// <summary>
    /// The language of the interface. <c>en</c> and <c>fr</c> choose it; anything else follows the system: the first preferred
    /// language that is French or English decides, and none of them gives English.
    /// </summary>
    /// <param name="setting">The value of the settings file.</param>
    /// <param name="preferred">The user's preferred languages, first first, as BCP-47 tags: <c>fr-FR</c>, <c>en-US</c>, <c>fr</c>.</param>
    public static UiLanguage Resolve(string? setting, IReadOnlyList<string> preferred)
    {
        switch (setting)
        {
            case GeneralSettings.LanguageEnglish:
                return UiLanguage.English;
            case GeneralSettings.LanguageFrench:
                return UiLanguage.French;
        }

        return FromSystem(preferred) ?? UiLanguage.English;
    }

    /// <summary>The first of the preferred languages the app has, or null when it has none of them.</summary>
    public static UiLanguage? FromSystem(IReadOnlyList<string> preferred)
    {
        foreach (var tag in preferred)
        {
            switch (Primary(tag))
            {
                case "fr":
                    return UiLanguage.French;
                case "en":
                    return UiLanguage.English;
            }
        }

        return null;
    }

    /// <summary>The language of a tag, in lower case: <c>fr</c> for <c>fr-FR</c>, <c>FR_ca</c> or <c>fr</c>.</summary>
    private static string Primary(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var trimmed = tag.Trim();
        var end = trimmed.IndexOfAny(['-', '_', '.', '@']);
        return (end < 0 ? trimmed : trimmed[..end]).ToLowerInvariant();
    }

    /// <summary>
    /// A POSIX locale as a BCP-47 tag: <c>fr_FR.UTF-8</c> and <c>fr_FR@euro</c> give <c>fr-FR</c>, <c>fr</c> gives <c>fr</c>.
    /// <c>C</c> and <c>POSIX</c>, with or without a code set, name no language: null, as for an empty value.
    /// </summary>
    public static string? FromPosixLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        var value = locale.Trim();
        var cut = value.IndexOfAny(['.', '@']);
        if (cut >= 0)
        {
            value = value[..cut];
        }

        if (value.Length == 0 || value is "C" or "POSIX")
        {
            return null;
        }

        return value.Replace('_', '-');
    }

    /// <summary>
    /// The preferred languages of a Linux session, as gettext reads them: the locale of the messages is the first set of
    /// <c>LC_ALL</c>, <c>LC_MESSAGES</c> and <c>LANG</c>; <c>LANGUAGE</c>, a list split by colons, comes before it. A locale of
    /// <c>C</c> or <c>POSIX</c> asks for no language at all, and gettext then ignores <c>LANGUAGE</c> too: nothing. So does a session
    /// where none of the three is set, which is the <c>C</c> locale as well.
    /// </summary>
    /// <param name="variable">Reads one variable of the environment; null when it is not set.</param>
    public static IReadOnlyList<string> FromLinuxEnvironment(Func<string, string?> variable)
    {
        var locale = new[] { "LC_ALL", "LC_MESSAGES", "LANG" }.Select(variable).FirstOrDefault(value => !string.IsNullOrEmpty(value));
        var messages = FromPosixLocale(locale);
        if (locale is null || messages is null)
        {
            return [];
        }

        var tags = new List<string>();
        foreach (var entry in (variable("LANGUAGE") ?? string.Empty).Split(':'))
        {
            if (FromPosixLocale(entry) is { } tag && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tag);
            }
        }

        if (messages is not null && !tags.Contains(messages, StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(messages);
        }

        return tags;
    }

    /// <summary>
    /// The list Windows' <c>GetUserPreferredUILanguages</c> writes with <c>MUI_LANGUAGE_NAME</c>: names each ended by a null,
    /// and the list ended by one more. Empty names are dropped.
    /// </summary>
    public static IReadOnlyList<string> FromMultiString(ReadOnlySpan<char> buffer)
    {
        var tags = new List<string>();
        foreach (var name in buffer.ToString().Split('\0'))
        {
            if (name.Trim() is { Length: > 0 } tag)
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    /// <summary>A list a system gave, cleaned: no empty tag, no tag twice, the order kept.</summary>
    public static IReadOnlyList<string> Clean(IEnumerable<string?> tags) =>
        tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Each language named in its own language, as the list of the General page shows it.</summary>
    public static string OwnName(UiLanguage language) => language == UiLanguage.French ? "Français" : "English";

    /// <summary>The line of the journal, in English as the whole journal is.</summary>
    public static string JournalLine(string? setting, IReadOnlyList<string> preferred, UiLanguage chosen) =>
        $"language: setting {setting ?? GeneralSettings.LanguageSystem}, system gave {(preferred.Count == 0 ? "nothing" : string.Join(' ', preferred))}, chosen {chosen}";
}
