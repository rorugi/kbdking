using System.Buffers.Binary;
using Kbdking.Binary.Kbd;
using Kbdking.Binary.Pe;
using Kbdking.Core.Formats;
using Kbdking.Core.Model;

namespace Kbdking.Binary;

/// <summary>
/// Reads a Windows keyboard layout DLL into a <see cref="KeyboardLayout"/> by parsing the file statically.
/// Nothing from the file is executed: the address of KBDTABLES is found by decoding the few instructions
/// of <c>KbdLayerDescriptor</c>, so untrusted DLLs are safe to open.
/// </summary>
public static class LayoutDllReader
{
    private const int MaxRows = 4096;

    public static KeyboardLayout Read(string path, ConversionReport? report = null) => Read(File.ReadAllBytes(path), report);

    public static KeyboardLayout Read(byte[] file, ConversionReport? report = null)
    {
        report ??= new ConversionReport();
        var pe = PeImage.Parse(file);
        var fnRva = pe.FindExport(KbdConstants.ExportName);
        var tablesRva = DecodeDescriptor(pe, fnRva);

        // The WOW64 build of a layout uses 64-bit pointers in a 32-bit image; a native 32-bit build uses 32-bit ones.
        var pointerSize = pe.IsPe32Plus ? 8 : (LooksLikeTables(pe, tablesRva, 8) ? 8 : 4);
        var layout = new TablesReader(pe, pointerSize, report).Read(tablesRva);
        var dllName = pe.ExportDllName();
        if (!string.IsNullOrEmpty(dllName))
        {
            layout.Metadata.DllName = Path.GetFileNameWithoutExtension(dllName);
            layout.Metadata.Name = layout.Metadata.DllName;
        }
        return layout;
    }

    /// <summary>Finds the address KbdLayerDescriptor returns by decoding its machine code.</summary>
    internal static uint DecodeDescriptor(PeImage pe, uint fnRva, int jumps = 0)
    {
        if (jumps > 4) throw Unsupported(); // jump thunks that lead nowhere (or in a loop)
        var code = pe.Read(fnRva, Math.Min(32, RemainingInSection(pe, fnRva)));
        if (code.Length < 8) throw Unsupported();
        switch (pe.Machine)
        {
            case 0x8664:
            {
                var i = 0;
                // Follow an incremental-linking thunk: jmp rel32.
                if (code[0] == 0xE9)
                {
                    var target = (uint)((int)fnRva + 5 + BinaryPrimitives.ReadInt32LittleEndian(code[1..]));
                    return DecodeDescriptor(pe, target, jumps + 1);
                }
                // lea rax, [rip + disp32]   (48 8D 05 disp32)
                if (code.Length >= 7 && code[i] == 0x48 && code[i + 1] == 0x8D && code[i + 2] == 0x05)
                    return (uint)((int)fnRva + i + 7 + BinaryPrimitives.ReadInt32LittleEndian(code[(i + 3)..]));
                // mov rax, imm64 (48 B8 imm64)
                if (code.Length >= 10 && code[i] == 0x48 && code[i + 1] == 0xB8)
                    return pe.VaToRva(BinaryPrimitives.ReadUInt64LittleEndian(code[(i + 2)..])) ?? throw Unsupported();
                throw Unsupported();
            }
            case 0x014C:
            {
                var i = 0;
                while (i < code.Length - 5)
                {
                    if (code[i] == 0xB8) // mov eax, imm32
                        return pe.VaToRva(BinaryPrimitives.ReadUInt32LittleEndian(code[(i + 1)..])) ?? throw Unsupported();
                    if (code[i] is 0x31 or 0x33 && code[i + 1] == 0xD2) { i += 2; continue; } // xor edx, edx
                    if (code[i] == 0x99) { i++; continue; }                                   // cdq
                    if (code[i] == 0x8B && code[i + 1] == 0xFF) { i += 2; continue; }         // mov edi, edi (hotpatch)
                    break;
                }
                throw Unsupported();
            }
            case 0xAA64:
            {
                // adrp x0, page ; add x0, x0, #imm12
                var adrp = BinaryPrimitives.ReadUInt32LittleEndian(code);
                var add = BinaryPrimitives.ReadUInt32LittleEndian(code[4..]);
                if ((adrp & 0x9F00001F) != 0x90000000 || (add & 0xFFC003FF) != 0x91000000) throw Unsupported();
                long immhi = (adrp >> 5) & 0x7FFFF, immlo = (adrp >> 29) & 0x3;
                var imm = (immhi << 2 | immlo) << 12;
                if ((imm & (1L << 32)) != 0) imm -= 1L << 33;
                var page = ((long)pe.ImageBase + fnRva) & ~0xFFFL;
                var shift = ((add >> 22) & 1) == 1 ? 12 : 0;
                var target = page + imm + ((long)((add >> 10) & 0xFFF) << shift);
                return pe.VaToRva((ulong)target) ?? throw Unsupported();
            }
            default:
                throw new InvalidLayoutDllException($"Unsupported CPU architecture 0x{pe.Machine:X4}.");
        }

        static InvalidLayoutDllException Unsupported() =>
            new("KbdLayerDescriptor uses code kbdking can't decode. Please report this layout so support can be added.");
    }

