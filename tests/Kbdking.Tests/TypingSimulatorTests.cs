using Kbdking.Core.Model;
using Kbdking.Core.Templates;

namespace Kbdking.Tests;

public class TypingSimulatorTests
{
    private const byte OemPeriod = 0xBE, OemMinus = 0xBD, Circumflex = 0xDC, Acute = 0xDD;

    private static string Type(TypingSimulator sim, params (byte Vk, ShiftState State)[] keys) =>
        string.Concat(keys.Select(k => sim.Press(k.Vk, k.State)));

    [Fact]
    public void Types_plain_and_shifted_characters()
    {
        var sim = new TypingSimulator(LayoutTemplates.German());
        Assert.Equal("z", sim.Press((byte)'Z', ShiftState.Base));
        Assert.Equal("Z", sim.Press((byte)'Z', ShiftState.Shift));
        Assert.Equal("@", sim.Press((byte)'Q', ShiftState.AltGr));
        Assert.Equal("", sim.Press(0x70 /*F1*/, ShiftState.Base));
    }

    [Fact]
    public void Dead_keys_compose()
    {
        var sim = new TypingSimulator(LayoutTemplates.German());
        Assert.Equal("â", Type(sim, (Circumflex, ShiftState.Base), ((byte)'A', ShiftState.Base)));
        Assert.Equal("É", Type(sim, (Acute, ShiftState.Base), ((byte)'E', ShiftState.Shift)));
        Assert.Equal("^", Type(sim, (Circumflex, ShiftState.Base), (VirtualKeys.Space, ShiftState.Base)));
    }

    [Fact]
    public void Dead_key_without_composition_types_accent_then_character()
    {
        var sim = new TypingSimulator(LayoutTemplates.German());
        Assert.Equal("^x", Type(sim, (Circumflex, ShiftState.Base), ((byte)'X', ShiftState.Base)));
        Assert.Equal("´`", Type(sim, (Acute, ShiftState.Base), (Acute, ShiftState.Shift)));
    }

    [Fact]
    public void Caps_lock_follows_key_attributes()
    {
        var sim = new TypingSimulator(LayoutTemplates.German());
        Assert.Equal("A", sim.Press((byte)'A', ShiftState.Base, capsLock: true));
        Assert.Equal("a", sim.Press((byte)'A', ShiftState.Shift, capsLock: true));
        Assert.Equal("1", sim.Press((byte)'1', ShiftState.Base, capsLock: true)); // digits ignore Caps Lock
    }

    [Fact]
    public void Sgcaps_uses_the_caps_row()
    {
        var sim = new TypingSimulator(TestLayouts.Everything());
        Assert.Equal("ü", sim.Press(0xBA, ShiftState.Base));
        Assert.Equal("Ü", sim.Press(0xBA, ShiftState.Base, capsLock: true));
        Assert.Equal("ü", sim.Press(0xBA, ShiftState.Shift, capsLock: true));
    }

    [Fact]
    public void Caps_lock_altgr_capitalizes_altgr_characters()
    {
        var sim = new TypingSimulator(TestLayouts.Everything());
        Assert.Equal("Ā", sim.Press((byte)'A', ShiftState.AltGr, capsLock: true));
    }

    [Fact]
    public void Ligatures_type_several_characters()
    {
        var sim = new TypingSimulator(TestLayouts.Everything());
        Assert.Equal("ks", sim.Press((byte)'X', ShiftState.AltGr));
        Assert.Equal("𝔻", sim.Press((byte)'D', ShiftState.AltGr));
    }

    [Fact]
    public void Iast_sample_types_sanskrit()
    {
        var sim = new TypingSimulator(TestLayouts.Sample("german-austria-iast"));
        var dot = (OemPeriod, ShiftState.AltGr);
        var macron = (OemMinus, ShiftState.AltGr);
        Assert.Equal("ā", sim.Press((byte)'A', ShiftState.AltGr));
        Assert.Equal("Ś", sim.Press((byte)'S', ShiftState.ShiftAltGr));
        Assert.Equal("ṭ", Type(sim, dot, ((byte)'T', ShiftState.Base)));
        Assert.Equal("Ṇ", Type(sim, dot, ((byte)'N', ShiftState.Shift)));
        // Chained: dot below, then macron, then r → ṝ (and in the other order).
        Assert.Equal("ṝ", Type(sim, dot, macron, ((byte)'R', ShiftState.Base)));
        Assert.Equal("ḹ", Type(sim, macron, dot, ((byte)'L', ShiftState.Base)));
        Assert.Equal("ī", Type(sim, macron, ((byte)'I', ShiftState.Base)));
    }

    [Fact]
    public void Scan_codes_go_through_the_layout_not_the_os()
    {
        var sim = new TypingSimulator(LayoutTemplates.German());
        Assert.Equal("z", sim.PressScanCode(0x15, extended: false, ShiftState.Base)); // QWERTZ: Y position on US is Z
        Assert.Equal("7", sim.PressScanCode(0x47, extended: false, ShiftState.Base)); // numpad 7 with Num Lock
        Assert.Equal("/", sim.PressScanCode(0x35, extended: true, ShiftState.Base));  // numpad divide
    }
}
