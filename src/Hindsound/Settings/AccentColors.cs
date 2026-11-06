namespace Hindsound.Settings;

public enum AccentPreset
{
    Lime,
    Mint,
    Cyan,
    Violet,
    Pink,
    White,
    Custom,
}

public enum AccentCheck
{
    Valid,
    InvalidFormat,
    TooDark,
}

/// <summary>The accent colour presets, hex parsing and the WCAG contrast check against the notification background.</summary>
public static class AccentColors
{
    public const string LimeHex = "#D4F53C";

    /// <summary>Background of the notification; the accent must stay visible on it.</summary>
    public const string BackgroundHex = "#15181E";

    /// <summary>Minimum contrast ratio against the background (WCAG large-text / graphics threshold).</summary>
    public const double MinContrast = 3.0;

    /// <summary>The two colours of what is laid on the accent: the dark of the mockup, and the window's text.</summary>
    public const uint DarkOnAccent = 0x14170A, LightOnAccent = 0xF2F4F7;

    /// <summary>Minimum contrast ratio of the text laid on the accent (WCAG body text).</summary>
    public const double MinTextContrast = 4.5;

    private const uint Black = 0x000000, White = 0xFFFFFF;

    public static IReadOnlyDictionary<AccentPreset, string> Presets { get; } = new Dictionary<AccentPreset, string>
    {
        [AccentPreset.Lime] = LimeHex,
        [AccentPreset.Mint] = "#2EF2C4",
        [AccentPreset.Cyan] = "#35D6FF",
        [AccentPreset.Violet] = "#B79CFF",
        [AccentPreset.Pink] = "#FF6FB5",
        [AccentPreset.White] = "#F2F4F7",
    };

    /// <summary>Parses "#RRGGBB", "RRGGBB" or "#RGB" (case-insensitive). Never throws.</summary>
    public static bool TryParseHex(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        if (s[0] == '#')
        {
            s = s[1..];
        }

        if (s.Length == 3 && s.All(char.IsAsciiHexDigit))
        {
            s = string.Concat(s.Select(c => new string(c, 2)));
        }

        if (s.Length != 6 || !s.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        r = Convert.ToByte(s[..2], 16);
        g = Convert.ToByte(s[2..4], 16);
        b = Convert.ToByte(s[4..], 16);
        return true;
    }

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    public static double RelativeLuminance(byte r, byte g, byte b)
    {
        static double Channel(byte v)
        {
            var c = v / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    /// <summary>WCAG contrast ratio between two colours, 1 to 21.</summary>
    public static double ContrastRatio(string firstHex, string secondHex)
    {
        TryParseHex(firstHex, out var r1, out var g1, out var b1);
        TryParseHex(secondHex, out var r2, out var g2, out var b2);
        var a = RelativeLuminance(r1, g1, b1);
        var b = RelativeLuminance(r2, g2, b2);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>WCAG contrast ratio between two colours given as 0xRRGGBB, 1 to 21.</summary>
    public static double ContrastRatio(uint firstRgb, uint secondRgb)
    {
        var a = RelativeLuminance((byte)(firstRgb >> 16), (byte)(firstRgb >> 8), (byte)firstRgb);
        var b = RelativeLuminance((byte)(secondRgb >> 16), (byte)(secondRgb >> 8), (byte)secondRgb);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>
    /// What to lay on the accent: the more readable of the two colours of the app. A mid grey such as #777777 only
    /// gets about 4:1 from either, so there pure black or pure white takes over: one of those always reaches 4.58:1.
    /// </summary>
    public static uint TextOn(uint accentRgb)
    {
        var dark = ContrastRatio(accentRgb, DarkOnAccent);
        var light = ContrastRatio(accentRgb, LightOnAccent);
        if (Math.Max(dark, light) >= MinTextContrast)
        {
            return dark >= light ? DarkOnAccent : LightOnAccent;
        }

        return ContrastRatio(accentRgb, Black) >= ContrastRatio(accentRgb, White) ? Black : White;
    }

    /// <summary>The accent as a colour of text on this background, or the fallback if it would not read there.</summary>
    public static uint AccentAsText(uint accentRgb, uint backgroundRgb, uint fallbackRgb) =>
        ContrastRatio(accentRgb, backgroundRgb) >= MinContrast ? accentRgb : fallbackRgb;

    public static AccentCheck Validate(string? hex)
    {
        if (!TryParseHex(hex, out _, out _, out _))
        {
            return AccentCheck.InvalidFormat;
        }

        return ContrastRatio(Normalize(hex!), BackgroundHex) >= MinContrast ? AccentCheck.Valid : AccentCheck.TooDark;
    }

    /// <summary>"#RRGGBB" in upper case. Only call after a successful parse.</summary>
    public static string Normalize(string hex)
    {
        TryParseHex(hex, out var r, out var g, out var b);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>The colour to use for a stored value: itself if valid, otherwise Lime.</summary>
    public static string Resolve(string? hex) => Validate(hex) == AccentCheck.Valid ? Normalize(hex!) : LimeHex;

    /// <summary>Which preset a stored value is, or Custom for any other valid colour.</summary>
    public static AccentPreset PresetOf(string? hex)
    {
        var resolved = Resolve(hex);
        foreach (var (preset, value) in Presets)
        {
            if (value == resolved)
            {
                return preset;
            }
        }

        return AccentPreset.Custom;
    }
}