    private static int RemainingInSection(PeImage pe, uint rva)
    {
        var s = pe.SectionOf(rva) ?? throw new InvalidLayoutDllException("KbdLayerDescriptor is outside every section.");
        return (int)Math.Min(s.RawSize - (rva - s.VirtualAddress), int.MaxValue);
    }

    private static bool LooksLikeTables(PeImage pe, uint rva, int pointerSize)
    {
        try
        {
            var l = new KbdTablesLayout(pointerSize);
            ulong Ptr(int offset) => pointerSize == 8 ? pe.ReadUInt64(rva + (uint)offset) : pe.ReadUInt32(rva + (uint)offset);
            var modifiers = Ptr(l.CharModifiers);
            var tables = Ptr(l.VkToWcharTable);
            var vsc = Ptr(l.VscToVk);
            return modifiers != 0 && tables != 0 && vsc != 0 && pe.IsValidVa(modifiers) && pe.IsValidVa(tables) && pe.IsValidVa(vsc);
        }
        catch (InvalidLayoutDllException)
        {
            return false;
        }
    }

    private sealed class TablesReader(PeImage pe, int pointerSize, ConversionReport report)
    {
        private readonly KbdTablesLayout _l = new(pointerSize);
        private readonly int _p = pointerSize;

        private uint? Ptr(uint rva) => pe.VaToRva(_p == 8 ? pe.ReadUInt64(rva) : pe.ReadUInt32(rva));

