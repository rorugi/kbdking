using Keyservant.Binary;
using Keyservant.Core.Editing;
using Keyservant.Core.Formats;
using Keyservant.Core.Model;
using Keyservant.Core.Templates;
using Keyservant.Core.Validation;

namespace Keyservant.Tests;

public class ShortcutTests
{
    /// <summary>What every physical key types in every shift state, by scan code.</summary>
    private static Dictionary<(int, int), string> Typed(KeyboardLayout layout)
    {
        var result = new Dictionary<(int, int), string>();
        foreach (var state in layout.Modifiers.Columns.Where(s => !s.Bits.HasFlag(ModifierBits.Ctrl) || s.Bits.HasFlag(ModifierBits.Alt)))
        {
            for (var sc = 1; sc < 0x59; sc++)
            {
                var sim = new TypingSimulator(layout);
                result[(sc, state.Value)] = sim.PressScanCode(sc, false, state) + (sim.PendingDeadKey is { } d ? $"<dead {d}>" : "");
            }
        }
        return result;
    }

    [Fact]
    public void Colemak_follows_letters_by_default()
    {
        var colemak = TestLayouts.Colemak();
        Assert.Equal("f", new TypingSimulator(colemak).PressScanCode(0x12, false, ShiftState.Base));
        Assert.Equal(new[] { ShortcutMode.FollowLetters }, ShortcutPositions.Detect(colemak));
    }

    [Fact]
    public void Qwerty_shortcuts_keep_every_character_and_move_only_virtual_keys()
    {
        var layout = TestLayouts.Colemak();
        var before = Typed(layout);
        var moved = ShortcutPositions.Apply(layout, ShortcutMode.Qwerty);
        Assert.True(moved >= 15);
        Assert.Equal(before, Typed(layout));
        Assert.Equal((byte)'E', layout.ScanCodes[0x12].VirtualKey); // Ctrl+E where QWERTY has E, though the key types f
        Assert.Equal((byte)'C', layout.ScanCodes[0x2E].VirtualKey);
        Assert.Equal(new[] { ShortcutMode.Qwerty }, ShortcutPositions.Detect(layout));
        // Ctrl+key types the control character of the new virtual key (Ctrl+C = U+0003 in consoles).
        Assert.Equal("\u0005", layout.FindKey((byte)'E')![ShiftState.Ctrl]!.Text);
        Assert.Empty(LayoutValidator.Validate(layout).Issues.Where(i => i.Severity == Severity.Error));

        // And back: shortcuts follow the letters again, with the original virtual keys.
        ShortcutPositions.Apply(layout, ShortcutMode.FollowLetters);
        Assert.Equal(before, Typed(layout));
        Assert.Equal(TestLayouts.Colemak().ScanCodes.Normal, layout.ScanCodes.Normal);
    }

    [Fact]
    public void German_layout_is_already_qwertz_and_can_use_qwerty_shortcuts()
    {
        var layout = LayoutTemplates.German("de-AT");
        var modes = ShortcutPositions.Detect(layout);
        Assert.Contains(ShortcutMode.FollowLetters, modes);
        Assert.Contains(ShortcutMode.Qwertz, modes);

        var before = Typed(layout);
        Assert.Equal(2, ShortcutPositions.Apply(layout, ShortcutMode.Qwerty));
        Assert.Equal(before, Typed(layout));
        Assert.Equal((byte)'Y', layout.ScanCodes[0x15].VirtualKey);
        Assert.Equal("z", new TypingSimulator(layout).Press((byte)'Y', ShiftState.Base));
    }

    [Fact]
    public void Shortcut_positions_survive_a_dll_round_trip()
    {
        var layout = TestLayouts.Colemak();
        ShortcutPositions.Apply(layout, ShortcutMode.Qwerty);
        var back = LayoutDllReader.Read(LayoutDllBuilder.Build(layout).X64);
        Assert.Equal(new[] { ShortcutMode.Qwerty }, ShortcutPositions.Detect(back));
        Assert.Equal(Typed(layout), Typed(back));
    }

    [Fact]
    public void Azerty_positions_are_supported()
    {
        var layout = TestLayouts.Colemak();
        var before = Typed(layout);
        ShortcutPositions.Apply(layout, ShortcutMode.Azerty);
        Assert.Equal(before, Typed(layout));
        Assert.Equal((byte)'A', layout.ScanCodes[0x10].VirtualKey);
        Assert.Equal((byte)'M', layout.ScanCodes[0x27].VirtualKey);
    }
}

