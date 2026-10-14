using System.Buffers.Binary;
using System.Text;

namespace Keyservant.Binary.Pe;

/// <summary>A PE file is malformed or not what was expected.</summary>
public sealed class InvalidLayoutDllException(string message) : Exception(message);

/// <summary>
/// A read-only view of a PE file on disk (not mapped or executed). Every read is bounds-checked,
/// so untrusted files can be inspected safely.
/// </summary>
internal sealed class PeImage
{
    private readonly byte[] _file;

    private PeImage(byte[] file) => _file = file;

    public ushort Machine { get; private set; }
    public bool IsPe32Plus { get; private set; }
    public ulong ImageBase { get; private set; }
    public uint SizeOfImage { get; private set; }
    public IReadOnlyList<PeSection> Sections { get; private set; } = [];
    public (uint Rva, uint Size) ExportDirectory { get; private set; }
    public (uint Rva, uint Size) ResourceDirectory { get; private set; }
    public (uint Rva, uint Size) RelocationDirectory { get; private set; }
    public int ChecksumOffset { get; private set; }
    public uint Checksum { get; private set; }

    public static PeImage Parse(byte[] file)
    {
        var pe = new PeImage(file);
        pe.ParseHeaders();
        return pe;
    }

    private void ParseHeaders()
    {
        if (_file.Length < 0x40 || _file[0] != 'M' || _file[1] != 'Z') throw new InvalidLayoutDllException("Not a Windows DLL (missing MZ header).");
        var peOffset = ReadFileInt32(0x3C);
        if (peOffset < 0 || peOffset > _file.Length - 24 || ReadFileUInt32(peOffset) != 0x00004550)
            throw new InvalidLayoutDllException("Not a Windows DLL (missing PE header).");
        var coff = peOffset + 4;
        Machine = ReadFileUInt16(coff);
        var sectionCount = ReadFileUInt16(coff + 2);
        var optSize = ReadFileUInt16(coff + 16);
        var opt = coff + 20;
        var magic = ReadFileUInt16(opt);
        IsPe32Plus = magic switch
        {
            0x20B => true,
            0x10B => false,
            _ => throw new InvalidLayoutDllException($"Unknown optional header magic 0x{magic:X}."),
        };
        ImageBase = IsPe32Plus ? ReadFileUInt64(opt + 24) : ReadFileUInt32(opt + 28);
        SizeOfImage = ReadFileUInt32(opt + 56);
        ChecksumOffset = opt + 64;
        Checksum = ReadFileUInt32(opt + 64);
        var dirCount = ReadFileUInt32(opt + (IsPe32Plus ? 108 : 92));
        var dirs = opt + (IsPe32Plus ? 112 : 96);
        (uint, uint) Dir(int i) => i < dirCount ? (ReadFileUInt32(dirs + i * 8), ReadFileUInt32(dirs + i * 8 + 4)) : (0, 0);
        ExportDirectory = Dir(0);
        ResourceDirectory = Dir(2);
        RelocationDirectory = Dir(5);

        var sh = opt + optSize;
        if (sectionCount > 96) throw new InvalidLayoutDllException("Too many sections.");
        var sections = new List<PeSection>();
        for (var i = 0; i < sectionCount; i++)
        {
            var h = sh + i * 40;
            CheckFileRange(h, 40);
            var nameBytes = _file.AsSpan(h, 8);
            var len = nameBytes.IndexOf((byte)0);
            var name = Encoding.ASCII.GetString(len >= 0 ? nameBytes[..len] : nameBytes);
            sections.Add(new PeSection(name, ReadFileUInt32(h + 12), ReadFileUInt32(h + 8),
                ReadFileUInt32(h + 20), ReadFileUInt32(h + 16), ReadFileUInt32(h + 36)));
        }
        Sections = sections;
    }

    public PeSection? FindSection(string name) => Sections.FirstOrDefault(s => s.Name == name);