        public KeyboardLayout Read(uint t)
        {
            var layout = new KeyboardLayout
            {
                Modifiers = ReadModifiers(Ptr(t + (uint)_l.CharModifiers) ?? throw new InvalidLayoutDllException("The layout has no modifier table.")),
            };

            var flags = pe.ReadUInt32(t + (uint)_l.LocaleFlags);
            layout.Flags = (LocaleFlags)(flags & 0x7);
            layout.KbdVersion = (ushort)(flags >> 16);
            var lgMax = pe.ReadByte(t + (uint)_l.LgMax);
            var lgEntry = pe.ReadByte(t + (uint)_l.LgEntrySize);
            var ligatures = layout.KbdVersion >= 1 ? ReadLigatures(Ptr(t + (uint)_l.Ligature), lgMax, lgEntry) : [];

            ReadCharacterTables(layout, Ptr(t + (uint)_l.VkToWcharTable) ?? throw new InvalidLayoutDllException("The layout has no character tables."), ligatures);
            ReadDeadKeys(layout, Ptr(t + (uint)_l.DeadKey));
            ReadKeyNames(Ptr(t + (uint)_l.KeyNames), layout.KeyNames);
            ReadKeyNames(Ptr(t + (uint)_l.KeyNamesExt), layout.KeyNamesExt);
            ReadDeadKeyNames(layout, Ptr(t + (uint)_l.KeyNamesDead));

            var count = pe.ReadByte(t + (uint)_l.MaxVscToVk);
            if (Ptr(t + (uint)_l.VscToVk) is { } vsc)
            {
                for (var i = 0; i < count; i++) layout.ScanCodes.Normal.Add(ScanCodeTarget.FromRaw(pe.ReadUInt16(vsc + (uint)(2 * i))));
            }
            ReadPrefixed(Ptr(t + (uint)_l.VscToVkE0), layout.ScanCodes.E0);
            ReadPrefixed(Ptr(t + (uint)_l.VscToVkE1), layout.ScanCodes.E1);

            // dwType/dwSubType were added to KBDTABLES in Windows XP; very old layouts end before them.
            try
            {
                var type = pe.ReadUInt32(t + (uint)_l.Type);
                var subType = pe.ReadUInt32(t + (uint)_l.SubType);
                if (type is > 0 and < 0x100)
                {
                    layout.KeyboardType = type;
                    layout.KeyboardSubType = subType;
                }
            }
            catch (InvalidLayoutDllException)
            {
            }

            layout.NormalizeShiftStates();
            return layout;
        }

        private ModifierTable ReadModifiers(uint rva)
        {
            var m = new ModifierTable();
            var vkToBits = Ptr(rva) ?? throw new InvalidLayoutDllException("Modifier table without keys.");
            for (var i = 0; i < 64; i++)
            {
                var vk = pe.ReadByte(vkToBits + (uint)(2 * i));
                if (vk == 0) break;
                m.VkToBits.Add(new VkModifierBit(vk, (ModifierBits)pe.ReadByte(vkToBits + (uint)(2 * i + 1))));
            }
            var max = pe.ReadUInt16(rva + (uint)_p);
            if (max > 0xFF) throw new InvalidLayoutDllException("Modifier table is too large.");
            for (var i = 0; i <= max; i++) m.ModNumber.Add(pe.ReadByte(rva + (uint)_p + 2 + (uint)i));
            return m;
        }

        private Dictionary<(byte Vk, int Column), string> ReadLigatures(uint? rva, int lgMax, int entrySize)
        {
            var result = new Dictionary<(byte, int), string>();
            if (rva is null || lgMax == 0) return result;
            if (entrySize < 4 + 2 * lgMax) throw new InvalidLayoutDllException("Ligature entries are smaller than declared.");
            for (var i = 0; i < MaxRows; i++)
            {
                var row = rva.Value + (uint)(i * entrySize);
                var vk = pe.ReadByte(row);
                if (vk == 0) return result;
                var column = pe.ReadUInt16(row + 2);
                var chars = new List<char>();
                for (var c = 0; c < lgMax; c++)
                {
                    var ch = pe.ReadUInt16(row + 4 + (uint)(2 * c));
                    if (ch == KbdConstants.WchNone) break;
                    chars.Add((char)ch);
                }
                result.TryAdd((vk, column), new string([.. chars]));
            }
            throw new InvalidLayoutDllException("Ligature table is not terminated.");
        }

        private void ReadCharacterTables(KeyboardLayout layout, uint rva, Dictionary<(byte, int), string> ligatures)
        {
            var columns = layout.Modifiers.ColumnCount;
            for (var t = 0; t < 64; t++)
            {
                var entry = rva + (uint)(t * _l.WcharTableEntrySize);
                var rows = Ptr(entry);
                if (rows is null) return;
                var width = pe.ReadByte(entry + (uint)_p);
                var size = pe.ReadByte(entry + (uint)_p + 1);
                if (size < 2 + 2 * width) throw new InvalidLayoutDllException("Character table rows are smaller than declared.");
                ReadRows(layout, rows.Value, width, size, ligatures, columns);
            }
            throw new InvalidLayoutDllException("Too many character tables.");
        }

