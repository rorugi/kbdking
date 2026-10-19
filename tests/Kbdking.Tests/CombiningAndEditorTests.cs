using System.Globalization;
using Kbdking.Core.Analysis;
using Kbdking.Core.Assist;
using Kbdking.Core.Model;
using Kbdking.Core.Templates;
using Kbdking.Presentation;

namespace Kbdking.Tests;

public class CombiningMarkTests
{
    internal static IEnumerable<string> Graphemes(string s)
    {
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) yield return (string)e.Current;
    }

    [Fact]
    public void A_letter_and_a_combining_mark_key_type_characters_without_a_code_point_of_their_own()
    {
        var layout = LayoutTemplates.Create("en-US");
        layout.SetOutput((byte)'0', ShiftState.AltGr, KeyOutput.Of("̥"));
        var finder = new KeystrokeFinder(layout);
        Assert.Equal(2, finder.Find("r̥")!.Strokes.Count);
        Assert.True(finder.CanType("L̥"));
        Assert.False(finder.CanType("ṛ")); // has a code point of its own; typing r + mark would not give it
    }

    [Fact]
    public void Planner_puts_combining_marks_on_keys_for_r_and_l_with_ring_below()
    {
        var layout = LayoutTemplates.Create("de-AT");
        const string wanted = "r̥R̥r̥̄R̥̄l̥L̥l̥̄L̥̄";
        var plan = PlacementPlanner.Plan(layout, wanted);
        Assert.Empty(plan.NotPlaced);
        var ring = Assert.Single(plan.Changes, c => c.Kind == PlannedChangeKind.CombiningKey && c.Description.Contains("ring below", StringComparison.Ordinal));
        Assert.Contains("r̥", ring.Characters);
        plan.ApplyAll(layout);
        var finder = new KeystrokeFinder(layout);
        foreach (var g in Graphemes(wanted)) Assert.True(finder.CanType(g), g);
        // A second run finds nothing left to do.
        Assert.Empty(PlacementPlanner.Plan(layout, wanted).Changes);
    }
}

public partial class PresentationTests
{
    [Fact]
    public void Key_corner_shows_the_shift_character_in_base_and_the_key_letter_elsewhere()
    {
        var editor = NewEditor();
        Assert.Equal("A", Key(editor, (byte)'A').CornerLabel);
        Assert.Equal("!", Key(editor, (byte)'1').CornerLabel);
        editor.SelectedState = editor.ShiftStates.First(s => s.State == ShiftState.AltGr);
        Assert.Equal("A", Key(editor, (byte)'A').CornerLabel);
        Assert.Equal("1", Key(editor, (byte)'1').CornerLabel);
        editor.SelectedState = editor.ShiftStates.First(s => s.State == ShiftState.Shift);
        Assert.Equal("Q", Key(editor, (byte)'Q').CornerLabel);
    }

    [Fact]
    public void A_dead_key_in_the_key_editor_links_to_its_entry_on_the_dead_keys_tab()
    {
        var editor = NewEditor();
        Key(editor, (byte)'E').SelectCommand.Execute(null);
        var row = editor.KeyEditor!.Rows.First(r => r.State == ShiftState.AltGr);
        row.Text = "¨";
        Assert.False(row.HasDeadKey);
        row.IsDead = true;
        Assert.True(row.HasDeadKey);

        Key(editor, (byte)'R').SelectCommand.Execute(null);
        var other = editor.KeyEditor!.Rows.First(r => r.State == ShiftState.AltGr);
        other.Text = "˚";
        other.IsDead = true;
        editor.DeadKeys.Selected = editor.DeadKeys.DeadKeys.First(d => d.AccentChar == '˚');

        string? requested = null;
        editor.TabRequested += (_, tab) => requested = tab;
        row.ShowDeadKeyCommand.Execute(null);
        Assert.Equal("tab-deadkeys", requested);
        Assert.Equal('¨', editor.DeadKeys.Selected?.AccentChar);
    }

    [Theory]
    [InlineData("Sanskrit (IAST)")]
    [InlineData("Sanskrit (ISO 15919)")]
    public void The_sanskrit_presets_become_typeable(string name)
    {
        var preset = NewEditor().Assistant.Presets.First(p => p.Name == name).Characters;
        // IAST writes vocalic r and l with dot below (ṛ ḷ); only ISO 15919 uses the ring below (r̥ l̥).
        Assert.Equal(name.Contains("ISO", StringComparison.Ordinal), preset.Contains('\u0325', StringComparison.Ordinal));
        var layout = LayoutTemplates.Create("de-AT");
        var plan = PlacementPlanner.Plan(layout, preset);
        Assert.Empty(plan.NotPlaced);
        plan.ApplyAll(layout);
        var finder = new KeystrokeFinder(Kbdking.Core.Compose.ComposeCompiler.Compile(layout).Layout);
        foreach (var g in CombiningMarkTests.Graphemes(preset)) Assert.True(finder.CanType(g), g);
    }
}

