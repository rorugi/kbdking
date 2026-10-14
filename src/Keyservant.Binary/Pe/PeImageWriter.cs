using System.Buffers.Binary;
using System.Text;

namespace Keyservant.Binary.Pe;

/// <summary>CPU architecture of a generated layout DLL.</summary>
public enum LayoutArchitecture : ushort
{
    /// <summary>64-bit DLL for System32.</summary>
    X64 = 0x8664,

    /// <summary>32-bit DLL for SysWOW64, with 64-bit pointers inside the tables (WOW64 layout).</summary>
    X86Wow64 = 0x014C,
}

/// <summary>
/// Writes a minimal, data-only Windows DLL: a tiny code stub that returns the address of a symbol,
/// an export table naming that stub, the data section, optional resources and base relocations.
/// The output is deterministic: the same input always produces the same bytes.
/// </summary>
internal sealed class PeImageWriter
{
    private const uint SectionAlignment = 0x1000;
    private const uint FileAlignment = 0x200;
    private const int DosHeaderSize = 0x80;

    private const uint ScnCode = 0x00000020, ScnInitData = 0x00000040, ScnExecute = 0x20000000,
        ScnRead = 0x40000000, ScnWrite = 0x80000000, ScnDiscardable = 0x02000000;

    /// <summary>
    /// IMAGE_SUBSYSTEM_WINDOWS_GUI. Windows' own layout DLLs are linked as native-subsystem images, but
    /// user-mode loaders accept either; GUI is the conservative choice until confirmed on real systems.
    /// </summary>
    public ushort Subsystem { get; init; } = 2;

    public required LayoutArchitecture Architecture { get; init; }

    public required string DllFileName { get; init; }

    public required SectionBuffer Data { get; init; }

    /// <summary>The symbol whose address the exported function returns.</summary>
    public required Symbol ExportTarget { get; init; }

    public required string ExportName { get; init; }

    /// <summary>Builds the .rsrc section for a given section RVA (resource entries contain RVAs).</summary>
    public Func<uint, byte[]>? Resources { get; init; }

    public ushort ImageVersionMajor { get; init; } = 1;

    public ushort ImageVersionMinor { get; init; }

    private bool Is64 => Architecture == LayoutArchitecture.X64;

    private ulong ImageBase => Is64 ? 0x0000000180000000UL : 0x10000000UL;