        private void ReadRows(KeyboardLayout layout, uint rva, int width, int size, Dictionary<(byte, int), string> ligatures, int columns)
        {
            KeyDefinition? key = null;
            ushort[]? pendingDeadRow = null;   // row whose WCH_DEAD columns are resolved by the next 0xFF row
            bool pendingDeadIsCaps = false;
            bool expectCapsRow = false;

            for (var r = 0; r < MaxRows; r++)
            {
                var row = rva + (uint)(r * size);
                var vk = pe.ReadByte(row);
                if (vk == 0) return;
                var attributes = pe.ReadByte(row + 1);
                var chars = new ushort[width];
                for (var c = 0; c < width; c++) chars[c] = pe.ReadUInt16(row + 2 + (uint)(2 * c));

                if (vk == KbdConstants.ContinuationVk)
                {
                    if (key is null) { report.Warn("A continuation row has no key before it; ignored."); continue; }
                    if (expectCapsRow)
                    {
                        expectCapsRow = false;
                        key.CapsOutputs = [];
                        if (pendingDeadRow is not null && !pendingDeadIsCaps)
                        {
                            // Windows reads the dead characters of the main row from the row right after it.
                            ResolveDeads(layout, key, key.Outputs, pendingDeadRow, chars, columns);
                            pendingDeadRow = null;
                        }
                        Fill(layout, key, key.CapsOutputs, chars, ligatures, columns, isCaps: true, out var capsHasDead);
                        if (capsHasDead) { pendingDeadRow = chars; pendingDeadIsCaps = true; }
                        continue;
                    }
                    if (pendingDeadRow is not null)
                    {
                        ResolveDeads(layout, key, pendingDeadIsCaps ? key.CapsOutputs! : key.Outputs, pendingDeadRow, chars, columns);
                        pendingDeadRow = null;
                        continue;
                    }
                    report.Warn($"Key {key.Name}: unexpected continuation row; ignored.");
                    continue;
                }

                if (pendingDeadRow is not null)
                {
                    report.Warn($"Key {key?.Name}: dead key row is missing; dead keys shown as U+F001.");
                    pendingDeadRow = null;
                }

                if (layout.FindKey(vk) is not null)
                {
                    report.Warn($"Key {VirtualKeys.GetName(vk)} appears in more than one table; Windows uses the first, so the later one is ignored.");
                    key = new KeyDefinition(vk); // parsed but discarded
                }
                else
                {
                    key = new KeyDefinition(vk);
                    layout.Keys.Add(key);
                }
                key.Attributes = (KeyAttributes)(attributes & ~KbdConstants.AttrSgCaps);
                expectCapsRow = (attributes & KbdConstants.AttrSgCaps) != 0;
                if (expectCapsRow) key.Attributes |= KeyAttributes.SgCaps;
                Fill(layout, key, key.Outputs, chars, ligatures, columns, isCaps: false, out var hasDead);
                if (hasDead) { pendingDeadRow = chars; pendingDeadIsCaps = false; }
            }
            throw new InvalidLayoutDllException("Character table is not terminated.");
        }

        private void Fill(KeyboardLayout layout, KeyDefinition key, SortedDictionary<ShiftState, KeyOutput> target,
            ushort[] chars, Dictionary<(byte, int), string> ligatures, int columns, bool isCaps, out bool hasDead)
        {
            hasDead = false;
            for (var c = 0; c < chars.Length; c++)
            {
                var ch = chars[c];
                if (ch == KbdConstants.WchNone) continue;
                if (c >= columns || layout.Modifiers.StatesOf(c).Count == 0)
                {
                    report.Warn($"Key {key.Name}: column {c} is not selected by any modifier combination; its character is dropped.");
                    continue;
                }
                var state = layout.Modifiers.CanonicalState(c);
                if (ch == KbdConstants.WchDead)
                {
                    hasDead = true;
                    target[state] = KeyOutput.Dead(''); // placeholder until the dead row is read
                }
                else if (ch == KbdConstants.WchLgtr && !isCaps)
                {
                    if (ligatures.TryGetValue((key.VirtualKey, c), out var text) && text.Length > 0) target[state] = KeyOutput.Of(text);
                    else report.Warn($"Key {key.Name}: ligature for column {c} is missing.");
                }
                else
                {
                    target[state] = KeyOutput.Of((char)ch);
                }
            }
        }