    public PeSection? SectionOf(uint rva) =>
        Sections.FirstOrDefault(s => rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.RawSize));

    /// <summary>File offset of an RVA, or throws if the RVA has no file data.</summary>
    public int RvaToOffset(uint rva, int length = 1)
    {
        var s = SectionOf(rva) ?? throw new InvalidLayoutDllException($"Address 0x{rva:X} is outside every section.");
        var delta = rva - s.VirtualAddress;
        if (delta + (uint)length > s.RawSize) throw new InvalidLayoutDllException($"Address 0x{rva:X} points past the section's data.");
        var offset = (long)s.RawPointer + delta;
        CheckFileRange(offset, length);
        return (int)offset;
    }

    public ReadOnlySpan<byte> Read(uint rva, int length) => _file.AsSpan(RvaToOffset(rva, length), length);

    public byte ReadByte(uint rva) => _file[RvaToOffset(rva)];
    public ushort ReadUInt16(uint rva) => BinaryPrimitives.ReadUInt16LittleEndian(Read(rva, 2));
    public uint ReadUInt32(uint rva) => BinaryPrimitives.ReadUInt32LittleEndian(Read(rva, 4));
    public ulong ReadUInt64(uint rva) => BinaryPrimitives.ReadUInt64LittleEndian(Read(rva, 8));

    /// <summary>Converts a stored (unrelocated) virtual address to an RVA; null for a null pointer.</summary>
    public uint? VaToRva(ulong va)
    {
        if (va == 0) return null;
        if (va < ImageBase || va - ImageBase >= SizeOfImage) throw new InvalidLayoutDllException($"Pointer 0x{va:X} is outside the image.");
        return (uint)(va - ImageBase);
    }

    public bool IsValidVa(ulong va) => va == 0 || va >= ImageBase && va - ImageBase < SizeOfImage && SectionOf((uint)(va - ImageBase)) is not null;

    public string ReadAsciiZ(uint rva, int max = 256)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < max; i++)
        {
            var b = ReadByte(rva + (uint)i);
            if (b == 0) return sb.ToString();
            sb.Append((char)b);
        }
        throw new InvalidLayoutDllException("String is not terminated.");
    }

    public string ReadWideZ(uint rva, int maxChars = 1024)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < maxChars; i++)
        {
            var c = (char)ReadUInt16(rva + (uint)(2 * i));
            if (c == '\0') return sb.ToString();
            sb.Append(c);
        }
        throw new InvalidLayoutDllException("String is not terminated.");
    }

    /// <summary>Returns the RVA of an exported function by name.</summary>
    public uint FindExport(string name)
    {
        var (rva, size) = ExportDirectory;
        if (rva == 0 || size == 0) throw new InvalidLayoutDllException("The DLL has no exports, so it is not a keyboard layout.");
        var count = ReadUInt32(rva + 24);
        var functions = ReadUInt32(rva + 28);
        var names = ReadUInt32(rva + 32);
        var ordinals = ReadUInt32(rva + 36);
        if (count > 4096) throw new InvalidLayoutDllException("Too many exports.");
        for (uint i = 0; i < count; i++)
        {
            if (ReadAsciiZ(ReadUInt32(names + 4 * i)) != name) continue;
            var ordinal = ReadUInt16(ordinals + 2 * i);
            return ReadUInt32(functions + 4u * ordinal);
        }
        throw new InvalidLayoutDllException($"The DLL does not export {name}, so it is not a keyboard layout.");
    }

    /// <summary>The DLL's own file name from the export directory.</summary>
    public string? ExportDllName()
    {
        var (rva, _) = ExportDirectory;
        return rva == 0 ? null : ReadAsciiZ(ReadUInt32(rva + 12));
    }

    /// <summary>All base relocations as (RVA, type).</summary>
    public IReadOnlyList<(uint Rva, int Type)> ReadRelocations()
    {
        var result = new List<(uint, int)>();
        var (rva, size) = RelocationDirectory;
        uint pos = 0;
        while (pos + 8 <= size)
        {
            var page = ReadUInt32(rva + pos);
            var blockSize = ReadUInt32(rva + pos + 4);
            if (blockSize < 8 || pos + blockSize > size) throw new InvalidLayoutDllException("Corrupt relocation table.");
            for (uint i = 8; i + 2 <= blockSize; i += 2)
            {
                var e = ReadUInt16(rva + pos + i);
                var type = e >> 12;
                if (type != 0) result.Add((page + (uint)(e & 0xFFF), type));
            }
            pos += blockSize;
        }
        return result;
    }

    public byte[] Bytes => _file;

    private void CheckFileRange(long offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > _file.Length) throw new InvalidLayoutDllException("The file is truncated.");
    }

    private int ReadFileInt32(int o) { CheckFileRange(o, 4); return BinaryPrimitives.ReadInt32LittleEndian(_file.AsSpan(o)); }
    private uint ReadFileUInt32(int o) { CheckFileRange(o, 4); return BinaryPrimitives.ReadUInt32LittleEndian(_file.AsSpan(o)); }
    private ushort ReadFileUInt16(int o) { CheckFileRange(o, 2); return BinaryPrimitives.ReadUInt16LittleEndian(_file.AsSpan(o)); }
    private ulong ReadFileUInt64(int o) { CheckFileRange(o, 8); return BinaryPrimitives.ReadUInt64LittleEndian(_file.AsSpan(o)); }
}

internal sealed record PeSection(string Name, uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize, uint Characteristics);
