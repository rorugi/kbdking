using System.Buffers.Binary;
using Keyservant.Binary;
using Keyservant.Binary.Pe;
using Keyservant.Core.Formats;
using Keyservant.Core.Model;
using Keyservant.Core.Templates;

namespace Keyservant.Tests;

public class LayoutDllTests
{
    private static string Normalize(KeyboardLayout layout, LayoutMetadata metadata)
    {
        layout.Metadata = metadata.Clone();
        return KeyservantFormat.Serialize(layout);
    }

    public static IEnumerable<object[]> Architectures => [[LayoutArchitecture.X64], [LayoutArchitecture.X86Wow64], [LayoutArchitecture.Arm64]];

    [Theory]
    [MemberData(nameof(TestLayouts.Templates), MemberType = typeof(TestLayouts))]
    public void Templates_round_trip_through_both_dlls(string templateId)
    {
        var layout = LayoutTemplates.Create(templateId);
        var result = LayoutDllBuilder.Build(layout);
        var expected = KeyservantFormat.Serialize(layout);
        Assert.Equal(expected, Normalize(LayoutDllReader.Read(result.X64), layout.Metadata));
        Assert.Equal(expected, Normalize(LayoutDllReader.Read(result.Wow64!), layout.Metadata));
    }

    [Theory]
    [MemberData(nameof(Architectures))]
    public void Every_table_feature_round_trips(LayoutArchitecture arch)
    {
        var layout = TestLayouts.Everything();
        var dll = LayoutDllBuilder.BuildImage(layout, arch);
        var report = new ConversionReport();
        var back = LayoutDllReader.Read(dll, report);
        Assert.Empty(report.Warnings);
        Assert.Equal(KeyservantFormat.Serialize(layout), Normalize(back, layout.Metadata));
    }