    public byte[] Write()
    {
        // ---- .text: the code stub
        var text = new SectionBuffer(".text");
        var stub = text.Define("stub");
        // ---- .rdata: export directory
        var rdata = new SectionBuffer(".rdata");
        var reloc = new SectionBuffer(".reloc");

        var sections = new List<(SectionBuffer Buffer, uint Characteristics)>
        {
            (text, ScnCode | ScnExecute | ScnRead),
            (rdata, ScnInitData | ScnRead),
            (Data, ScnInitData | ScnRead | ScnWrite),
        };

        // Code size is fixed, so we can lay out RVAs before writing the bytes.
        var stubSize = Is64 ? 7 + 1 : 5 + 2 + 1;
        var exportDirSize = 40 + 4 + 4 + 2 + DllFileName.Length + 1 + ExportName.Length + 1;
        uint rva = SectionAlignment;
        text.Rva = rva; rva += AlignUp((uint)stubSize, SectionAlignment);
        rdata.Rva = rva; rva += AlignUp((uint)exportDirSize, SectionAlignment);
        Data.Rva = rva; rva += AlignUp((uint)Math.Max(1, Data.Length), SectionAlignment);

        byte[]? resources = null;
        uint rsrcRva = 0;
        if (Resources is not null)
        {
            rsrcRva = rva;
            resources = Resources(rsrcRva);
            rva += AlignUp((uint)resources.Length, SectionAlignment);
        }
        reloc.Rva = rva;

        var targetRva = Data.Rva + (uint)ExportTarget.Offset;
        if (ExportTarget.Section != Data) throw new InvalidOperationException("The export target must be in the data section.");

        // Write the stub.
        if (Is64)
        {
            // lea rax, [rip + disp32] ; ret
            var disp = (int)(targetRva - (text.Rva + 7));
            text.WriteBytes([0x48, 0x8D, 0x05]);
            text.WriteUInt32((uint)disp);
            text.WriteByte(0xC3);
        }
        else
        {
            // mov eax, imm32 ; xor edx, edx ; ret   (EDX:EAX = __ptr64 return value)
            text.WriteByte(0xB8);
            text.AddFixup(text.Length, ExportTarget, 4);
            text.WriteUInt32(0);
            text.WriteBytes([0x31, 0xD2, 0xC3]);
        }
        if (text.Length != stubSize) throw new InvalidOperationException("Stub size mismatch.");

        // Write the export directory.
        var exportDirRva = rdata.Rva;
        uint eat = exportDirRva + 40, enpt = eat + 4, eot = enpt + 4, dllNameRva = eot + 2;
        var fnNameRva = dllNameRva + (uint)DllFileName.Length + 1;
        rdata.WriteUInt32(0);                 // Characteristics
        rdata.WriteUInt32(0);                 // TimeDateStamp
        rdata.WriteUInt16(0);                 // MajorVersion
        rdata.WriteUInt16(0);                 // MinorVersion
        rdata.WriteUInt32(dllNameRva);        // Name
        rdata.WriteUInt32(1);                 // Base
        rdata.WriteUInt32(1);                 // NumberOfFunctions
        rdata.WriteUInt32(1);                 // NumberOfNames
        rdata.WriteUInt32(eat);               // AddressOfFunctions
        rdata.WriteUInt32(enpt);              // AddressOfNames
        rdata.WriteUInt32(eot);               // AddressOfNameOrdinals
        rdata.WriteUInt32(text.Rva);          // EAT[0]
        rdata.WriteUInt32(fnNameRva);         // ENPT[0]
        rdata.WriteUInt16(0);                 // EOT[0]
        rdata.WriteAsciiString(DllFileName);
        rdata.WriteAsciiString(ExportName);
        if (rdata.Length != exportDirSize) throw new InvalidOperationException("Export directory size mismatch.");

        // Resolve pointer fixups into section bytes and collect base relocations.
        var images = new Dictionary<SectionBuffer, byte[]>();
        var relocations = new List<(uint Rva, ushort Type)>();
        foreach (var (section, _) in sections)
        {
            var bytes = section.ToArray();
            foreach (var f in section.Fixups)
            {
                if (!f.Target.IsDefined) throw new InvalidOperationException($"Undefined symbol {f.Target.Name}.");
                var va = ImageBase + f.Target.Section!.Rva + (uint)f.Target.Offset;
                if (f.Size == 8)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(f.Offset), va);
                    // In a 32-bit image the address fits in the low DWORD; the high DWORD stays zero.
                    relocations.Add((section.Rva + (uint)f.Offset, Is64 ? (ushort)10 /*DIR64*/ : (ushort)3 /*HIGHLOW*/));
                }
                else if (f.Size == 4)
                {
                    if (Is64) throw new InvalidOperationException("32-bit absolute addresses are not relocatable in a 64-bit image.");
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(f.Offset), checked((uint)va));
                    relocations.Add((section.Rva + (uint)f.Offset, 3));
                }
                else throw new InvalidOperationException($"Unsupported pointer size {f.Size}.");
            }
            images[section] = bytes;
        }

        WriteRelocations(reloc, relocations);

        // Final section list with resources and relocations.
        var all = new List<(string Name, uint Rva, byte[] Bytes, uint Characteristics)>();
        foreach (var (section, ch) in sections) all.Add((section.Name, section.Rva, images[section], ch));
        if (resources is not null) all.Add((".rsrc", rsrcRva, resources, ScnInitData | ScnRead));
        all.Add((".reloc", reloc.Rva, reloc.ToArray(), ScnInitData | ScnRead | ScnDiscardable));

        return Assemble(all, exportDirRva, (uint)exportDirSize, rsrcRva, (uint)(resources?.Length ?? 0), reloc.Rva, (uint)reloc.Length);
    }

    private static void WriteRelocations(SectionBuffer reloc, List<(uint Rva, ushort Type)> relocations)
    {
        foreach (var page in relocations.OrderBy(r => r.Rva).GroupBy(r => r.Rva & ~0xFFFu))
        {
            var entries = page.ToList();
            var count = entries.Count + (entries.Count % 2); // keep blocks 4-byte aligned
            reloc.WriteUInt32(page.Key);
            reloc.WriteUInt32((uint)(8 + 2 * count));
            foreach (var (rva, type) in entries) reloc.WriteUInt16((ushort)(type << 12 | (int)(rva & 0xFFF)));
            if (count != entries.Count) reloc.WriteUInt16(0); // IMAGE_REL_BASED_ABSOLUTE padding
        }
    }

    private byte[] Assemble(List<(string Name, uint Rva, byte[] Bytes, uint Characteristics)> sections,
        uint exportRva, uint exportSize, uint rsrcRva, uint rsrcSize, uint relocRva, uint relocSize)
    {
        var optSize = Is64 ? 240 : 224;
        var headersRaw = DosHeaderSize + 4 + 20 + optSize + 40 * sections.Count;
        var sizeOfHeaders = AlignUp((uint)headersRaw, FileAlignment);

        // File layout of section raw data.
        var fileOffsets = new List<uint>();
        var offset = sizeOfHeaders;
        foreach (var s in sections)
        {
            fileOffsets.Add(offset);
            offset += AlignUp((uint)s.Bytes.Length, FileAlignment);
        }
        var file = new byte[offset];
        var span = file.AsSpan();

        // DOS header and stub.
        DosStub.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], DosHeaderSize);

        var pe = DosHeaderSize;
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(span[pe..]);
        var coff = pe + 4;
        BinaryPrimitives.WriteUInt16LittleEndian(span[coff..], (ushort)Architecture);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(coff + 2)..], (ushort)sections.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(coff + 4)..], DeterministicTimestamp(sections));
        BinaryPrimitives.WriteUInt16LittleEndian(span[(coff + 16)..], (ushort)optSize);
        ushort characteristics = 0x0002 /*EXECUTABLE*/ | 0x2000 /*DLL*/;
        characteristics |= Is64 ? (ushort)0x0020 /*LARGE_ADDRESS_AWARE*/ : (ushort)0x0100 /*32BIT_MACHINE*/;
        BinaryPrimitives.WriteUInt16LittleEndian(span[(coff + 18)..], characteristics);

        var opt = coff + 20;
        uint sizeOfCode = 0, sizeOfInitData = 0;
        for (var i = 0; i < sections.Count; i++)
        {
            var raw = AlignUp((uint)sections[i].Bytes.Length, FileAlignment);
            if ((sections[i].Characteristics & ScnCode) != 0) sizeOfCode += raw;
            else sizeOfInitData += raw;
        }
        var last = sections[^1];
        var sizeOfImage = last.Rva + AlignUp((uint)Math.Max(1, last.Bytes.Length), SectionAlignment);
        var textRva = sections[0].Rva;
        var dataRva = sections[1].Rva;

        BinaryPrimitives.WriteUInt16LittleEndian(span[opt..], Is64 ? (ushort)0x20B : (ushort)0x10B);
        span[opt + 2] = 14; // linker version 14.0
        span[opt + 3] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 4)..], sizeOfCode);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 8)..], sizeOfInitData);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 12)..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 16)..], 0); // no entry point (/NOENTRY)
        BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 20)..], textRva);
        if (Is64)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span[(opt + 24)..], ImageBase);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 24)..], dataRva);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 28)..], (uint)ImageBase);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 32)..], SectionAlignment);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 36)..], FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(opt + 40)..], 6); // OS version 6.0
        BinaryPrimitives.WriteUInt16LittleEndian(span[(opt + 42)..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(opt + 44)..], ImageVersionMajor);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(opt + 46)..], ImageVersionMinor);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(opt + 48)..], 6); // subsystem version 6.0
        BinaryPrimitives.WriteUInt16LittleEndian(span[(opt + 50)..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 56)..], sizeOfImage);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 60)..], sizeOfHeaders);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(opt + 68)..], Subsystem);
        ushort dllCharacteristics = 0x0040 /*DYNAMIC_BASE*/ | 0x0100 /*NX_COMPAT*/ | 0x0400 /*NO_SEH*/;
        if (Is64) dllCharacteristics |= 0x0020; /*HIGH_ENTROPY_VA*/
        BinaryPrimitives.WriteUInt16LittleEndian(span[(opt + 70)..], dllCharacteristics);

        int dirs;
        if (Is64)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span[(opt + 72)..], 0x100000);
            BinaryPrimitives.WriteUInt64LittleEndian(span[(opt + 80)..], 0x1000);
            BinaryPrimitives.WriteUInt64LittleEndian(span[(opt + 88)..], 0x100000);
            BinaryPrimitives.WriteUInt64LittleEndian(span[(opt + 96)..], 0x1000);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 108)..], 16);
            dirs = opt + 112;
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 72)..], 0x100000);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 76)..], 0x1000);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 80)..], 0x100000);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 84)..], 0x1000);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(opt + 92)..], 16);
            dirs = opt + 96;
        }
        WriteDirectory(file, dirs, 0, exportRva, exportSize);
        if (rsrcSize > 0) WriteDirectory(file, dirs, 2, rsrcRva, rsrcSize);
        if (relocSize > 0) WriteDirectory(file, dirs, 5, relocRva, relocSize);

        // Section headers and raw data.
        var sh = opt + optSize;
        for (var i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            var h = sh + i * 40;
            var name = Encoding.ASCII.GetBytes(s.Name);
            name.AsSpan(0, Math.Min(8, name.Length)).CopyTo(span[h..]);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(h + 8)..], (uint)s.Bytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(h + 12)..], s.Rva);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(h + 16)..], AlignUp((uint)s.Bytes.Length, FileAlignment));
            BinaryPrimitives.WriteUInt32LittleEndian(span[(h + 20)..], fileOffsets[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(h + 36)..], s.Characteristics);
            s.Bytes.CopyTo(span[(int)fileOffsets[i]..]);
        }

        var checksumOffset = opt + 64;
        BinaryPrimitives.WriteUInt32LittleEndian(span[checksumOffset..], ComputeChecksum(file, checksumOffset));
        return file;
    }

    private static void WriteDirectory(byte[] file, int dirs, int index, uint rva, uint size)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(dirs + index * 8), rva);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(dirs + index * 8 + 4), size);
    }

    /// <summary>The PE checksum (same algorithm as imagehlp's CheckSumMappedFile).</summary>
    internal static uint ComputeChecksum(byte[] file, int checksumOffset)
    {
        ulong sum = 0;
        for (var i = 0; i < file.Length; i += 2)
        {
            if (i == checksumOffset || i == checksumOffset + 2) continue;
            uint word = file[i];
            if (i + 1 < file.Length) word |= (uint)file[i + 1] << 8;
            sum += word;
            sum = (sum & 0xFFFF) + (sum >> 16);
        }
        sum = (sum & 0xFFFF) + (sum >> 16);
        return (uint)(sum + (ulong)file.Length);
    }

    /// <summary>A stable stand-in for the link time, derived from the content (like the linker's /Brepro).</summary>
    private static uint DeterministicTimestamp(List<(string Name, uint Rva, byte[] Bytes, uint Characteristics)> sections)
    {
        uint h = 2166136261;
        foreach (var s in sections)
        {
            foreach (var b in s.Bytes)
            {
                h ^= b;
                h *= 16777619;
            }
        }
        return h & 0x7FFFFFFF;
    }

    private static uint AlignUp(uint value, uint alignment) => (value + alignment - 1) / alignment * alignment;

    // Standard MS-DOS header and "This program cannot be run in DOS mode." stub.
    private static ReadOnlySpan<byte> DosStub =>
    [
        0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00,
        0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00,
        0x0E, 0x1F, 0xBA, 0x0E, 0x00, 0xB4, 0x09, 0xCD, 0x21, 0xB8, 0x01, 0x4C, 0xCD, 0x21, 0x54, 0x68,
        0x69, 0x73, 0x20, 0x70, 0x72, 0x6F, 0x67, 0x72, 0x61, 0x6D, 0x20, 0x63, 0x61, 0x6E, 0x6E, 0x6F,
        0x74, 0x20, 0x62, 0x65, 0x20, 0x72, 0x75, 0x6E, 0x20, 0x69, 0x6E, 0x20, 0x44, 0x4F, 0x53, 0x20,
        0x6D, 0x6F, 0x64, 0x65, 0x2E, 0x0D, 0x0D, 0x0A, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];
}
