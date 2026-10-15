using System.Text;

namespace Kbdking.Binary.Pe;

/// <summary>Version information written into the VS_VERSIONINFO resource.</summary>
internal sealed record VersionInfo(
    Version FileVersion,
    string FileDescription,
    string ProductName,
    string CompanyName,
    string LegalCopyright,
    string InternalName,
    string OriginalFilename);

/// <summary>
/// Builds a .rsrc section with a string table (the layout's display name, referenced from the registry
/// as <c>@%SystemRoot%\system32\kbdxxx.dll,-1000</c>) and a version resource.
/// </summary>
internal static class ResourceSectionBuilder
{
    public const int DisplayNameStringId = 1000;

    private const int RtString = 6;
    private const int RtVersion = 16;
    private const ushort LangNeutral = 0x0000;
    private const ushort LangEnUs = 0x0409;

    public static byte[] Build(uint sectionRva, string displayName, VersionInfo version)
    {
        var resources = new List<(int Type, int Id, ushort Lang, byte[] Data)>
        {
            (RtString, DisplayNameStringId / 16 + 1, LangNeutral, StringBlock(DisplayNameStringId % 16, displayName)),
            (RtVersion, 1, LangEnUs, VersionResource(version)),
        };
        resources.Sort((a, b) => a.Type.CompareTo(b.Type));

        // Layout: root dir, one type dir per type, one language dir per resource, data entries, data.
        const int DirHeader = 16, Entry = 8, DataEntry = 16;
        var rootSize = DirHeader + Entry * resources.Count;
        var typeDirOffset = rootSize;
        var typeDirSize = DirHeader + Entry;
        var langDirOffset = typeDirOffset + typeDirSize * resources.Count;
        var langDirSize = DirHeader + Entry;
        var dataEntryOffset = langDirOffset + langDirSize * resources.Count;
        var dataOffset = dataEntryOffset + DataEntry * resources.Count;

        var buf = new SectionBuffer(".rsrc");
        void Dir(int idEntries)
        {
            buf.WriteUInt32(0); buf.WriteUInt32(0); buf.WriteUInt16(0); buf.WriteUInt16(0);
            buf.WriteUInt16(0);                // named entries
            buf.WriteUInt16((ushort)idEntries); // ID entries
        }

        // Root: type IDs → type directories.
        Dir(resources.Count);
        for (var i = 0; i < resources.Count; i++)
        {
            buf.WriteUInt32((uint)resources[i].Type);
            buf.WriteUInt32(0x80000000u | (uint)(typeDirOffset + i * typeDirSize));
        }
        // Type directories: resource ID → language directory.
        for (var i = 0; i < resources.Count; i++)
        {
            Dir(1);
            buf.WriteUInt32((uint)resources[i].Id);
            buf.WriteUInt32(0x80000000u | (uint)(langDirOffset + i * langDirSize));
        }
        // Language directories: language → data entry.
        for (var i = 0; i < resources.Count; i++)
        {
            Dir(1);
            buf.WriteUInt32(resources[i].Lang);
            buf.WriteUInt32((uint)(dataEntryOffset + i * DataEntry));
        }
        // Data entries (RVA, size, code page, reserved).
        var offsets = new List<int>();
        var cursor = dataOffset;
        foreach (var r in resources)
        {
            cursor = (cursor + 7) & ~7;
            offsets.Add(cursor);
            cursor += r.Data.Length;
        }
        for (var i = 0; i < resources.Count; i++)
        {
            buf.WriteUInt32(sectionRva + (uint)offsets[i]);
            buf.WriteUInt32((uint)resources[i].Data.Length);
            buf.WriteUInt32(0);
            buf.WriteUInt32(0);
        }
        for (var i = 0; i < resources.Count; i++)
        {
            buf.Align(8);
            if (buf.Length != offsets[i]) throw new InvalidOperationException("Resource layout mismatch.");
            buf.WriteBytes(resources[i].Data);
        }
        return buf.ToArray();
    }

