using System.Globalization;

namespace Kbdking.Platform;

/// <summary>Allocation of keyboard layout identifiers. Pure logic, usable and testable on any OS.</summary>
public static class LayoutIds
{
    /// <summary>Picks a free KLID of the form Annn&lt;LANGID&gt;, as MSKLC does.</summary>
    public static string AllocateKlid(IEnumerable<string> existing, ushort languageId)
    {
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        for (var n = 0; n < 0x1000; n++)
        {
            var klid = string.Create(CultureInfo.InvariantCulture, $"A{n:X3}{languageId:X4}");
            if (!used.Contains(klid)) return klid;
        }
        throw new LayoutSystemException("No free layout ID is left for this language.");
    }

    /// <summary>Picks a "Layout Id" registry value one higher than any in use.</summary>
    public static string AllocateLayoutId(IEnumerable<string?> existing)
    {
        var max = 0;
        foreach (var id in existing)
        {
            if (int.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) max = Math.Max(max, v);
        }
        return (max + 1).ToString("X4", CultureInfo.InvariantCulture);
    }
}
