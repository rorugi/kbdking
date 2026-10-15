using System.Text;
using Kbdking.Core.Formats;
using Kbdking.Core.Model;
using Kbdking.Core.Templates;

namespace Kbdking.Tests;

public class KlcFormatTests
{
    // Shaped like MSKLC 1.4 output, with an SGCap key, a ligature and a chained dead key.
    private const string Sample = """
        KBD	kbdtst	"Test Layout"

        COPYRIGHT	"(c) 2026 Test"

        COMPANY	"Test"

        LOCALENAME	"de-AT"

        LOCALEID	"00000c07"

        VERSION	1.2

        SHIFTSTATE

        0	//Column 4
        1	//Column 5 : Shft
        2	//Column 6 :       Ctrl
        6	//Column 7 :       Ctrl Alt
        7	//Column 8 : Shft  Ctrl Alt

        LAYOUT		;an extra '@' at the end is a dead key

        //SC	VK_		Cap	0	1	2	6	7
        //--	----		----	----	----	----	----

        02	1		0	1	0021	-1	-1	-1		// DIGIT ONE, EXCLAMATION MARK
        10	Q		1	q	Q	-1	0040	-1		// q, Q, @
        1e	A		5	a	A	-1	0101	0100		// a, A, ā, Ā
        2d	X		1	x	X	-1	%%	-1
        1a	OEM_1		SGCap	00fc	00dc	001b	-1	-1
        -1	-1		0	00dc	00fc
        34	OEM_PERIOD		0	002e	003a	-1	0323@	-1
        35	OEM_MINUS		0	002d	005f	-1	0304@	-1
        39	SPACE		0	0020	0020	0020	-1	-1
        53	DECIMAL		0	002c	002c	-1	-1	-1

        LIGATURE

        //VK_	Mod#	Char0	Char1	Char2	Char3
        //----		----	----	----	----	----

        X	3	006b	0073	// k s

        DEADKEY	0323

        0074	1e6d	// t -> ṭ
        0304	1e5d@	// macron -> ṝ (chained)

        DEADKEY	0304

        0061	0101	// a -> ā

        DEADKEY	1e5d

        0072	1e5d	// r -> ṝ

        KEYNAME

        01	Esc
        39	Leer

        KEYNAME_EXT

        1c	"Num Enter"

        KEYNAME_DEAD

        0323	"DOT BELOW"

        DESCRIPTIONS

        0409	Test Layout

        LANGUAGENAMES

        0409	German (Austria)

        ENDKBD
        """;

    [Fact]
    public void Parses_an_msklc_file()
    {
        var report = new ConversionReport();
        var l = KlcFormat.Parse(Sample, report);

        Assert.Empty(report.Warnings);
        Assert.Equal("Test Layout", l.Metadata.Name);
        Assert.Equal("kbdtst", l.Metadata.DllName);
        Assert.Equal("de-AT", l.Metadata.Locale);
        Assert.Equal("1.2", l.Metadata.Version);
        Assert.True(l.Flags.HasFlag(LocaleFlags.AltGr));

        var a = l.FindKey((byte)'A')!;
        Assert.Equal(KeyAttributes.CapsLock | KeyAttributes.CapsLockAltGr, a.Attributes);
        Assert.Equal("ā", a[ShiftState.AltGr]!.Text);
        Assert.Equal("Ā", a[ShiftState.ShiftAltGr]!.Text);

        Assert.Equal("ks", l.FindKey((byte)'X')![ShiftState.AltGr]!.Text);

        var oem1 = l.FindKey(0xBA)!;
        Assert.NotNull(oem1.CapsOutputs);
        Assert.Equal("Ü", oem1.CapsOutputs![ShiftState.Base].Text);

        var dot = l.FindDeadKey('̣')!;
        Assert.Equal("DOT BELOW", dot.Name);
        Assert.Equal(new DeadKeyEntry('̄', 'ṝ', true), dot.Find('̄'));
        Assert.Equal("Leer", l.KeyNames[0x39]);
        Assert.Equal("Num Enter", l.KeyNamesExt[0x1C]);
    }

    [Fact]
    public void Keeps_control_keys_and_numpad_from_the_template()
    {
        var l = KlcFormat.Parse(Sample, new ConversionReport());
        Assert.Equal("\r", l.FindKey(VirtualKeys.Return)![ShiftState.Base]!.Text);
        Assert.Equal("7", l.FindKey(0x67)![ShiftState.Base]!.Text);
        // The numpad decimal key keeps its navigation VK so Num Lock still works.
        Assert.Equal(0x2E, l.ScanCodes[0x53].VirtualKey);
        Assert.Equal(",", l.FindKey(VirtualKeys.Decimal)![ShiftState.Base]!.Text);
    }

    [Fact]
    public void Imported_layout_is_valid_and_builds()
    {
        var l = KlcFormat.Parse(Sample, new ConversionReport());
        var result = Binary.LayoutDllBuilder.Build(l);
        Assert.False(result.Validation.HasErrors);
    }

    [Fact]
    public void German_template_survives_a_klc_round_trip()
    {
        var original = LayoutTemplates.German("de-AT");
        var report = new ConversionReport();
        var klc = KlcFormat.Serialize(original, report);
        Assert.Empty(report.Warnings);

        var back = KlcFormat.Parse(klc, new ConversionReport());
        foreach (var key in original.Keys)
        {
            var other = back.FindKey(key.VirtualKey);
            Assert.NotNull(other);
            Assert.Equal(key.Attributes, other!.Attributes);
            foreach (var (state, output) in key.Outputs) Assert.Equal(output, other[state]);
        }
        foreach (var d in original.DeadKeys)
        {
            var other = back.FindDeadKey(d.Accent)!;
            Assert.Equal(d.Name, other.Name);
            Assert.Equal(d.Entries, other.Entries);
        }
        Assert.Equal(original.KeyNames.ToList(), back.KeyNames.ToList());
    }

    [Fact]
    public void Writes_utf16_with_bom()
    {
        using var ms = new MemoryStream();
        new KlcFormat().Write(LayoutTemplates.UnitedStates(), ms, new ConversionReport());
        var bytes = ms.ToArray();
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
        Assert.StartsWith("KBD\t", Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
    }

    [Fact]
    public void Reads_utf16_files()
    {
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(Sample)).ToArray();
        var l = new KlcFormat().Read(new MemoryStream(bytes), new ConversionReport());
        Assert.Equal("Test Layout", l.Metadata.Name);
    }

    [Fact]
    public void Ligature_without_entry_is_an_error()
    {
        var broken = Sample.Replace("X\t3\t006b\t0073\t// k s", "");
        Assert.Throws<LayoutFormatException>(() => KlcFormat.Parse(broken, new ConversionReport()));
    }

    [Fact]
    public void Errors_name_the_line()
    {
        var broken = Sample.Replace("10\tQ\t\t1", "10\tNOTAKEY\t\t1");
        var ex = Assert.Throws<LayoutFormatException>(() => KlcFormat.Parse(broken, new ConversionReport()));
        Assert.Contains("Line", ex.Message);
        Assert.Contains("NOTAKEY", ex.Message);
    }
}
