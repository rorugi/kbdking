using Kbdking.Core.Model;

namespace Kbdking.Tests;

public class ModelTests
{
    [Theory]
    [InlineData("base", 0)]
    [InlineData("shift", 1)]
    [InlineData("ctrl", 2)]
    [InlineData("shift+ctrl", 3)]
    [InlineData("altgr", 6)]
    [InlineData("shift+altgr", 7)]
    [InlineData("kana", 8)]
    [InlineData("shift+kana", 9)]
    public void Shift_state_names_round_trip(string name, int bits)
    {
        var state = ShiftState.Parse(name);
        Assert.Equal(bits, state.Value);
        Assert.Equal(name, state.Name);
    }

    [Fact]
    public void Ctrl_plus_alt_is_written_as_altgr()
    {
        Assert.Equal("altgr", ShiftState.Parse("ctrl+alt").Name);
        Assert.Equal("Shift + AltGr", ShiftState.ShiftAltGr.DisplayName);
    }

    [Fact]
    public void Unknown_shift_state_is_rejected()
    {
        Assert.False(ShiftState.TryParse("hyper", out _));
    }

    [Fact]
    public void Virtual_key_names_round_trip()
    {
        for (var vk = 1; vk < 255; vk++)
        {
            var name = VirtualKeys.GetName((byte)vk);
            Assert.Equal((byte)vk, VirtualKeys.Parse(name));
        }
        Assert.Equal(0xBA, VirtualKeys.Parse("VK_OEM_1"));
    }

    [Fact]
    public void Setting_an_output_in_a_new_shift_state_adds_a_column()
    {
        var layout = Core.Templates.LayoutTemplates.UnitedStates();
        Assert.Null(layout.Modifiers.ColumnOf(ShiftState.AltGr));
        layout.SetOutput((byte)'A', ShiftState.AltGr, KeyOutput.Of("á"));
        Assert.NotNull(layout.Modifiers.ColumnOf(ShiftState.AltGr));
        Assert.True(layout.Flags.HasFlag(LocaleFlags.AltGr));
        Assert.Equal("á", layout.FindKey((byte)'A')![ShiftState.AltGr]!.Text);
    }

    [Fact]
    public void Ligature_detection()
    {
        Assert.False(KeyOutput.Of("a").IsLigature);
        Assert.True(KeyOutput.Of("ks").IsLigature);
        Assert.True(KeyOutput.Of("𝔻").IsLigature);
        Assert.False(KeyOutput.Dead('^').IsLigature);
    }

    [Fact]
    public void New_keys_are_inserted_before_the_numpad()
    {
        var layout = Core.Templates.LayoutTemplates.UnitedStates();
        layout.GetOrAddKey(0xDF); // OEM_8
        var index = layout.Keys.FindIndex(k => k.VirtualKey == 0xDF);
        var firstNumpad = layout.Keys.FindIndex(k => VirtualKeys.IsNumpadDigit(k.VirtualKey));
        Assert.True(index < firstNumpad);
    }
}
