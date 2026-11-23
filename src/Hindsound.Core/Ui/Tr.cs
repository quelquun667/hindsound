using System.Globalization;

namespace Hindsound;

/// <summary>The languages the interface is drawn in.</summary>
public enum UiLanguage
{
    English,
    French,
}

/// <summary>The size units the app shows, in the words of each language: KB, MB, GB in English; Ko, Mo, Go in French.</summary>
public enum SizeUnit
{
    KB,
    MB,
    GB,
}

/// <summary>
/// The language of everything the app shows, and the texts in both languages. A text is written once, where it is used, with
/// its two versions side by side, so that a review reads both at once and nothing needs a resource file or a satellite
/// assembly, which the trimmed builds would not carry:
/// <code>
/// Tr.Of("Play", "Lire")                                   // a plain text
/// Tr.Format($"Clip saved ({d})", $"Clip enregistré ({d})") // a text with values, formatted with the invariant culture
/// Tr.Number(2000)                                         // "2,000" or "2 000"
/// Tr.Size(1.4, "0.0", SizeUnit.MB)                        // "1.4 MB" or "1,4 Mo"
/// Tr.Plural(n, "{0} file", "{0} files", "{0} fichier", "{0} fichiers")
/// </code>
/// The rules, each learned from a text that went wrong:
/// <list type="bullet">
/// <item>Never <c>$""</c> inside <see cref="Of"/>: on Windows it formats with the culture of the PC, and gives <c>1,5 s</c> in
/// English on a French one. Values go through <see cref="Format"/>, <see cref="Number(long)"/>, <see cref="Size"/> or
/// <see cref="Plural"/>. A test refuses it in every source file.</item>
/// <item>A displayed <c>const string</c> becomes <c>static string X =&gt; Tr.Of(..)</c>, read again at each use, and never
/// <c>static readonly</c>, which would keep the language of the first use. A <c>switch</c> case or an attribute cannot take a
/// property: compare to the value the text stands for.</item>
/// <item>Compare states, never displayed words. Ids, dictionary keys, <c>DropItem</c> values and the values of the settings
/// file stay English: they are read by the app, not by the user.</item>
/// <item>Never <c>Enum.ToString()</c> on screen: it is the name of the member, in English. Write a <c>switch</c> to a text.</item>
/// <item>One whole sentence per language. Pieces glued together (<c>"Saved " + n + " clips"</c>) cannot follow the order of the
/// words or the agreement of another language. A name that goes inside a sentence is a value, not a piece.</item>
/// <item>Plurals go through <see cref="Plural"/>: French says "0 fichier" where English says "0 files".</item>
/// <item>The journal, the clip file names and the settings file stay in English, whatever the language: they are read by people
/// helping with a problem, and by the app itself, so they never take a text from here. A line of the journal that needs the
/// words of the screen makes them in <c>using (Tr.Use(UiLanguage.English))</c>.</item>
/// <item>A text kept in a living object (a notification, a label of a built control) has the language it was made in: keep the
/// facts and make the words when they are shown, or make it again in <c>ApplyLanguage</c> or <c>LanguageChanged</c>.</item>
/// <item>French typography: U+00A0 before <c>: ; ! ? %</c> and before a unit; French quotes « » with U+00A0 inside, as
/// «&#160;ceci&#160;». <see cref="Number(long)"/> and <see cref="Size"/> already do it for numbers and units.</item>
/// <item>A test of translated text opens <c>using var scope = Tr.Use(..)</c>, so that tests beside it never see its language.</item>
/// </list>
/// <para>
/// English until something sets it, so that every test reads English on a PC of any language. The app's entry point calls
/// <see cref="ForProcess"/> once, through <c>AppStartup.Begin</c>; from then on <see cref="Set"/> changes the language of the
/// whole process. Without it, <see cref="Set"/> only changes the scope a test or the dump opened with <see cref="Use"/>, and
/// changes nothing anywhere else, so tests that run side by side never see each other's language.
/// </para>
/// </summary>
internal static class Tr
{
    // A no-break space: the plain one, which Segoe UI and the fonts of the Unix builds all draw.
    private const char NoBreakSpace = ' ';

    private static readonly AsyncLocal<Scope?> Scoped = new();
    private static int _process;
    private static int _processWide;

    /// <summary>The language now: the scope's, when this flow opened one; otherwise the process's.</summary>
    public static UiLanguage Language => Scoped.Value is { } scope ? scope.Language : (UiLanguage)Volatile.Read(ref _process);

