using Kbdking.Binary.Pe;
using Kbdking.Core.Model;

namespace Kbdking.Binary.Kbd;

/// <summary>
/// Serializes a <see cref="KbdTablesPlan"/> into the binary KBDTABLES structures of kbd.h inside a
/// <c>.data</c> section. Everything the kernel reads (tables and key-name strings) lives in that one
/// section, because win32k copies only <c>.data</c> out of a layout DLL.
/// </summary>
internal sealed class KbdTablesWriter
{
    private readonly KbdTablesPlan _plan;
    private readonly SectionBuffer _data;
    private readonly int _p = KbdConstants.TablePointerSize;
    private readonly KbdTablesLayout _offsets = new(KbdConstants.TablePointerSize);

    private KbdTablesWriter(KbdTablesPlan plan, SectionBuffer data)
    {
        _plan = plan;
        _data = data;
    }

    /// <summary>Writes all tables and returns the symbol of the KBDTABLES structure.</summary>
    public static Symbol Write(KeyboardLayout layout, SectionBuffer data) => new KbdTablesWriter(KbdTablesPlan.Create(layout), data).WriteAll();

    private Symbol WriteAll()
    {
        var modifiers = WriteModifiers();
        var wcharTables = WriteCharacterTables();
        var ligatures = WriteLigatures();
        var deadKeys = WriteDeadKeys();
        var keyNames = WriteKeyNames(_plan.KeyNames, "aKeyNames");
        var keyNamesExt = WriteKeyNames(_plan.KeyNamesExt, "aKeyNamesExt");
        var keyNamesDead = WriteDeadKeyNames();
        var vscToVk = WriteScanCodes();
        var e0 = WritePrefixed(_plan.ScanCodesE0, "aE0VscToVk");
        var e1 = WritePrefixed(_plan.ScanCodesE1, "aE1VscToVk");

        var tables = _data.Define("KbdTables", _p);
        var start = _data.Length;
        _data.WritePointer(modifiers, _p);
        _data.WritePointer(wcharTables, _p);
        _data.WritePointer(deadKeys, _p);
        _data.WritePointer(keyNames, _p);
        _data.WritePointer(keyNamesExt, _p);
        _data.WritePointer(keyNamesDead, _p);
        _data.WritePointer(vscToVk, _p);
        _data.WriteByte((byte)_plan.ScanCodes.Count);
        _data.Align(_p);
        _data.WritePointer(e0, _p);
        _data.WritePointer(e1, _p);
        _data.WriteUInt32(_plan.LocaleFlags);
        _data.WriteByte((byte)_plan.LigatureMax);
        _data.WriteByte((byte)_plan.LigatureEntrySize);
        _data.Align(_p);
        _data.WritePointer(ligatures, _p);
        _data.WriteUInt32(_plan.KeyboardType);
        _data.WriteUInt32(_plan.KeyboardSubType);
        _data.Align(_p);
        if (_data.Length - start != _offsets.Size) throw new InvalidOperationException("KBDTABLES layout mismatch.");
        return tables;
    }

    private Symbol WriteModifiers()
    {
        var m = _plan.Modifiers;
        var vkToBits = _data.Define("aVkToBits", 2);
        foreach (var v in m.VkToBits)
        {
            _data.WriteByte(v.VirtualKey);
            _data.WriteByte((byte)v.Bits);
        }
        _data.WriteUInt16(0);

        var modifiers = _data.Define("CharModifiers", _p);
        _data.WritePointer(vkToBits, _p);
        _data.WriteUInt16((ushort)(m.ModNumber.Count - 1));
        foreach (var n in m.ModNumber) _data.WriteByte(n);
        return modifiers;
    }

