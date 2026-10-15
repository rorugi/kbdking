namespace Keyservant.Core.Compose;

/// <summary>One compose sequence: after the compose key, typing <see cref="Keys"/> produces <see cref="Result"/>.</summary>
/// <param name="Keys">The characters typed after the compose key, e.g. <c>.r</c>.</param>
/// <param name="Result">The character produced, e.g. <c>ṛ</c>. Windows allows one BMP character.</param>
public sealed record ComposeSequence(string Keys, string Result);

/// <summary>
/// A compose key and its sequences. Keyservant compiles them into chained dead keys when the layout is
/// built, so they work natively, without a background program (see <see cref="ComposeCompiler"/>).
/// </summary>
public sealed class ComposeDefinition
{
    /// <summary>Default: the Menu (Application) key, E0 5D.</summary>
    public int ScanCode { get; set; } = 0x5D;

    public bool Extended { get; set; } = true;

    /// <summary>The dead character the compose key produces; typed if a sequence is abandoned with Space.</summary>
    public char Accent { get; set; } = '⎄'; // ⎄ COMPOSITION SYMBOL

    public List<ComposeSequence> Sequences { get; } = [];

    /// <summary>The key as written in files and shown to people, e.g. <c>E0 5D</c> or <c>3A</c>.</summary>
    public string KeyText => Extended ? $"E0 {ScanCode:X2}" : $"{ScanCode:X2}";

    public static (int ScanCode, bool Extended) ParseKey(string text)
    {
        var t = text.Trim().ToUpperInvariant().Replace("0X", "", StringComparison.Ordinal);
        var extended = t.StartsWith("E0", StringComparison.Ordinal) && t.Length > 2;
        if (extended) t = t[2..].Trim();
        return int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var sc) && sc is > 0 and < 0x80
            ? (sc, extended)
            : throw new FormatException($"Invalid compose key '{text}' (expected a scan code such as 3A or E0 5D).");
    }

    /// <summary>Well-known places for a compose key.</summary>
    public static IReadOnlyList<(string Name, int ScanCode, bool Extended)> CommonKeys { get; } =
    [
        ("Menu key", 0x5D, true),
        ("Right Windows key", 0x5C, true),
        // The Copilot key sends Left Windows + Left Shift + F23 (scan code 6E). As the compose key, 6E types the
        // compose dead key instead of F23, so the Copilot shortcut (Win+Shift+F23) no longer fires.
        ("Copilot key", 0x6E, false),
        ("Caps Lock", 0x3A, false),
        ("Right Ctrl", 0x1D, true),
        ("Scroll Lock", 0x46, false),
    ];

    public ComposeDefinition Clone()
    {
        var c = new ComposeDefinition { ScanCode = ScanCode, Extended = Extended, Accent = Accent };
        c.Sequences.AddRange(Sequences);
        return c;
    }
}
