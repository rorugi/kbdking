namespace Keyservant.Core.Geometry;

/// <summary>
/// A physical key: its scan code, whether it sends an E0 prefix, its width in key units (1 unit = 19.05 mm)
/// and, for keys without characters (Shift, Enter, …), a fixed label.
/// </summary>
public sealed record PhysicalKey(int ScanCode, bool Extended, double Width, string? FixedLabel = null)
{
    /// <summary>Horizontal position of the key's left edge in key units, set by <see cref="KeyboardGeometry"/>.</summary>
    public double X { get; init; }

    /// <summary>Row index, 0 = number row.</summary>
    public int Row { get; init; }
}

public enum KeyboardShape
{
    /// <summary>102/105-key keyboards used in Europe: tall Enter key, extra key left of Z.</summary>
    Iso,
    /// <summary>101/104-key US keyboards.</summary>
    Ansi,
    /// <summary>106/109-key Japanese keyboards: yen key, ro key, conversion keys.</summary>
    Jis,
    /// <summary>ABNT2 Brazilian keyboards: ISO plus an extra key right of the slash key.</summary>
    Abnt,
}

/// <summary>The main block of a PC keyboard, row by row, for each common keyboard shape.</summary>
public static class KeyboardGeometry
{
    public static IReadOnlyList<IReadOnlyList<PhysicalKey>> Rows(KeyboardShape shape)
    {
        static PhysicalKey K(int sc, double w = 1, string? label = null) => new(sc, false, w, label);
        static PhysicalKey E(int sc, double w = 1, string? label = null) => new(sc, true, w, label);
        static IEnumerable<PhysicalKey> Range(int from, int to) => Enumerable.Range(from, to - from + 1).Select(sc => K(sc));

        var row0 = new List<PhysicalKey> { K(0x29) };
        row0.AddRange(Range(0x02, 0x0D));
        if (shape == KeyboardShape.Jis) { row0.Add(K(0x7D)); row0.Add(K(0x0E, 1, "Backspace")); }
        else row0.Add(K(0x0E, 2, "Backspace"));

        var row1 = new List<PhysicalKey> { K(0x0F, 1.5, "Tab") };
        row1.AddRange(Range(0x10, 0x1B));
        row1.Add(shape == KeyboardShape.Ansi ? K(0x2B, 1.5) : K(0x1C, 1.5, "Enter"));

        var row2 = new List<PhysicalKey> { K(0x3A, 1.75, "Caps Lock") };
        row2.AddRange(Range(0x1E, 0x28));
        if (shape == KeyboardShape.Ansi) row2.Add(K(0x1C, 2.25, "Enter"));
        else
        {
            row2.Add(K(0x2B));
            row2.Add(K(0x1C, 1.25, "Enter"));
        }

        var row3 = new List<PhysicalKey>();
        switch (shape)
        {
            case KeyboardShape.Ansi:
                row3.Add(K(0x2A, 2.25, "Shift"));
                row3.AddRange(Range(0x2C, 0x35));
                row3.Add(K(0x36, 2.75, "Shift"));
                break;
            case KeyboardShape.Jis:
                row3.Add(K(0x2A, 2.25, "Shift"));
                row3.AddRange(Range(0x2C, 0x35));
                row3.Add(K(0x73));
                row3.Add(K(0x36, 1.75, "Shift"));
                break;
            case KeyboardShape.Abnt:
                row3.Add(K(0x2A, 1.25, "Shift"));
                row3.Add(K(0x56));
                row3.AddRange(Range(0x2C, 0x35));
                row3.Add(K(0x73));
                row3.Add(K(0x36, 1.75, "Shift"));
                break;
            default:
                row3.Add(K(0x2A, 1.25, "Shift"));
                row3.Add(K(0x56));
                row3.AddRange(Range(0x2C, 0x35));
                row3.Add(K(0x36, 2.75, "Shift"));
                break;
        }

        var row4 = shape == KeyboardShape.Jis
            ? new List<PhysicalKey>
            {
                K(0x1D, 1.25, "Ctrl"), E(0x5B, 1.25, "Win"), K(0x38, 1.25, "Alt"), K(0x7B, 1.25, "無変換"), K(0x39, 3),
                K(0x79, 1.25, "変換"), K(0x70, 1.25, "かな"), E(0x38, 1.25, "AltGr"), E(0x5D, 1.25, "Menu"), E(0x1D, 1.25, "Ctrl"),
            }
            : new List<PhysicalKey>
            {
                K(0x1D, 1.25, "Ctrl"), E(0x5B, 1.25, "Win"), K(0x38, 1.25, "Alt"), K(0x39, 6.25),
                E(0x38, 1.25, "AltGr"), E(0x5C, 1.25, "Win"), E(0x5D, 1.25, "Menu"), E(0x1D, 1.25, "Ctrl"),
            };

        return [Place(row0, 0), Place(row1, 1), Place(row2, 2), Place(row3, 3), Place(row4, 4)];
    }

    private static List<PhysicalKey> Place(List<PhysicalKey> keys, int row)
    {
        var x = 0.0;
        var placed = new List<PhysicalKey>(keys.Count);
        foreach (var k in keys)
        {
            placed.Add(k with { X = x, Row = row });
            x += k.Width;
        }
        return placed;
    }

    public static string DisplayName(KeyboardShape shape) => shape switch
    {
        KeyboardShape.Iso => "ISO (Europe)",
        KeyboardShape.Ansi => "ANSI (US)",
        KeyboardShape.Jis => "JIS (Japan)",
        KeyboardShape.Abnt => "ABNT2 (Brazil)",
        _ => shape.ToString(),
    };
}