    private Symbol WriteCharacterTables()
    {
        var symbols = new List<Symbol>();
        foreach (var table in _plan.Tables)
        {
            var rows = _data.Define(table.Name, 2);
            foreach (var row in table.Rows)
            {
                _data.WriteByte(row.VirtualKey);
                _data.WriteByte(row.Attributes);
                foreach (var c in row.Chars) _data.WriteUInt16(c);
            }
            _data.WriteZeros(RowSize(table.Width));
            symbols.Add(rows);
        }

        var tableArray = _data.Define("aVkToWcharTable", _p);
        for (var i = 0; i < _plan.Tables.Count; i++)
        {
            _data.WritePointer(symbols[i], _p);
            _data.WriteByte((byte)_plan.Tables[i].Width);
            _data.WriteByte((byte)RowSize(_plan.Tables[i].Width));
            _data.Align(_p);
        }
        _data.WritePointer(null, _p);
        _data.WriteUInt16(0);
        _data.Align(_p);
        return tableArray;
    }

    internal static int RowSize(int width) => 2 + 2 * width;

    private Symbol? WriteLigatures()
    {
        if (_plan.Ligatures.Count == 0) return null;
        var max = _plan.LigatureMax;
        var ligatures = _data.Define("aLigature", 2);
        foreach (var l in _plan.Ligatures)
        {
            _data.WriteByte(l.VirtualKey);
            _data.WriteByte(0);
            _data.WriteUInt16((ushort)l.Column);
            for (var i = 0; i < max; i++) _data.WriteUInt16(i < l.Text.Length ? l.Text[i] : KbdConstants.WchNone);
        }
        _data.WriteZeros(_plan.LigatureEntrySize);
        return ligatures;
    }

    private Symbol? WriteDeadKeys()
    {
        if (_plan.DeadKeys.Count == 0) return null;
        var dead = _data.Define("aDeadKey", 4);
        foreach (var d in _plan.DeadKeys)
        {
            _data.WriteUInt32(d.Base | (uint)d.Accent << 16);
            _data.WriteUInt16(d.Result);
            _data.WriteUInt16(d.Chained ? KbdConstants.DkfDead : (ushort)0);
        }
        _data.WriteZeros(8);
        return dead;
    }

    private Symbol? WriteKeyNames(SortedDictionary<byte, string> names, string name)
    {
        if (names.Count == 0) return null;
        var strings = names.ToDictionary(n => n.Key, n => WriteString(n.Value));
        var table = _data.Define(name, _p);
        foreach (var (sc, _) in names)
        {
            _data.WriteByte(sc);
            _data.WriteZeros(_p - 1);
            _data.WritePointer(strings[sc], _p);
        }
        _data.WriteZeros(_offsets.KeyNameEntrySize);
        return table;
    }

    private Symbol? WriteDeadKeyNames()
    {
        if (_plan.DeadKeyNames.Count == 0) return null;
        var strings = _plan.DeadKeyNames.Select(WriteString).ToList();
        var table = _data.Define("aKeyNamesDead", _p);
        foreach (var s in strings) _data.WritePointer(s, _p);
        _data.WritePointer(null, _p);
        return table;
    }

    private Symbol WriteString(string s)
    {
        var sym = _data.Define("str", 2);
        _data.WriteWideString(s);
        return sym;
    }

    private Symbol WriteScanCodes()
    {
        if (_plan.ScanCodes.Count > 255) throw new InvalidOperationException("At most 255 unprefixed scan codes are possible.");
        var table = _data.Define("ausVK", 2);
        foreach (var vk in _plan.ScanCodes) _data.WriteUInt16(vk);
        if (_plan.ScanCodes.Count == 0) _data.WriteUInt16(0);
        return table;
    }

    private Symbol WritePrefixed(List<(byte Scan, ushort Vk)> map, string name)
    {
        var table = _data.Define(name, 2);
        foreach (var (sc, vk) in map)
        {
            _data.WriteByte(sc);
            _data.WriteByte(0);
            _data.WriteUInt16(vk);
        }
        _data.WriteUInt32(0);
        return table;
    }
}