    [Fact]
    public void Sample_layout_round_trips()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var result = LayoutDllBuilder.Build(layout);
        Assert.Equal(KeyservantFormat.Serialize(layout), Normalize(LayoutDllReader.Read(result.X64), layout.Metadata));
    }

    [Fact]
    public void Builds_are_deterministic()
    {
        var a = LayoutDllBuilder.Build(TestLayouts.Everything());
        var b = LayoutDllBuilder.Build(TestLayouts.Everything());
        Assert.Equal(a.X64, b.X64);
        Assert.Equal(a.Wow64, b.Wow64);
    }

    [Theory]
    [MemberData(nameof(Architectures))]
    public void Pe_headers_are_consistent(LayoutArchitecture arch)
    {
        var dll = LayoutDllBuilder.BuildImage(TestLayouts.Everything(), arch, "kbdtest.dll");
        var pe = PeImage.Parse(dll);

        Assert.Equal((ushort)arch, pe.Machine);
        Assert.Equal(arch != LayoutArchitecture.X86Wow64, pe.IsPe32Plus);
        Assert.Equal(PeImageWriter.ComputeChecksum(dll, pe.ChecksumOffset), pe.Checksum);
        Assert.Equal("kbdtest.dll", pe.ExportDllName());
        Assert.NotNull(pe.FindSection(".data"));
        Assert.NotNull(pe.FindSection(".rsrc"));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(dll.AsSpan(0x80 + 4 + 20 + 16))); // no entry point

        // Sections are file- and section-aligned and do not overlap.
        uint lastEnd = 0;
        foreach (var s in pe.Sections)
        {
            Assert.Equal(0u, s.VirtualAddress % 0x1000);
            Assert.Equal(0u, s.RawPointer % 0x200);
            Assert.True(s.VirtualAddress >= lastEnd);
            lastEnd = s.VirtualAddress + s.VirtualSize;
        }
    }

    [Theory]
    [MemberData(nameof(Architectures))]
    public void Kbdtables_is_in_the_data_section_and_every_pointer_is_relocated(LayoutArchitecture arch)
    {
        var dll = LayoutDllBuilder.BuildImage(TestLayouts.Everything(), arch);
        var pe = PeImage.Parse(dll);
        var tables = LayoutDllReader.DecodeDescriptor(pe, pe.FindExport("KbdLayerDescriptor"));
        var data = pe.FindSection(".data")!;
        Assert.InRange(tables, data.VirtualAddress, data.VirtualAddress + data.VirtualSize - 1);

        var relocs = pe.ReadRelocations();
        Assert.All(relocs, r => Assert.Equal(arch == LayoutArchitecture.X86Wow64 ? 3 : 10, r.Type));
        // Each relocated slot holds an address inside the image, pointing into .data (or .text for nothing).
        foreach (var (rva, _) in relocs)
        {
            var value = arch == LayoutArchitecture.X86Wow64 ? pe.ReadUInt32(rva) : pe.ReadUInt64(rva);
            var target = pe.VaToRva(value)!.Value;
            Assert.Equal(".data", pe.SectionOf(target)!.Name);
        }
        // The first seven KBDTABLES pointers that are set must all have relocations.
        var relocated = relocs.Select(r => r.Rva).ToHashSet();
        for (var i = 0; i < 7; i++)
        {
            var slot = tables + (uint)(8 * i);
            if (pe.ReadUInt64(slot) != 0) Assert.Contains(slot, relocated);
        }
    }

    [Fact]
    public void Wow64_tables_use_64_bit_pointers_with_zero_high_dword()
    {
        var dll = LayoutDllBuilder.BuildImage(LayoutTemplates.German(), LayoutArchitecture.X86Wow64);
        var pe = PeImage.Parse(dll);
        var tables = LayoutDllReader.DecodeDescriptor(pe, pe.FindExport("KbdLayerDescriptor"));
        var modifiers = pe.ReadUInt64(tables);
        Assert.Equal(0u, (uint)(modifiers >> 32));
        Assert.NotNull(pe.VaToRva(modifiers));
    }

    [Fact]
    public void Validation_errors_stop_the_build()
    {
        var layout = LayoutTemplates.UnitedStates();
        layout.Metadata.DllName = "not valid!";
        var ex = Assert.Throws<LayoutBuildException>(() => LayoutDllBuilder.Build(layout));
        Assert.Contains("KS002", ex.Message);
    }

    [Fact]
    public void Display_name_resource_points_at_string_1000()
    {
        var result = LayoutDllBuilder.Build(LayoutTemplates.UnitedStates());
        Assert.Equal(@"@%SystemRoot%\system32\kbdks.dll,-1000", result.DisplayNameResource);
    }

    [Fact]
    public void Reading_random_corruptions_never_crashes_with_unexpected_exceptions()
    {
        var original = LayoutDllBuilder.BuildImage(TestLayouts.Everything(), LayoutArchitecture.X64);
        var random = new Random(1234);
        for (var i = 0; i < 300; i++)
        {
            var copy = (byte[])original.Clone();
            for (var j = 0; j < 8; j++) copy[random.Next(copy.Length)] = (byte)random.Next(256);
            if (i % 10 == 0) Array.Resize(ref copy, random.Next(copy.Length));
            try
            {
                LayoutDllReader.Read(copy);
            }
            catch (InvalidLayoutDllException)
            {
            }
            catch (FormatException)
            {
                // e.g. a corrupted dead key name; acceptable, still a controlled failure
            }
        }
    }

    [Fact]
    public void Non_dll_files_are_rejected_clearly()
    {
        var ex = Assert.Throws<InvalidLayoutDllException>(() => LayoutDllReader.Read("hello"u8.ToArray()));
        Assert.Contains("MZ", ex.Message);
    }

    [Fact]
    public void C_source_contains_every_table()
    {
        var c = Binary.Kbd.KbdCSourceWriter.Write(TestLayouts.Everything());
        foreach (var name in new[] { "aVkToBits", "CharModifiers", "aVkToWch", "aVkToWchNumpad", "aLigature", "aDeadKey", "aKeyNames", "aKeyNamesDead", "ausVK", "aE0VscToVk", "KbdTables", "KbdLayerDescriptor" })
            Assert.Contains(name, c);
    }
}