public class LayerKeyTests
{
    private static readonly ShiftState LayerA = new(ModifierBits.LayerA);

    [Fact]
    public void Layer_keys_select_their_column_while_held()
    {
        var layout = TestLayouts.Layered();
        var keys = LayerKeys.Find(layout);
        Assert.Equal(2, keys.Count);
        Assert.Contains(keys, k => k.ScanCode == 0x3A && k.Layer == ModifierBits.LayerA);
        Assert.Equal(ModifierBits.LayerA, LayerKeys.HeldLayers(layout, [(0x3A, false)]));

        var sim = new TypingSimulator(layout);
        Assert.Equal("α", sim.PressScanCode(0x1E, false, LayerA));
        Assert.Equal("Α", sim.PressScanCode(0x1E, false, new ShiftState(ModifierBits.LayerA | ModifierBits.Shift)));
        Assert.Equal("√", sim.PressScanCode(0x13, false, new ShiftState(ModifierBits.LayerB)));
        Assert.Equal("", sim.PressScanCode(0x12, false, new ShiftState(ModifierBits.LayerB))); // dead macron
        Assert.Equal("ā", sim.PressScanCode(0x1E, false, ShiftState.Base));
        Assert.Equal("a", sim.PressScanCode(0x1E, false, ShiftState.Base));
    }

    [Fact]
    public void Layered_layouts_round_trip_through_files_and_dlls()
    {
        var layout = TestLayouts.Layered();
        var text = KeyservantFormat.Serialize(layout);
        Assert.Contains("\"layer-a\"", text);
        Assert.Equal(text, KeyservantFormat.Serialize(KeyservantFormat.Parse(text)));
        foreach (var dll in new[] { LayoutDllBuilder.Build(layout).X64, LayoutDllBuilder.Build(layout).Arm64! })
        {
            var back = LayoutDllReader.Read(dll);
            back.Metadata = layout.Metadata.Clone();
            Assert.Equal(text, KeyservantFormat.Serialize(back));
        }
        Assert.DoesNotContain(LayoutValidator.Validate(layout).Issues, i => i.Severity == Severity.Error || i.Code == "KS090");
    }

    [Fact]
    public void Old_names_for_the_layer_bits_still_parse()
    {
        Assert.Equal(LayerA, ShiftState.Parse("roya"));
        Assert.Equal(new ShiftState(ModifierBits.LayerC | ModifierBits.Shift), ShiftState.Parse("shift+0x40"));
        Assert.Equal("Shift + Layer A", new ShiftState(ModifierBits.LayerA | ModifierBits.Shift).DisplayName);
    }

    [Fact]
    public void Key_sequences_and_tests_can_use_layers()
    {
        var layout = TestLayouts.Layered();
        layout.Tests.Add(new LayoutTestCase("LayerA+a", "α"));
        layout.Tests.Add(new LayoutTestCase("Shift+LayerA+a", "Α"));
        layout.Tests.Add(new LayoutTestCase("LayerB+e a", "ā"));
        Assert.All(LayoutTestRunner.Run(layout), r => Assert.True(r.Passed, r.Message));

        var finder = new Core.Analysis.KeystrokeFinder(layout);
        var recipe = finder.Find("β")!;
        Assert.Equal("LayerA+b", KeySequence.Format(recipe.Strokes, layout));
    }

    [Fact]
    public void Unreachable_layers_are_reported()
    {
        var layout = TestLayouts.Layered();
        LayerKeys.Remove(layout, LayerKeys.Find(layout).First(k => k.Layer == ModifierBits.LayerA));
        Assert.Equal(0x14, layout.ScanCodes[0x3A].VirtualKey); // Caps Lock again
        Assert.Contains(LayoutValidator.Validate(layout).Issues, i => i.Code == "KS090" && i.Message.Contains("Layer A", StringComparison.Ordinal));
        Assert.Equal("α", layout.FindKey((byte)'A')![LayerA]!.Text); // the characters are kept
    }

    [Fact]
    public void Right_alt_cannot_replace_altgr_while_altgr_has_characters()
    {
        var layout = LayoutTemplates.German("de-AT");
        Assert.Throws<InvalidOperationException>(() => LayerKeys.Add(layout, 0x38, true, ModifierBits.LayerA));
        Assert.NotNull(LayerKeys.Add(layout, 0x56, false, ModifierBits.LayerA)); // the <> key typed < > |
    }
}
