using Kbdking.Core.Model;
using Kbdking.Core.Templates;
using Kbdking.Core.Validation;

namespace Kbdking.Tests;

public class ValidatorTests
{
    private static IEnumerable<string> Codes(KeyboardLayout l, Severity severity) =>
        LayoutValidator.Validate(l).Issues.Where(i => i.Severity == severity).Select(i => i.Code);

    [Theory]
    [MemberData(nameof(TestLayouts.Templates), MemberType = typeof(TestLayouts))]
    public void Templates_have_no_errors_or_warnings(string templateId)
    {
        var result = LayoutValidator.Validate(LayoutTemplates.Create(templateId));
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Everything_layout_is_valid()
    {
        Assert.Empty(LayoutValidator.Validate(TestLayouts.Everything()).Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kbd-iast")]
    [InlineData("toolongname")]
    public void Bad_dll_names_are_errors(string name)
    {
        var l = LayoutTemplates.UnitedStates();
        l.Metadata.DllName = name;
        Assert.Contains("KS002", Codes(l, Severity.Error));
    }

    [Fact]
    public void Chaining_to_a_missing_dead_key_is_an_error()
    {
        var l = LayoutTemplates.German();
        l.FindDeadKey('^')!.Set('x', 'q', chained: true);
        Assert.Contains("KS044", Codes(l, Severity.Error));
    }

    [Fact]
    public void Sgcaps_with_a_dead_key_is_an_error()
    {
        var l = LayoutTemplates.German();
        var key = l.FindKey(0xDC)!; // ^ key, which is a dead key
        key.CapsOutputs = new() { [ShiftState.Base] = KeyOutput.Of("x") };
        Assert.Contains("KS024", Codes(l, Severity.Error));
    }

    [Fact]
    public void Reserved_code_points_are_errors()
    {
        var l = LayoutTemplates.UnitedStates();
        l.SetOutput((byte)'A', ShiftState.Base, KeyOutput.Of(""));
        Assert.Contains("KS060", Codes(l, Severity.Error));
    }

    [Fact]
    public void Broken_surrogates_are_errors()
    {
        var l = LayoutTemplates.UnitedStates();
        l.SetOutput((byte)'A', ShiftState.Base, KeyOutput.Of("\uD835x"));
        Assert.Contains("KS061", Codes(l, Severity.Error));
    }

    [Fact]
    public void Outputs_without_a_column_are_errors()
    {
        var l = LayoutTemplates.UnitedStates();
        l.FindKey((byte)'A')!.Outputs[ShiftState.AltGr] = KeyOutput.Of("á"); // bypasses SetOutput
        Assert.Contains("KS022", Codes(l, Severity.Error));
    }

    [Fact]
    public void AltGr_characters_without_the_flag_warn()
    {
        var l = LayoutTemplates.German();
        l.Flags = LocaleFlags.None;
        Assert.Contains("KS030", Codes(l, Severity.Warning));
    }

    [Fact]
    public void Chain_loops_warn()
    {
        var l = LayoutTemplates.German();
        l.FindDeadKey('^')!.Set('`', '`', chained: true);
        l.FindDeadKey('`')!.Set('^', '^', chained: true);
        Assert.Contains("KS047", Codes(l, Severity.Warning));
    }

    [Fact]
    public void Unreachable_keys_warn()
    {
        var l = LayoutTemplates.UnitedStates();
        l.SetOutput(0xDF, ShiftState.Base, KeyOutput.Of("x")); // OEM_8 is on no scan code
        Assert.Contains("KS025", Codes(l, Severity.Warning));
    }
}