public partial class PresentationTests
{
    [Fact]
    public void A_combining_mark_shown_on_a_dotted_circle_is_taken_as_the_mark_alone()
    {
        Assert.True(OutputRowViewModel.TryParse("◌̥", out var mark));
        Assert.Equal("̥", mark);
        Assert.True(OutputRowViewModel.TryParse("◌", out var circle));
        Assert.Equal("◌", circle);

        var editor = NewEditor();
        Key(editor, (byte)'0').SelectCommand.Execute(null);
        var row = editor.KeyEditor!.Rows.First(r => r.State == ShiftState.AltGr);
        row.Text = "◌̥";
        row.IsDead = true;
        Assert.Null(row.Error);
        Assert.Equal('̥', editor.Layout.FindKey((byte)'0')![ShiftState.AltGr]!.Accent);
        Assert.NotNull(editor.Layout.FindDeadKey('̥'));

        row.Text = "r̥";
        Assert.Contains("combining mark", row.Error, StringComparison.Ordinal);
    }
}

public partial class PresentationTests
{
    [Fact]
    public void Auto_shift_state_follows_the_held_modifier_and_layer_keys()
    {
        var editor = NewEditor();
        Assert.True(editor.ShiftStateChoices[0].IsAuto);
        Assert.DoesNotContain(editor.ShiftStates, s => s.IsAuto); // the key editor and ligatures only see real states
        Key(editor, (byte)'A').SelectCommand.Execute(null);
        editor.KeyEditor!.Rows.First(r => r.State == ShiftState.AltGr).Text = "ā";
        editor.KeyEditor!.Rows.First(r => r.State == ShiftState.ShiftAltGr).Text = "Ā";

        editor.SelectedState = editor.AutoState;
        Assert.Equal("a", Key(editor, (byte)'A').Label);
        editor.SetHeldKeys(shift: true, ctrl: false, alt: false, []);
        Assert.Equal("A", Key(editor, (byte)'A').Label);
        editor.SetHeldKeys(shift: false, ctrl: true, alt: true, []);
        Assert.Equal("ā", Key(editor, (byte)'A').Label);
        Assert.Equal("Showing " + ShiftState.AltGr.DisplayName, editor.AutoStateText);
        editor.SetHeldKeys(shift: true, ctrl: true, alt: true, []);
        Assert.Equal("Ā", Key(editor, (byte)'A').Label);
        editor.SetHeldKeys(false, false, false, []);
        Assert.Equal("a", Key(editor, (byte)'A').Label);

        // A layer key held by its scan code shows the layer.
        Kbdking.Core.Editing.LayerKeys.Add(editor.Layout, 0x3A, false, ModifierBits.LayerA);
        editor.LayoutChangedExternally();
        Assert.True(editor.SelectedState.IsAuto);
        editor.SetHeldKeys(false, false, false, [(0x3A, false)]);
        Assert.Equal(new ShiftState(ModifierBits.LayerA), editor.DisplayedState);

        // Other views ignore held keys.
        editor.SelectedState = editor.ShiftStates.First(s => s.State == ShiftState.Base);
        editor.SetHeldKeys(true, false, false, []);
        Assert.Equal("a", Key(editor, (byte)'A').Label);
        Assert.Equal("", editor.AutoStateText);
    }

    [Fact]
    public void Home_row_keys_are_marked()
    {
        var editor = NewEditor();
        var home = editor.KeyboardRows.SelectMany(r => r.Keys).Where(k => k.IsHomeKey).Select(k => k.Key.ScanCode).Order().ToArray();
        Assert.Equal(new[] { 0x1E, 0x1F, 0x20, 0x21, 0x24, 0x25, 0x26, 0x27 }, home);
    }

    [Fact]
    public void Layouts_in_the_switcher_come_first()
    {
        var system = new FakeSystem();
        system.Layouts.Insert(0, new() { Klid = "00000409", LayoutFile = "KBDUS.DLL", Name = "US" });
        system.Layouts.Add(new() { Klid = "00000809", LayoutFile = "KBDUK.DLL", Name = "United Kingdom", IsEnabled = true });
        var manager = new LayoutManagerViewModel(system, new FakePrivileged(), new FakeDialogs(), _ => { });
        manager.Refresh();
        Assert.Equal(new[] { "German", "United Kingdom", "German (Austria) + IAST", "US" }, manager.Layouts.Select(l => l.Name).ToArray());
    }
}

public partial class PresentationTests
{
    [Fact]
    public async Task Add_with_compositions_extends_the_existing_dead_key_for_that_accent()
    {
        var editor = NewEditor();
        Assert.True(await editor.OpenFileInteractiveAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "german-austria-iast.kbdking")));
        var count = editor.Layout.DeadKeys.Count;
        var macron = editor.Layout.FindDeadKey('̄')!;
        Assert.Null(macron.Find('e'));

        editor.DeadKeys.AddCommonCommand.Execute("̄");
        Assert.Equal(count, editor.Layout.DeadKeys.Count); // no second macron dead key
        Assert.Equal('ē', macron.Find('e')!.Value.Result);
        Assert.Equal('Ē', macron.Find('E')!.Value.Result);
        Assert.Equal('̄', editor.DeadKeys.Selected?.AccentChar);
        Assert.Equal("German (Austria) + IAST — german-austria-iast.kbdking •", editor.Title);
    }
}

public partial class PresentationTests
{
    [Fact]
    public async Task Demo_fills_the_tools_with_an_example()
    {
        var editor = NewEditor();
        Assert.True(await editor.OpenFileInteractiveAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "german-austria-iast.kbdking")));
        editor.ShowDemo();
        Assert.NotNull(editor.KeyEditor);
        Assert.NotEmpty(editor.Assistant.Proposals);
        Assert.NotEmpty(editor.PreviewText);
        await WaitUntil(() => editor.Analysis.HeatmapSvg is not null);
        Assert.NotNull(editor.Analysis.HeatmapSvg);
    }
}
