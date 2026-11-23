using System.Text;

namespace Hindsound.Library;

/// <summary>
/// The words of the search field of the Library, and which names they find. Pure: no disk and no window. The folding is written here,
/// letter by letter, because Linux and Mac run with InvariantGlobalization, where a culture-aware comparison and string.Normalize
/// do not take an accent off.
/// </summary>
public static class LibrarySearch
{
    // Each group: what the letters become, then every form of them, lower case and upper case. The letters with a diacritic are
    // listed by hand, as escapes, so the table is the same on every system and in every editor.
    private static readonly (string Folded, string Letters)[] Groups =
    [
        ("a", "àáâãäåāăąǎǟǡǻȁȃȧạảấầẩẫậắằẳẵặ"
            + "ÀÁÂÃÄÅĀĂĄǍǞǠǺȀȂȦẠẢẤẦẨẪẬẮẰẲẴẶ"),
        ("b", "ƀɓḃḅḇƁḂḄḆ"),
        ("c", "çćĉċčƈÇĆĈĊČƇ"),
        ("d", "ďđɗḋḍḏḑḓðĎĐƊḊḌḎḐḒÐ"),
        ("e", "èéêëēĕėęěȅȇȩẹẻẽếềểễệ"
            + "ÈÉÊËĒĔĖĘĚȄȆȨẸẺẼẾỀỂỄỆ"),
        ("f", "ƒḟƑḞ"),
        ("g", "ĝğġģǥǧǵḡĜĞĠĢǤǦǴḠ"),
        ("h", "ĥħḣḥḧḩḫĤĦḢḤḦḨḪ"),
        ("i", "ìíîïĩīĭįıǐȉȋỉị"
            + "ÌÍÎÏĨĪĬĮİǏȈȊỈỊ"),
        ("j", "ĵǰĴ"),
        ("k", "ķƙǩḱḳḵĶƘǨḰḲḴ"),
        ("l", "ĺļľŀłḷḻĻĽĿŁĹḶḺ"),
        ("m", "ḿṁṃḾṀṂ"),
        ("n", "ñńņňǹṅṇṉÑŃŅŇǸṄṆṈ"),
        ("o", "òóôõöøōŏőơǒǫǭǿȍȏȫȭȯȱọỏốồổỗộớờởỡợ"
            + "ÒÓÔÕÖØŌŎŐƠǑǪǬǾȌȎȪȬȮȰỌỎỐỒỔỖỘỚỜỞỠỢ"),
        ("p", "ṕṗƥṔṖƤ"),
        ("r", "ŕŗřȑȓṙṛṟŔŖŘȐȒṘṚṞ"),
        ("s", "śŝşšșṡṣŚŜŞŠȘṠṢ"),
        ("t", "ţťŧțṫṭṯẗŢŤŦȚṪṬṮ"),
        ("u", "ùúûüũūŭůűųưǔǖǘǚǜȕȗụủứừửữự"
            + "ÙÚÛÜŨŪŬŮŰŲƯǓǕǗǙǛȔȖỤỦỨỪỬỮỰ"),
        ("v", "ṽṿṼṾ"),
        ("w", "ŵẁẃẅẇẉŴẀẂẄẆẈ"),
        ("x", "ẋẍẊẌ"),
        ("y", "ýÿŷƴẏỳỵỷỹÝŸŶƳẎỲỴỶỸ"),
        ("z", "źżžƶẑẓẕŹŻŽƵẐẒẔ"),

        // Ligatures, and letters that have no accent to take off: they read as the letters a keyboard without them types.
        ("ae", "æǽǣÆǼǢ"),
        ("oe", "œŒ"),
        ("ss", "ßẞ"),
        ("th", "þÞ"),
        ("ij", "ĳĲ"),
        ("fi", "ﬁ"),
        ("fl", "ﬂ"),
        ("ff", "ﬀ"),
    ];

    private static readonly Dictionary<char, string> Table = BuildTable();

    /// <summary>
    /// The text as the search reads it: lower case, with no accent, <c>oe</c> for the ligature of o and e, <c>ae</c> for the one of
    /// a and e, <c>ss</c> for the German sharp s. A mark that was typed apart from its letter is dropped too.
    /// </summary>
    public static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var letter in text)
        {
            if (letter is >= 'A' and <= 'Z')
            {
                folded.Append((char)(letter + ('a' - 'A')));
            }
            else if (letter < 0x80)
            {
                folded.Append(letter);
            }
            else if (Table.TryGetValue(letter, out var plain))
            {
                folded.Append(plain);
            }
            else if (letter is >= '̀' and <= 'ͯ')
            {
                // A combining mark: the letter before it keeps its place.
            }
            else
            {
                // Another script: the simple case mapping of .NET needs no ICU, and a word of it stays a word.
                folded.Append(char.ToLowerInvariant(letter));
            }
        }

        return folded.ToString();
    }

    /// <summary>What was typed, as the words to find: folded, cut at white space, with none empty.</summary>
    public static IReadOnlyList<string> Words(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return Fold(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The text holds a word to find. White space only has none: it keeps every line, as an empty field does.</summary>
    public static bool IsActive(string? query) => !string.IsNullOrWhiteSpace(query);

    /// <summary>Every word is in the name, in any order and anywhere in it.</summary>
    public static bool Matches(string name, string? query) => Matches(name, Words(query));

    /// <summary>The same, for words already cut by <see cref="Words"/>: a list is searched with one cut of them.</summary>
    public static bool Matches(string name, IReadOnlyList<string> words)
    {
        if (words.Count == 0)
        {
            return true;
        }

        var folded = Fold(name);
        foreach (var word in words)
        {
            if (!folded.Contains(word, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The name of a line as the list shows it, without the extension: what a search reads.</summary>
    public static string VisibleName(string fileName) => Path.GetFileNameWithoutExtension(fileName);

    private static Dictionary<char, string> BuildTable()
    {
        var table = new Dictionary<char, string>(512);
        foreach (var (folded, letters) in Groups)
        {
            foreach (var letter in letters)
            {
                table[letter] = folded;
            }
        }

        return table;
    }
}
