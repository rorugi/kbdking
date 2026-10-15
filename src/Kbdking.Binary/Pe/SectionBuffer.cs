using System.Buffers.Binary;
using System.Text;

namespace Kbdking.Binary.Pe;

/// <summary>A named position inside a <see cref="SectionBuffer"/>, resolved to an address when the image is laid out.</summary>
internal sealed class Symbol(string name)
{
    public string Name { get; } = name;
    public SectionBuffer? Section { get; set; }
    public int Offset { get; set; } = -1;

    public bool IsDefined => Section is not null && Offset >= 0;

    public override string ToString() => $"{Name}@{Offset}";
}

/// <summary>A pointer-sized slot that must hold the virtual address of a symbol (and gets a base relocation).</summary>
internal readonly record struct PointerFixup(int Offset, Symbol Target, int Size);

/// <summary>
/// Little-endian byte buffer for one PE section. Pointers to symbols are recorded as fixups and filled in
/// once section addresses are known.
/// </summary>
internal sealed class SectionBuffer(string name)
{
    private readonly MemoryStream _data = new();
    private readonly List<PointerFixup> _fixups = [];

    public string Name { get; } = name;

    public int Length => (int)_data.Length;

    public IReadOnlyList<PointerFixup> Fixups => _fixups;

    /// <summary>Relative virtual address, assigned by the image writer.</summary>
    public uint Rva { get; set; }

    public void Align(int alignment)
    {
        while (_data.Length % alignment != 0) _data.WriteByte(0);
    }

    public Symbol Define(string name, int alignment = 1)
    {
        Align(alignment);
        return new Symbol(name) { Section = this, Offset = Length };
    }

    public void Define(Symbol symbol, int alignment = 1)
    {
        if (symbol.IsDefined) throw new InvalidOperationException($"Symbol {symbol.Name} is already defined.");
        Align(alignment);
        symbol.Section = this;
        symbol.Offset = Length;
    }

    public void WriteByte(byte value) => _data.WriteByte(value);

    public void WriteUInt16(ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, value);
        _data.Write(b);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        _data.Write(b);
    }

    public void WriteUInt64(ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, value);
        _data.Write(b);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes) => _data.Write(bytes);

    public void WriteZeros(int count)
    {
        for (var i = 0; i < count; i++) _data.WriteByte(0);
    }

    /// <summary>Writes a zero-terminated UTF-16LE string.</summary>
    public void WriteWideString(string s)
    {
        _data.Write(Encoding.Unicode.GetBytes(s));
        WriteUInt16(0);
    }

    public void WriteAsciiString(string s)
    {
        _data.Write(Encoding.ASCII.GetBytes(s));
        _data.WriteByte(0);
    }

    /// <summary>
    /// Writes a pointer slot of <paramref name="size"/> bytes (8 for 64-bit tables, also in the WOW64 DLL).
    /// A null target writes zero and needs no relocation.
    /// </summary>
    public void WritePointer(Symbol? target, int size)
    {
        if (target is not null) _fixups.Add(new PointerFixup(Length, target, size));
        WriteZeros(size);
    }

    /// <summary>Records a fixup at an already written position (used for code that embeds an absolute address).</summary>
    public void AddFixup(int offset, Symbol target, int size) => _fixups.Add(new PointerFixup(offset, target, size));

    public byte[] ToArray() => _data.ToArray();
}
