using Keyservant.Core.Formats;
using Keyservant.Core.Model;
using Keyservant.Core.Templates;

namespace Keyservant.Tests;

/// <summary>Layouts used across tests.</summary>
internal static class TestLayouts
{
    public static string SamplesDirectory => Path.Combine(AppContext.BaseDirectory, "Samples");

    public static KeyboardLayout Sample(string name) =>
        KeyservantFormat.Parse(File.ReadAllText(Path.Combine(SamplesDirectory, name + ".keyservant")));

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
}
