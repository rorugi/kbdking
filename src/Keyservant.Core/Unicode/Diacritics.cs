using System.Text;

namespace Keyservant.Core.Unicode;

/// <summary>A diacritic: its combining mark, its spacing form (if any) and its XKB dead-key keysym.</summary>
public sealed record Diacritic(char Combining, char? Spacing, string XkbDeadKey, string Name);

/// <summary>Diacritics, their spacing and combining forms, and compositions generated from Unicode data.</summary>
public static class Diacritics
{
    public static IReadOnlyList<Diacritic> All { get; } =
    [
        new('̀', '`', "dead_grave", "grave"),
        new('́', '´', "dead_acute", "acute"),
        new('̂', '^', "dead_circumflex", "circumflex"),
        new('̃', '~', "dead_tilde", "tilde"),
        new('̄', '¯', "dead_macron", "macron"),
        new('̆', '˘', "dead_breve", "breve"),
        new('̇', '˙', "dead_abovedot", "dot above"),
        new('̈', '¨', "dead_diaeresis", "diaeresis"),
        new('̉', null, "dead_hook", "hook above"),
        new('̊', '˚', "dead_abovering", "ring above"),
        new('̋', '˝', "dead_doubleacute", "double acute"),
        new('̌', 'ˇ', "dead_caron", "caron"),
        new('̛', null, "dead_horn", "horn"),
        new('̣', null, "dead_belowdot", "dot below"),
        new('̥', null, "dead_belowring", "ring below"),
        new('̦', null, "dead_belowcomma", "comma below"),
        new('̧', '¸', "dead_cedilla", "cedilla"),
        new('̨', '˛', "dead_ogonek", "ogonek"),
        new('̱', null, "dead_belowmacron", "macron below"),
        new('̵', null, "dead_stroke", "stroke"),
    ];

    /// <summary>Finds a diacritic by its combining, spacing or plain ASCII form (^, ~, `, ', ", ¨ …).</summary>
    public static Diacritic? Find(char c) =>
        All.FirstOrDefault(d => d.Combining == c || d.Spacing == c)
        ?? c switch
        {
            '\'' => All.First(d => d.Combining == '́'),
            '"' => All.First(d => d.Combining == '̈'),
            ',' => All.First(d => d.Combining == '̧'),
            _ => null,
        };

    public static Diacritic? FindXkb(string keysym) => All.FirstOrDefault(d => d.XkbDeadKey == keysym);

    /// <summary>
    /// Every letter that has a precomposed form with this diacritic (from Unicode normalization), e.g.
    /// macron: a→ā, e→ē, … Used to fill dead keys imported from formats that rely on the OS to compose.
    /// </summary>
    public static IReadOnlyList<(char Base, char Result)> Compositions(char combining)
    {
        var list = new List<(char, char)>();
        for (var b = 0x20; b < 0x250; b++)
        {
            var baseChar = (char)b;
            if (!char.IsLetter(baseChar)) continue;
            var composed = (baseChar.ToString() + combining).Normalize(NormalizationForm.FormC);
            if (composed.Length == 1 && composed[0] != baseChar) list.Add((baseChar, composed[0]));
        }
        return list;
    }
}
