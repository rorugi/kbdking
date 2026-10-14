namespace Keyservant.Presentation;

/// <summary>A physical key: its scan code, whether it sends an E0 prefix, and its width in key units.</summary>
public sealed record PhysicalKey(int ScanCode, bool Extended, double Width, string? FixedLabel = null);

public enum KeyboardShape
{
    /// <summary>102/105-key keyboards used in Europe: tall Enter key, extra key left of Z.</summary>
    Iso,
    /// <summary>101/104-key US keyboards.</summary>
    Ansi,
}

/// <summary>The main block of a PC keyboard, row by row.</summary>
public static class KeyboardGeometry
{
    public static IReadOnlyList<IReadOnlyList<PhysicalKey>> Rows(KeyboardShape shape)
    {
        static PhysicalKey K(int sc, double w = 1, string? label = null) => new(sc, false, w, label);
        static PhysicalKey E(int sc, double w = 1, string? label = null) => new(sc, true, w, label);
        static IEnumerable<PhysicalKey> Range(int from, int to) => Enumerable.Range(from, to - from + 1).Select(sc => K(sc));

        var row0 = new List<PhysicalKey> { K(0x29) };
        row0.AddRange(Range(0x02, 0x0D));
        row0.Add(K(0x0E, 2, "Backspace"));

        var row1 = new List<PhysicalKey> { K(0x0F, 1.5, "Tab") };
        row1.AddRange(Range(0x10, 0x1B));
        row1.Add(shape == KeyboardShape.Iso ? K(0x1C, 1.5, "Enter") : K(0x2B, 1.5));

        var row2 = new List<PhysicalKey> { K(0x3A, 1.75, "Caps Lock") };
        row2.AddRange(Range(0x1E, 0x28));
        if (shape == KeyboardShape.Iso)
        {
            row2.Add(K(0x2B));
            row2.Add(K(0x1C, 1.25, "Enter"));
        }
        else
        {
            row2.Add(K(0x1C, 2.25, "Enter"));
        }

        var row3 = new List<PhysicalKey>();
        if (shape == KeyboardShape.Iso)
        {
            row3.Add(K(0x2A, 1.25, "Shift"));
            row3.Add(K(0x56));
        }
        else
        {
            row3.Add(K(0x2A, 2.25, "Shift"));
        }
        row3.AddRange(Range(0x2C, 0x35));
        row3.Add(K(0x36, 2.75, "Shift"));

        var row4 = new List<PhysicalKey>
        {
            K(0x1D, 1.25, "Ctrl"), E(0x5B, 1.25, "Win"), K(0x38, 1.25, "Alt"), K(0x39, 6.25),
            E(0x38, 1.25, "AltGr"), E(0x5C, 1.25, "Win"), E(0x5D, 1.25, "Menu"), E(0x1D, 1.25, "Ctrl"),
        };
        return [row0, row1, row2, row3, row4];
    }
}
