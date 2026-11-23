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
        ("a", "\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u0101\u0103\u0105\u01CE\u01DF\u01E1\u01FB\u0201\u0203\u0227\u1EA1\u1EA3\u1EA5\u1EA7\u1EA9\u1EAB\u1EAD\u1EAF\u1EB1\u1EB3\u1EB5\u1EB7"
            + "\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u0100\u0102\u0104\u01CD\u01DE\u01E0\u01FA\u0200\u0202\u0226\u1EA0\u1EA2\u1EA4\u1EA6\u1EA8\u1EAA\u1EAC\u1EAE\u1EB0\u1EB2\u1EB4\u1EB6"),
        ("b", "\u0180\u0253\u1E03\u1E05\u1E07\u0181\u1E02\u1E04\u1E06"),
        ("c", "\u00E7\u0107\u0109\u010B\u010D\u0188\u00C7\u0106\u0108\u010A\u010C\u0187"),
        ("d", "\u010F\u0111\u0257\u1E0B\u1E0D\u1E0F\u1E11\u1E13\u00F0\u010E\u0110\u018A\u1E0A\u1E0C\u1E0E\u1E10\u1E12\u00D0"),
        ("e", "\u00E8\u00E9\u00EA\u00EB\u0113\u0115\u0117\u0119\u011B\u0205\u0207\u0229\u1EB9\u1EBB\u1EBD\u1EBF\u1EC1\u1EC3\u1EC5\u1EC7"
            + "\u00C8\u00C9\u00CA\u00CB\u0112\u0114\u0116\u0118\u011A\u0204\u0206\u0228\u1EB8\u1EBA\u1EBC\u1EBE\u1EC0\u1EC2\u1EC4\u1EC6"),
        ("f", "\u0192\u1E1F\u0191\u1E1E"),
        ("g", "\u011D\u011F\u0121\u0123\u01E5\u01E7\u01F5\u1E21\u011C\u011E\u0120\u0122\u01E4\u01E6\u01F4\u1E20"),
        ("h", "\u0125\u0127\u1E23\u1E25\u1E27\u1E29\u1E2B\u0124\u0126\u1E22\u1E24\u1E26\u1E28\u1E2A"),
        ("i", "\u00EC\u00ED\u00EE\u00EF\u0129\u012B\u012D\u012F\u0131\u01D0\u0209\u020B\u1EC9\u1ECB"
            + "\u00CC\u00CD\u00CE\u00CF\u0128\u012A\u012C\u012E\u0130\u01CF\u0208\u020A\u1EC8\u1ECA"),
        ("j", "\u0135\u01F0\u0134"),
        ("k", "\u0137\u0199\u01E9\u1E31\u1E33\u1E35\u0136\u0198\u01E8\u1E30\u1E32\u1E34"),
        ("l", "\u013A\u013C\u013E\u0140\u0142\u1E37\u1E3B\u013B\u013D\u013F\u0141\u0139\u1E36\u1E3A"),
        ("m", "\u1E3F\u1E41\u1E43\u1E3E\u1E40\u1E42"),
        ("n", "\u00F1\u0144\u0146\u0148\u01F9\u1E45\u1E47\u1E49\u00D1\u0143\u0145\u0147\u01F8\u1E44\u1E46\u1E48"),
        ("o", "\u00F2\u00F3\u00F4\u00F5\u00F6\u00F8\u014D\u014F\u0151\u01A1\u01D2\u01EB\u01ED\u01FF\u020D\u020F\u022B\u022D\u022F\u0231\u1ECD\u1ECF\u1ED1\u1ED3\u1ED5\u1ED7\u1ED9\u1EDB\u1EDD\u1EDF\u1EE1\u1EE3"
            + "\u00D2\u00D3\u00D4\u00D5\u00D6\u00D8\u014C\u014E\u0150\u01A0\u01D1\u01EA\u01EC\u01FE\u020C\u020E\u022A\u022C\u022E\u0230\u1ECC\u1ECE\u1ED0\u1ED2\u1ED4\u1ED6\u1ED8\u1EDA\u1EDC\u1EDE\u1EE0\u1EE2"),
        ("p", "\u1E55\u1E57\u01A5\u1E54\u1E56\u01A4"),
        ("r", "\u0155\u0157\u0159\u0211\u0213\u1E59\u1E5B\u1E5F\u0154\u0156\u0158\u0210\u0212\u1E58\u1E5A\u1E5E"),
        ("s", "\u015B\u015D\u015F\u0161\u0219\u1E61\u1E63\u015A\u015C\u015E\u0160\u0218\u1E60\u1E62"),
        ("t", "\u0163\u0165\u0167\u021B\u1E6B\u1E6D\u1E6F\u1E97\u0162\u0164\u0166\u021A\u1E6A\u1E6C\u1E6E"),
        ("u", "\u00F9\u00FA\u00FB\u00FC\u0169\u016B\u016D\u016F\u0171\u0173\u01B0\u01D4\u01D6\u01D8\u01DA\u01DC\u0215\u0217\u1EE5\u1EE7\u1EE9\u1EEB\u1EED\u1EEF\u1EF1"
            + "\u00D9\u00DA\u00DB\u00DC\u0168\u016A\u016C\u016E\u0170\u0172\u01AF\u01D3\u01D5\u01D7\u01D9\u01DB\u0214\u0216\u1EE4\u1EE6\u1EE8\u1EEA\u1EEC\u1EEE\u1EF0"),
        ("v", "\u1E7D\u1E7F\u1E7C\u1E7E"),
        ("w", "\u0175\u1E81\u1E83\u1E85\u1E87\u1E89\u0174\u1E80\u1E82\u1E84\u1E86\u1E88"),
        ("x", "\u1E8B\u1E8D\u1E8A\u1E8C"),
        ("y", "\u00FD\u00FF\u0177\u01B4\u1E8F\u1EF3\u1EF5\u1EF7\u1EF9\u00DD\u0178\u0176\u01B3\u1E8E\u1EF2\u1EF4\u1EF6\u1EF8"),
        ("z", "\u017A\u017C\u017E\u01B6\u1E91\u1E93\u1E95\u0179\u017B\u017D\u01B5\u1E90\u1E92\u1E94"),

        // Ligatures, and letters that have no accent to take off: they read as the letters a keyboard without them types.
        ("ae", "\u00E6\u01FD\u01E3\u00C6\u01FC\u01E2"),
        ("oe", "\u0153\u0152"),
        ("ss", "\u00DF\u1E9E"),
        ("th", "\u00FE\u00DE"),
        ("ij", "\u0133\u0132"),
        ("fi", "\uFB01"),
        ("fl", "\uFB02"),
        ("ff", "\uFB00"),
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
            else if (letter is >= '\u0300' and <= '\u036F')
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
