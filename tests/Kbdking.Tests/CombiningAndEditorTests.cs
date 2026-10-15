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

    [Fact]
    public void The_whole_sanskrit_preset_becomes_typeable()
    {
        var preset = NewEditor().Assistant.Presets.First(p => p.Name.StartsWith("Sanskrit", StringComparison.Ordinal)).Characters;
        Assert.Contains("r̥", preset, StringComparison.Ordinal);
        Assert.Contains("l̥̄", preset, StringComparison.Ordinal);
        var layout = LayoutTemplates.Create("de-AT");
        var plan = PlacementPlanner.Plan(layout, preset);
        Assert.Empty(plan.NotPlaced);
        plan.ApplyAll(layout);
        var finder = new KeystrokeFinder(Kbdking.Core.Compose.ComposeCompiler.Compile(layout).Layout);
        foreach (var g in CombiningMarkTests.Graphemes(preset)) Assert.True(finder.CanType(g), g);
    }
}