    public static bool French => Language == UiLanguage.French;

    /// <summary>The entry point of the app, once, before anything is shown: <see cref="Set"/> then changes the whole process.</summary>
    public static void ForProcess() => Volatile.Write(ref _processWide, 1);

    /// <summary>Changes the language. Returns whether it changed: what shows texts must then be built again.</summary>
    public static bool Set(UiLanguage language)
    {
        if (Scoped.Value is { } scope)
        {
            return scope.Swap(language) != language;
        }

        if (Volatile.Read(ref _processWide) == 0)
        {
            return false;
        }

        return (UiLanguage)Interlocked.Exchange(ref _process, (int)language) != language;
    }

    /// <summary>A language for this flow alone, until disposed: a test, the dump drawing its pages in one language, or a line of the journal.</summary>
    public static IDisposable Use(UiLanguage language)
    {
        var scope = new Scope(language, Scoped.Value);
        Scoped.Value = scope;
        return scope;
    }

    /// <summary>The text in the current language.</summary>
    public static string Of(string english, string french) => French ? french : english;

    /// <summary>A text with values in the current language. Numbers go in with the invariant culture: use <see cref="Number(long)"/> for a grouped one.</summary>
    public static string Format(FormattableString english, FormattableString french) =>
        (French ? french : english).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A text that agrees with a count, each version written with <c>{0}</c> where the number goes, written by
    /// <see cref="Number(long)"/>. English has the singular for 1 only; French has it for 0 and 1:
    /// <c>Tr.Plural(n, "{0} file", "{0} files", "{0} fichier", "{0} fichiers")</c> gives "0 files", "1 file", "2 files" in English
    /// and "0 fichier", "1 fichier", "2 fichiers" in French.
    /// </summary>
    public static string Plural(long count, string englishOne, string englishMany, string frenchOne, string frenchMany)
    {
        var text = French
            ? (count is 0 or 1 ? frenchOne : frenchMany)
            : (count == 1 ? englishOne : englishMany);
        return text.Replace("{0}", Number(count), StringComparison.Ordinal);
    }

    /// <summary>
    /// The name of an application after "de" in French, which becomes "d'" before a vowel: "de Discord", "d'OBS", "d'Opéra". A
    /// leading "h" is left alone, as the "h" of a name from another language is rarely the mute one of French ("de Hindsound").
    /// </summary>
    public static string FrenchOf(string name)
    {
        var first = name.TrimStart() is { Length: > 0 } trimmed ? trimmed[0] : ' ';
        return "aeiouàâäéèêëîïôöùûüœæ".Contains(char.ToLowerInvariant(first)) ? "d'" + name : "de " + name;
    }

    /// <summary>A whole number with its thousands separator: "2,000" in English, "2 000" in French.</summary>
    public static string Number(long value) => Number((double)value, "#,0");

    /// <summary>
    /// A number in a .NET format of the invariant culture ("0.0", "#,0"), with the separators of the current language: a comma
    /// between groups and a point before the decimals in English, a no-break space and a comma in French.
    /// </summary>
    public static string Number(double value, string format)
    {
        var text = value.ToString(format, CultureInfo.InvariantCulture);
        if (!French)
        {
            return text;
        }

        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                ',' => NoBreakSpace,
                '.' => ',',
                var other => other,
            };
        }

        return new string(chars);
    }

    /// <summary>The unit alone: "MB" or "Mo".</summary>
    public static string Unit(SizeUnit unit) => unit switch
    {
        SizeUnit.KB => Of("KB", "Ko"),
        SizeUnit.MB => Of("MB", "Mo"),
        _ => Of("GB", "Go"),
    };

    /// <summary>A size with its unit, a no-break space between so that a wrapped line never parts them: "1.4 MB" or "1,4 Mo".</summary>
    public static string Size(double value, string format, SizeUnit unit) => Number(value, format) + NoBreakSpace + Unit(unit);

    private sealed class Scope(UiLanguage language, Scope? outer) : IDisposable
    {
        private int _language = (int)language;

        public UiLanguage Language => (UiLanguage)Volatile.Read(ref _language);

        public UiLanguage Swap(UiLanguage next) => (UiLanguage)Interlocked.Exchange(ref _language, (int)next);

        public void Dispose() => Scoped.Value = outer;
    }
}
