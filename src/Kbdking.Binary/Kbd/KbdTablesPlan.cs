using Kbdking.Core.Model;

namespace Kbdking.Binary.Kbd;

/// <summary>One VK_TO_WCHARS row: a key, a continuation row with dead characters, or an SGCAPS row.</summary>
internal sealed record WcharRow(byte VirtualKey, byte Attributes, ushort[] Chars);

/// <summary>One VK_TO_WCHARSn table: all rows have the same number of columns.</summary>
internal sealed record WcharTable(string Name, int Width, List<WcharRow> Rows);

internal sealed record LigatureRow(byte VirtualKey, int Column, string Text);

internal sealed record DeadKeyRow(char Accent, char Base, char Result, bool Chained);

/// <summary>
/// The exact contents of the kbd.h tables for a layout, independent of how they are written out.
/// Both the binary writer and the C source writer serialize this plan, so a DLL built by kbdking and
/// one compiled from the generated C contain the same tables.
/// </summary>
internal sealed class KbdTablesPlan
{
    public required ModifierTable Modifiers { get; init; }
    public required List<WcharTable> Tables { get; init; }
    public required List<LigatureRow> Ligatures { get; init; }
    public required List<DeadKeyRow> DeadKeys { get; init; }
    public required SortedDictionary<byte, string> KeyNames { get; init; }
    public required SortedDictionary<byte, string> KeyNamesExt { get; init; }
    public required List<string> DeadKeyNames { get; init; }
    public required List<ushort> ScanCodes { get; init; }
    public required List<(byte Scan, ushort Vk)> ScanCodesE0 { get; init; }
    public required List<(byte Scan, ushort Vk)> ScanCodesE1 { get; init; }
    public required uint LocaleFlags { get; init; }
    public required uint KeyboardType { get; init; }
    public required uint KeyboardSubType { get; init; }

    public int LigatureMax => Ligatures.Count == 0 ? 0 : Ligatures.Max(l => l.Text.Length);

    public int LigatureEntrySize => Ligatures.Count == 0 ? 0 : 4 + 2 * LigatureMax;

    public static KbdTablesPlan Create(KeyboardLayout layout)
    {
        var columns = layout.Modifiers.Columns;
        var ligatures = new List<LigatureRow>();
        var tables = new List<WcharTable>();

        // One table for all keys, then the numpad last so that VkKeyScan('1') prefers the number row
        // (the same order Windows' own layouts use).
        var main = layout.Keys.Where(k => !VirtualKeys.IsNumpadDigit(k.VirtualKey)).ToList();
        var numpad = layout.Keys.Where(k => VirtualKeys.IsNumpadDigit(k.VirtualKey)).ToList();
        if (main.Count > 0) tables.Add(BuildTable("aVkToWch", main, columns.Count, columns, ligatures));
        if (numpad.Count > 0)
        {
            var width = numpad.SelectMany(k => k.Outputs.Keys).Select(s => (layout.Modifiers.ColumnOf(s) ?? 0) + 1).DefaultIfEmpty(1).Max();
            tables.Add(BuildTable("aVkToWchNumpad", numpad, Math.Max(1, width), columns, ligatures));
        }

        var dead = new List<DeadKeyRow>();
        foreach (var d in layout.DeadKeys)
        {
            foreach (var e in d.Entries) dead.Add(new DeadKeyRow(d.Accent, e.Base, e.Result, e.Chained));
        }

        var version = ligatures.Count > 0 ? Math.Max((ushort)1, layout.KbdVersion) : layout.KbdVersion;
        return new KbdTablesPlan
        {
            Modifiers = layout.Modifiers,
            Tables = tables,
            Ligatures = ligatures,
            DeadKeys = dead,
            KeyNames = layout.KeyNames,
            KeyNamesExt = layout.KeyNamesExt,
            DeadKeyNames = [.. layout.DeadKeys.Where(d => !string.IsNullOrEmpty(d.Name)).Select(d => d.Accent + d.Name)],
            ScanCodes = [.. layout.ScanCodes.Normal.Select(t => t.Raw)],
            ScanCodesE0 = [.. layout.ScanCodes.E0.Select(p => (p.Key, p.Value.Raw))],
            ScanCodesE1 = [.. layout.ScanCodes.E1.Select(p => (p.Key, p.Value.Raw))],
            LocaleFlags = (uint)(ushort)layout.Flags | (uint)version << 16,
            KeyboardType = layout.KeyboardType,
            KeyboardSubType = layout.KeyboardSubType,
        };
    }

    private static WcharTable BuildTable(string name, List<KeyDefinition> keys, int width, IReadOnlyList<ShiftState> columns, List<LigatureRow> ligatures)
    {
        var rows = new List<WcharRow>();
        foreach (var key in keys)
        {
            var hasCaps = key.CapsOutputs is not null;
            var attributes = (byte)((key.Attributes & ~KeyAttributes.SgCaps) | (hasCaps ? KeyAttributes.SgCaps : 0));
            var (mainChars, mainDeads) = Encode(key, key.Outputs, width, columns, ligatures);
            rows.Add(new WcharRow(key.VirtualKey, attributes, mainChars));
            if (hasCaps)
            {
                // Windows reads the Caps Lock row right after the key and, for dead keys in it, the row after that.
                var (capsChars, capsDeads) = Encode(key, key.CapsOutputs!, width, columns, null);
                rows.Add(new WcharRow(KbdConstants.ContinuationVk, 0, capsChars));
                if (capsDeads is not null) rows.Add(DeadRow(capsDeads));
            }
            else if (mainDeads is not null)
            {
                rows.Add(DeadRow(mainDeads));
            }
        }
        return new WcharTable(name, width, rows);
    }

    private static WcharRow DeadRow(ushort[] accents) => new(KbdConstants.ContinuationVk, 0, accents);

    private static (ushort[] Chars, ushort[]? Deads) Encode(KeyDefinition key, SortedDictionary<ShiftState, KeyOutput> outputs,
        int width, IReadOnlyList<ShiftState> columns, List<LigatureRow>? ligatures)
    {
        var chars = new ushort[width];
        ushort[]? deads = null;
        for (var c = 0; c < width; c++)
        {
            var o = c < columns.Count ? outputs.GetValueOrDefault(columns[c]) : null;
            if (o is null)
            {
                chars[c] = KbdConstants.WchNone;
            }
            else if (o.IsDead)
            {
                chars[c] = KbdConstants.WchDead;
                deads ??= Enumerable.Repeat(KbdConstants.WchNone, width).ToArray();
                deads[c] = o.Accent;
            }
            else if (o.IsLigature)
            {
                if (ligatures is null) throw new InvalidOperationException($"Key {key.Name}: ligatures are not possible in the Caps Lock row.");
                chars[c] = KbdConstants.WchLgtr;
                ligatures.Add(new LigatureRow(key.VirtualKey, c, o.Text));
            }
            else
            {
                chars[c] = o.Text[0];
            }
        }
        return (chars, deads);
    }
}