    /// <summary>A string-table block of 16 length-prefixed UTF-16 strings, with one string set.</summary>
    private static byte[] StringBlock(int index, string value)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        for (var i = 0; i < 16; i++)
        {
            if (i == index)
            {
                w.Write((ushort)value.Length);
                w.Write(Encoding.Unicode.GetBytes(value));
            }
            else
            {
                w.Write((ushort)0);
            }
        }
        return ms.ToArray();
    }

    private static byte[] VersionResource(VersionInfo v)
    {
        uint Ms(Version ver) => (uint)(Math.Max(0, ver.Major) << 16 | Math.Max(0, ver.Minor) & 0xFFFF);
        uint Ls(Version ver) => (uint)(Math.Max(0, ver.Build) << 16 | Math.Max(0, ver.Revision) & 0xFFFF);
        var versionText = $"{v.FileVersion.Major}.{v.FileVersion.Minor}.{Math.Max(0, v.FileVersion.Build)}.{Math.Max(0, v.FileVersion.Revision)}";

        var fixedInfo = new byte[52];
        void U32(int offset, uint value) => BitConverter.TryWriteBytes(fixedInfo.AsSpan(offset), value);
        U32(0, 0xFEEF04BD);     // dwSignature
        U32(4, 0x00010000);     // dwStrucVersion
        U32(8, Ms(v.FileVersion));
        U32(12, Ls(v.FileVersion));
        U32(16, Ms(v.FileVersion));
        U32(20, Ls(v.FileVersion));
        U32(24, 0x3F);          // dwFileFlagsMask
        U32(28, 0);             // dwFileFlags
        U32(32, 0x00040004);    // VOS_NT_WINDOWS32
        U32(36, 2);             // VFT_DLL
        U32(40, 0);             // dwFileSubtype
        U32(44, 0);
        U32(48, 0);

        var strings = new (string Key, string Value)[]
        {
            ("CompanyName", v.CompanyName),
            ("FileDescription", v.FileDescription),
            ("FileVersion", versionText),
            ("InternalName", v.InternalName),
            ("LegalCopyright", v.LegalCopyright),
            ("OriginalFilename", v.OriginalFilename),
            ("ProductName", v.ProductName),
            ("ProductVersion", versionText),
        };

        var stringChildren = strings
            .Select(s => Block(s.Key, Encoding.Unicode.GetBytes(s.Value + "\0"), isText: true, valueLength: s.Value.Length + 1, []))
            .ToArray();
        var stringTable = Block("040904B0", [], isText: true, valueLength: 0, stringChildren);
        var stringFileInfo = Block("StringFileInfo", [], isText: true, valueLength: 0, [stringTable]);
        var translation = Block("Translation", BitConverter.GetBytes(0x04B00409u), isText: false, valueLength: 4, []);
        var varFileInfo = Block("VarFileInfo", [], isText: true, valueLength: 0, [translation]);
        return Block("VS_VERSION_INFO", fixedInfo, isText: false, valueLength: 52, [stringFileInfo, varFileInfo]);
    }

    /// <summary>One node of a version resource: header, key, padding, value, padding, children.</summary>
    private static byte[] Block(string key, byte[] value, bool isText, int valueLength, byte[][] children)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);                // wLength, patched below
        w.Write((ushort)valueLength);
        w.Write((ushort)(isText ? 1 : 0)); // wType
        w.Write(Encoding.Unicode.GetBytes(key + "\0"));
        Pad(ms);
        w.Write(value);
        foreach (var child in children)
        {
            Pad(ms);
            w.Write(child);
        }
        var bytes = ms.ToArray();
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 2), (ushort)bytes.Length);
        return bytes;

        static void Pad(MemoryStream s)
        {
            while (s.Length % 4 != 0) s.WriteByte(0);
        }
    }
}
