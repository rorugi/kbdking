using Kbdking.Core.Formats;
using Kbdking.Core.Model;
using Kbdking.Core.Templates;

namespace Kbdking.Tests;

/// <summary>Layouts used across tests.</summary>
internal static class TestLayouts
{
    public static string SamplesDirectory => Path.Combine(AppContext.BaseDirectory, "Samples");

    public static KeyboardLayout Sample(string name) =>
        KbdkingFormat.Parse(File.ReadAllText(Path.Combine(SamplesDirectory, name + ".kbdking")));

    public static IEnumerable<object[]> Templates => LayoutTemplates.All.Select(t => new object[] { t.Id });

    /// <summary>
    /// A layout exercising every table feature: ligatures (including a non-BMP character), SGCAPS,
    /// Caps Lock with AltGr, chained dead keys, dead-key names and a Kana-style extra column.
    /// </summary>
    public static KeyboardLayout Everything()
    {
        var l = LayoutTemplates.German("de-AT");
        l.Metadata.Name = "Everything";
        l.Metadata.DllName = "kbdtest";
        l.SetOutput((byte)'A', ShiftState.AltGr, KeyOutput.Of("ā"));
        l.SetOutput((byte)'A', ShiftState.ShiftAltGr, KeyOutput.Of("Ā"));
        l.SetOutput((byte)'X', ShiftState.AltGr, KeyOutput.Of("ks"));         // ligature
        l.SetOutput((byte)'D', ShiftState.AltGr, KeyOutput.Of("𝔻"));          // surrogate pair → ligature
        l.SetOutput((byte)'J', ShiftState.AltGr, KeyOutput.Of("j̃"));    // base + combining mark
        l.SetOutput(0xBE, ShiftState.AltGr, KeyOutput.Dead('̣'));
        l.SetOutput(0xBD, ShiftState.AltGr, KeyOutput.Dead('̄'));
        var dot = l.GetOrAddDeadKey('̣');
        dot.Name = "DOT BELOW";
        dot.Set('t', 'ṭ');
        dot.Set('̄', 'ṝ', chained: true);
        var macron = l.GetOrAddDeadKey('̄');
        macron.Set('a', 'ā');
        var combined = l.GetOrAddDeadKey('ṝ');
        combined.Set('r', 'ṝ');
        combined.Set('l', 'ḹ');

        // Swiss-style SGCAPS key: different characters with Caps Lock.
        var oem1 = l.GetOrAddKey(0xBA);
        oem1.CapsOutputs = new SortedDictionary<ShiftState, KeyOutput> { [ShiftState.Base] = KeyOutput.Of("Ü"), [ShiftState.Shift] = KeyOutput.Of("ü") };
        oem1.Attributes &= ~KeyAttributes.CapsLock;

        l.FindKey((byte)'A')!.Attributes |= KeyAttributes.CapsLockAltGr;
        l.Flags |= LocaleFlags.ShiftLock;
        return l;
    }

    /// <summary>US Colemak built the Windows way: every key sends the virtual key of the letter it types.</summary>
    public static KeyboardLayout Colemak()
    {
        var l = LayoutTemplates.Create("en-US");
        l.Metadata.Name = "Colemak";
        l.Metadata.DllName = "kbdcolem";
        string[] rows = ["QWFPGJLUY\u00BA", "ARSTDHNEIO", "ZXCVBKM"];
        int[] starts = [0x10, 0x1E, 0x2C];
        for (var r = 0; r < rows.Length; r++)
            for (var i = 0; i < rows[r].Length; i++)
                l.ScanCodes.Set(starts[r] + i, new ScanCodeTarget((byte)rows[r][i], ScanCodeFlags.None));
        return l;
    }

    /// <summary>German with Caps Lock as a Layer A key that types Greek, and the &lt;&gt; key as Layer B.</summary>
    public static KeyboardLayout Layered()
    {
        var l = LayoutTemplates.German("de-AT");
        l.Metadata.Name = "Layered";
        l.Metadata.DllName = "kbdlayer";
        Core.Editing.LayerKeys.Add(l, 0x3A, false, ModifierBits.LayerA);
        Core.Editing.LayerKeys.Add(l, 0x56, false, ModifierBits.LayerB, withShift: false);
        var a = new ShiftState(ModifierBits.LayerA);
        var sa = new ShiftState(ModifierBits.LayerA | ModifierBits.Shift);
        l.SetOutput((byte)'A', a, KeyOutput.Of("α"));
        l.SetOutput((byte)'A', sa, KeyOutput.Of("Α"));
        l.SetOutput((byte)'B', a, KeyOutput.Of("β"));
        l.SetOutput((byte)'G', a, KeyOutput.Of("γ"));
        l.SetOutput((byte)'R', new ShiftState(ModifierBits.LayerB), KeyOutput.Of("√"));
        l.SetOutput((byte)'E', new ShiftState(ModifierBits.LayerB), KeyOutput.Dead('\u0304'));
        l.GetOrAddDeadKey('\u0304').Set('a', 'ā');
        return l;
    }
}