        private void ResolveDeads(KeyboardLayout layout, KeyDefinition key, SortedDictionary<ShiftState, KeyOutput> target,
            ushort[] mainRow, ushort[] deadRow, int columns)
        {
            for (var c = 0; c < mainRow.Length && c < columns; c++)
            {
                if (mainRow[c] != KbdConstants.WchDead || layout.Modifiers.StatesOf(c).Count == 0) continue;
                var state = layout.Modifiers.CanonicalState(c);
                var accent = deadRow[c];
                if (accent is KbdConstants.WchNone or KbdConstants.WchDead or KbdConstants.WchLgtr or 0)
                {
                    report.Warn($"Key {key.Name}: dead key in column {c} has no accent character.");
                    target.Remove(state);
                    continue;
                }
                target[state] = KeyOutput.Dead((char)accent);
            }
        }

        private void ReadDeadKeys(KeyboardLayout layout, uint? rva)
        {
            if (rva is null) return;
            for (var i = 0; i < 65536; i++)
            {
                var e = rva.Value + (uint)(8 * i);
                var both = pe.ReadUInt32(e);
                if (both == 0) return;
                var composed = (char)pe.ReadUInt16(e + 4);
                var flags = pe.ReadUInt16(e + 6);
                var accent = (char)(both >> 16);
                var baseChar = (char)(both & 0xFFFF);
                var d = layout.GetOrAddDeadKey(accent);
                if (d.Find(baseChar) is not null)
                {
                    report.Warn($"Dead key U+{(int)accent:X4}: base U+{(int)baseChar:X4} appears twice; Windows uses the first.");
                    continue;
                }
                d.Entries.Add(new DeadKeyEntry(baseChar, composed, (flags & KbdConstants.DkfDead) != 0));
            }
            throw new InvalidLayoutDllException("Dead key table is not terminated.");
        }

        private void ReadKeyNames(uint? rva, SortedDictionary<byte, string> target)
        {
            if (rva is null) return;
            for (var i = 0; i < 512; i++)
            {
                var e = rva.Value + (uint)(i * _l.KeyNameEntrySize);
                var sc = pe.ReadByte(e);
                var str = Ptr(e + (uint)_p);
                if (str is null) return;
                target[sc] = pe.ReadWideZ(str.Value);
            }
            throw new InvalidLayoutDllException("Key name table is not terminated.");
        }

        private void ReadDeadKeyNames(KeyboardLayout layout, uint? rva)
        {
            if (rva is null) return;
            for (var i = 0; i < 512; i++)
            {
                var str = Ptr(rva.Value + (uint)(i * _p));
                if (str is null) return;
                var s = pe.ReadWideZ(str.Value);
                if (s.Length == 0) continue;
                layout.GetOrAddDeadKey(s[0]).Name = s[1..];
            }
            throw new InvalidLayoutDllException("Dead key name table is not terminated.");
        }

        private void ReadPrefixed(uint? rva, SortedDictionary<byte, ScanCodeTarget> target)
        {
            if (rva is null) return;
            for (var i = 0; i < 256; i++)
            {
                var e = rva.Value + (uint)(4 * i);
                var sc = pe.ReadByte(e);
                if (sc == 0) return;
                target[sc] = ScanCodeTarget.FromRaw(pe.ReadUInt16(e + 2));
            }
            throw new InvalidLayoutDllException("Scan code table is not terminated.");
        }
    }
}
