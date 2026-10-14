using Keyservant.Core.Compose;
using Keyservant.Core.Formats;
using Keyservant.Core.Geometry;
using Keyservant.Core.Model;
using Keyservant.Core.Templates;
using Keyservant.Platform;
using Keyservant.Presentation;

namespace Keyservant.Tests;

public partial class PresentationTests
{
    private static EditorViewModel NewEditor() => new(new FakeDialogs(), new FakePrivileged());

    /// <summary>Commands run asynchronously; wait for their effect instead of a fixed delay.</summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
    }

    private static KeyCapViewModel Key(EditorViewModel editor, byte vk) => editor.KeyboardRows.SelectMany(r => r.Keys).First(k => k.VirtualKey == vk);

    [Fact]
    public void Undo_and_redo_revert_key_edits()
    {
        var editor = NewEditor();
        Assert.False(editor.UndoCommand.CanExecute(null));
        Key(editor, (byte)'A').SelectCommand.Execute(null);
        editor.KeyEditor!.Rows.First(r => r.State == ShiftState.AltGr).Text = "ā";
        Assert.True(editor.UndoCommand.CanExecute(null));

        editor.UndoCommand.Execute(null);
        Assert.Null(editor.Layout.FindKey((byte)'A')![ShiftState.AltGr]);
        Assert.True(editor.RedoCommand.CanExecute(null));
        editor.RedoCommand.Execute(null);
        Assert.Equal("ā", editor.Layout.FindKey((byte)'A')![ShiftState.AltGr]!.Text);
        editor.SelectedState = editor.ShiftStates.First(s => s.State == ShiftState.AltGr);
        Assert.Equal("ā", Key(editor, (byte)'A').Label);
    }

    [Fact]
    public void Typing_into_one_field_is_one_undo_step()
    {
        var editor = NewEditor();
        var original = editor.LayoutName;
        editor.LayoutName = "M";
        editor.LayoutName = "My";
        editor.LayoutName = "My layout";
        editor.UndoCommand.Execute(null);
        Assert.Equal(original, editor.LayoutName);
        Assert.False(editor.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Unicode_picker_fills_the_target_field()
    {
        var editor = NewEditor();
        Key(editor, (byte)'R').SelectCommand.Execute(null);
        var row = editor.KeyEditor!.Rows.First(r => r.State == ShiftState.AltGr);
        editor.Unicode.Target = row;
        editor.Unicode.Query = "r dot below small";
        Assert.Equal("ṛ", editor.Unicode.Results[0].Text);
        Assert.True(editor.Unicode.Pick());
        Assert.Equal("ṛ", editor.Layout.FindKey((byte)'R')![ShiftState.AltGr]!.Text);
        Assert.Equal("U+1E5B LATIN SMALL LETTER R WITH DOT BELOW", row.CodePoints);
    }

    [Fact]
    public void Keyboard_shapes_switch_the_on_screen_keyboard()
    {
        var editor = NewEditor();
        editor.Shape = KeyboardShape.Jis;
        Assert.Contains(editor.KeyboardRows.SelectMany(r => r.Keys), k => k.Key.ScanCode == 0x7D);
        editor.Shape = KeyboardShape.Ansi;
        Assert.DoesNotContain(editor.KeyboardRows.SelectMany(r => r.Keys), k => k.Key.ScanCode == 0x56);
    }

    [Fact]
    public void Shift_states_can_be_added_and_removed()
    {
        var editor = NewEditor();
        var states = editor.ShiftStateEditor;
        var before = states.Columns.Count;
        states.AddKana = true;
        states.AddCommand.Execute(null);
        Assert.Equal(before + 1, states.Columns.Count);
        Assert.Contains("KANA", states.Message);
        Assert.Contains(editor.ShiftStates, s => s.State.Bits == ModifierBits.Kana);

        var kana = states.Columns.First(c => c.State.Bits == ModifierBits.Kana);
        kana.RemoveCommand.Execute(null);
        Assert.Equal(before, states.Columns.Count);
        Assert.False(states.Columns.First(c => c.State == ShiftState.Base).RemoveCommand.CanExecute(null));
        editor.UndoCommand.Execute(null);
        Assert.Equal(before + 1, editor.ShiftStateEditor.Columns.Count);
    }

    [Fact]
    public void Scan_codes_can_be_remapped()
    {
        var editor = NewEditor();
        var z = editor.ScanCodes.Rows.First(r => r.Code == "15");
        z.Target = "Y";
        Assert.Null(z.Error);
        Assert.Equal((byte)'Y', editor.Layout.ScanCodes[0x15].VirtualKey);
        Assert.Equal("y", editor.KeyboardRows.SelectMany(r => r.Keys).First(k => k.Key.ScanCode == 0x15 && !k.Key.Extended).Label);
        z.Target = "NOT_A_KEY";
        Assert.NotNull(z.Error);

        editor.ScanCodes.NewCode = "E0 5C";
        editor.ScanCodes.AddCommand.Execute(null);
        Assert.Contains(editor.ScanCodes.Rows, r => r.Code == "E0 5C");
    }

    [Fact]
    public void Key_names_can_be_edited_and_replaced()
    {
        var editor = NewEditor();
        editor.KeyNames.TranslateCommand.Execute("en");
        Assert.Equal("Esc", editor.Layout.KeyNames[0x01]);
        var esc = editor.KeyNames.Rows.First(r => r.Code == "01");
        esc.Name = "Escape";
        Assert.Equal("Escape", editor.Layout.KeyNames[0x01]);
        esc.RemoveCommand.Execute(null);
        Assert.False(editor.Layout.KeyNames.ContainsKey(0x01));
    }

    [Fact]
    public void Ligatures_are_listed_added_and_edited()
    {
        var editor = NewEditor();
        editor.Ligatures.NewKey = "q";
        editor.Ligatures.NewState = editor.ShiftStates.First(s => s.State == ShiftState.AltGr);
        editor.Ligatures.NewText = "ng";
        editor.Ligatures.AddCommand.Execute(null);
        var row = Assert.Single(editor.Ligatures.Rows);
        Assert.Equal("ng", editor.Layout.FindKey((byte)'Q')![ShiftState.AltGr]!.Text);
        row.Text = "rU+0323U+0304";
        Assert.Null(row.Error);
        Assert.Equal("ṝ", editor.Layout.FindKey((byte)'Q')![ShiftState.AltGr]!.Text);
        row.RemoveCommand.Execute(null);
        Assert.Empty(editor.Ligatures.Rows);
    }

    [Fact]
    public void Compose_tab_edits_sequences_and_preview_uses_them()
    {
        var editor = NewEditor();
        editor.Compose.IsEnabled = true;
        editor.Compose.AddCommand.Execute(null);
        var seq = editor.Compose.Sequences[0];
        seq.Keys = ".r";
        seq.Result = "ṛ";
        Assert.Equal(new[] { new ComposeSequence(".r", "ṛ") }, editor.Layout.Compose!.Sequences);
        Assert.Contains("1 usable", editor.Compose.Summary);

        editor.HandlePreviewKey(0x5D, true, false, false, false, false); // Menu key = compose
        editor.HandlePreviewKey(0x34, false, false, false, false, false); // .
        editor.HandlePreviewKey(0x13, false, false, false, false, false); // r
        Assert.Equal("ṛ", editor.PreviewText);

        editor.Compose.IsEnabled = false;
        Assert.Null(editor.Layout.Compose);
    }

    [Fact]
    public void Assistant_proposals_can_be_applied_selectively()
    {
        var editor = NewEditor();
        editor.Assistant.Characters = "ṭḍṇṣḥṃñ";
        editor.Assistant.SuggestCommand.Execute(null);
        Assert.NotEmpty(editor.Assistant.Proposals);
        foreach (var p in editor.Assistant.Proposals.Skip(1)) p.IsSelected = false;
        editor.Assistant.ApplyCommand.Execute(null);
        Assert.True(editor.IsDirty);
        Assert.True(editor.Assistant.Proposals.Count >= 1); // re-planned: what's left
        editor.UndoCommand.Execute(null);
        Assert.Equal(KeyservantFormat.Serialize(LayoutTemplates.German("de-AT")), KeyservantFormat.Serialize(editor.Layout));
    }

    [Fact]
    public void Analysis_reports_effort_and_untypeable_characters()
    {
        var editor = NewEditor();
        editor.Analysis.Text = "Grüße ṛ";
        editor.Analysis.AnalyzeCommand.Execute(null);
        Assert.Contains("typeable", editor.Analysis.Summary);
        Assert.Equal("ṛ", Assert.Single(editor.Analysis.Untypeable).Character);
        Assert.NotNull(editor.Analysis.HeatmapSvg);
        editor.Analysis.SendUntypeableToAssistant();
        Assert.Equal("ṛ", editor.Assistant.Characters);
    }

    [Fact]
    public void Tests_tab_runs_tests_after_every_change()
    {
        var editor = NewEditor();
        editor.Tests.AddCommand.Execute(null);
        var test = editor.Tests.Tests[0];
        test.Keys = "AltGr+a";
        test.Expect = "ā";
        Assert.False(test.Passed);
        Assert.Single(editor.Layout.Tests);

        Key(editor, (byte)'A').SelectCommand.Execute(null);
        editor.KeyEditor!.Rows.First(r => r.State == ShiftState.AltGr).Text = "ā";
        Assert.True(test.Passed);
        Assert.Equal("All 1 tests pass.", editor.Tests.Summary);
    }

    [Fact]
    public void Default_tests_cover_what_the_layout_types()
    {
        var editor = NewEditor();
        editor.Tests.AddDefaultsCommand.Execute(null);
        Assert.Contains(editor.Layout.Tests, t => t.Expect == "ä");
        Assert.All(editor.Tests.Tests, t => Assert.True(t.Passed, t.Message));
    }

    [Fact]
    public void Compare_lists_differences_and_marks_keys()
    {
        var editor = NewEditor();
        var original = editor.Layout.Clone();
        Key(editor, (byte)'A').SelectCommand.Execute(null);
        editor.KeyEditor!.Rows.First(r => r.State == ShiftState.AltGr).Text = "ā";
        editor.Compare.CompareWith(original, "the original");
        Assert.Contains(editor.Compare.Changes, c => c.VirtualKey == (byte)'A');
        Assert.True(Key(editor, (byte)'A').IsHighlighted);
        Assert.False(Key(editor, (byte)'B').IsHighlighted);
        editor.Compare.ClearCommand.Execute(null);
        Assert.False(Key(editor, (byte)'A').IsHighlighted);
    }

    [Fact]
    public void Common_dead_keys_come_with_compositions()
    {
        var editor = NewEditor();
        editor.DeadKeys.AddCommonCommand.Execute("̣");
        var dot = editor.Layout.FindDeadKey('̣')!;
        Assert.Equal('ṭ', dot.Find('t')!.Value.Result);
        editor.DeadKeys.Selected = editor.DeadKeys.DeadKeys.First(d => d.AccentCode == "U+0323");
        editor.DeadKeys.RemoveDeadKeyCommand.Execute(null);
        Assert.Null(editor.Layout.FindDeadKey('̣'));
    }

    [Fact]
    public async Task Test_install_goes_through_the_elevated_helper()
    {
        var dialogs = new FakeDialogs();
        var privileged = new FakePrivileged();
        var editor = new EditorViewModel(dialogs, privileged);
        var file = Path.Combine(Path.GetTempPath(), $"ks-{Guid.NewGuid():N}.keyservant");
        try
        {
            dialogs.NextFile = file;
            editor.TestMinutesValue = 15;
            editor.TestInstallCommand.Execute(null);
            await WaitUntil(() => editor.Status == "ok"); // the command has finished reading the saved file
            Assert.Contains($"temporary 15 {file}", privileged.Calls);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Temporary_installs_can_be_kept()
    {
        var system = new FakeSystem();
        system.Layouts.Add(new InstalledLayout { Klid = "A0010407", LayoutFile = "kbdtry.dll", Name = "Trying", IsCustom = true, TemporaryUntil = DateTimeOffset.Now.AddMinutes(20) });
        var privileged = new FakePrivileged();
        var manager = new LayoutManagerViewModel(system, privileged, new FakeDialogs(), _ => { });
        manager.Refresh();
        var item = manager.Layouts.First(l => l.Klid == "A0010407");
        Assert.True(item.IsTemporary);
        Assert.Contains("removed automatically", item.TemporaryText);
        Assert.False(manager.Layouts.First(l => l.Klid == "A0000C07").KeepCommand.CanExecute(null));
        item.KeepCommand.Execute(null);
        await WaitUntil(() => privileged.Calls.Count > 0);
        Assert.Contains("keep A0010407", privileged.Calls);
    }
}
