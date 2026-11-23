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
/// </code>
/// A constant becomes a property (<c>public static string Caption => Tr.Of("Settings", "Réglages");</c>), so that it is read
/// again at each use; a text kept in a field keeps the language it was made in, until what holds it is built again. A
/// <c>switch</c> or an attribute cannot take a property: compare to the value it stands for, not to its words.
/// The journal, the clip file names and the settings file stay in English: they are read by people helping with a problem,
/// and by the app itself, so they never go through here.
/// <para>
/// English until something sets it, so that every test reads English on a PC of any language. The app's entry point calls
/// <see cref="ForProcess"/> once; from then on <see cref="Set"/> changes the language of the whole process. Without it,
/// <see cref="Set"/> only changes the scope a test or the dump opened with <see cref="Use"/>, and changes nothing anywhere
/// else, so tests that run side by side never see each other's language.
/// </para>
/// </summary>
internal static class Tr
{
    // A narrow or a plain no-break space: the plain one, which Segoe UI and the fonts of the Unix builds all draw.
    private const char FrenchGroup = ' ';

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

    /// <summary>A language for this flow alone, until disposed: a test, or the dump drawing its pages in one language.</summary>
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
                ',' => FrenchGroup,
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

    /// <summary>A size with its unit, a space between: "1.4 MB" or "1,4 Mo".</summary>
    public static string Size(double value, string format, SizeUnit unit) => Number(value, format) + " " + Unit(unit);

    private sealed class Scope(UiLanguage language, Scope? outer) : IDisposable
    {
        private int _language = (int)language;

        public UiLanguage Language => (UiLanguage)Volatile.Read(ref _language);

        public UiLanguage Swap(UiLanguage next) => (UiLanguage)Interlocked.Exchange(ref _language, (int)next);

        public void Dispose() => Scoped.Value = outer;
    }
}
