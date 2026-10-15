using Kbdking.Binary.Packaging;
using Kbdking.Core.Model;
using Kbdking.Platform;
using Kbdking.Presentation;

namespace Kbdking.Tests;

public partial class PresentationTests
{
    private sealed class FakeDialogs : IDialogService
    {
        public List<string> Messages { get; } = [];
        public bool ConfirmResult { get; set; } = true;
        public string? NextFile { get; set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirmText) => Task.FromResult(ConfirmResult);
        public Task ShowMessageAsync(string title, string message) { Messages.Add(message); return Task.CompletedTask; }
        public Task<string?> PickOpenFileAsync(IReadOnlyList<string> extensions) => Task.FromResult(NextFile);
        public Task<string?> PickSaveFileAsync(string suggestedName, IReadOnlyList<string> extensions) => Task.FromResult(NextFile);
        public Task<string?> PickFolderAsync() => Task.FromResult(NextFile);
    }

    private sealed class FakePrivileged : IPrivilegedOperations
    {
        public List<string> Calls { get; } = [];
        public Task<OperationResult> InstallAsync(string layoutFile) { Calls.Add("install " + layoutFile); return Task.FromResult(new OperationResult(true, "ok")); }
        public Task<OperationResult> UninstallAsync(string klid) { Calls.Add("uninstall " + klid); return Task.FromResult(new OperationResult(true, "ok")); }
        public Task<OperationResult> RepairAsync() { Calls.Add("repair"); return Task.FromResult(new OperationResult(true, "ok")); }
        public Task<OperationResult> InstallTemporaryAsync(string layoutFile, int minutes) { Calls.Add($"temporary {minutes} {layoutFile}"); return Task.FromResult(new OperationResult(true, "ok")); }
        public Task<OperationResult> KeepAsync(string klid) { Calls.Add("keep " + klid); return Task.FromResult(new OperationResult(true, "ok")); }
    }

    private sealed class FakeSystem : ILayoutSystem
    {
        public List<InstalledLayout> Layouts { get; } =
        [
            new() { Klid = "00000407", LayoutFile = "KBDGR.DLL", Name = "German", IsEnabled = true },
            new() { Klid = "A0000C07", LayoutFile = "kbdiast.dll", Name = "German (Austria) + IAST", IsCustom = true },
        ];
        public List<string> Calls { get; } = [];

        public IReadOnlyList<InstalledLayout> GetLayouts() => Layouts;
        public void Enable(InstalledLayout layout) => Calls.Add("enable " + layout.Klid);
        public void Disable(InstalledLayout layout) => Calls.Add("disable " + layout.Klid);
        public InstalledLayout Install(LayoutPackage package) => throw new NotSupportedException();
        public void Uninstall(string klid) => throw new NotSupportedException();
        public IReadOnlyList<string> DescribeUninstall(string klid) => ["files"];
        public RepairReport Repair() => throw new NotSupportedException();
        public InstalledLayout InstallTemporary(LayoutPackage package, TimeSpan duration, string cleanupExecutable) => throw new NotSupportedException();
        public void Keep(string klid) => Calls.Add("keep " + klid);
        public IReadOnlyList<string> CleanupExpired() => [];
    }

    [Fact]
    public void Editing_a_key_updates_the_layout_and_the_keyboard()
    {
        var editor = new EditorViewModel(new FakeDialogs(), new FakePrivileged());
        var aKey = editor.KeyboardRows.SelectMany(r => r.Keys).First(k => k.VirtualKey == (byte)'A');
        aKey.SelectCommand.Execute(null);
        Assert.NotNull(editor.KeyEditor);

        var altGr = editor.KeyEditor!.Rows.First(r => r.State == ShiftState.AltGr);
        altGr.Text = "U+0101";
        Assert.Null(altGr.Error);
        Assert.Equal("ā", editor.Layout.FindKey((byte)'A')![ShiftState.AltGr]!.Text);
        Assert.True(editor.IsDirty);

        editor.SelectedState = editor.ShiftStates.First(s => s.State == ShiftState.AltGr);
        Assert.Equal("ā", aKey.Label);
    }

    [Fact]
    public void Making_a_key_dead_creates_the_dead_key()
    {
        var editor = new EditorViewModel(new FakeDialogs(), new FakePrivileged());
        editor.KeyboardRows.SelectMany(r => r.Keys).First(k => k.VirtualKey == 0xBE).SelectCommand.Execute(null);
        var row = editor.KeyEditor!.Rows.First(r => r.State == ShiftState.AltGr);
        row.Text = "\\u0323";
        row.IsDead = true;
        Assert.True(editor.Layout.FindKey(0xBE)![ShiftState.AltGr]!.IsDead);
        Assert.Contains(editor.DeadKeys.DeadKeys, d => d.AccentCode == "U+0323");
    }

    [Fact]
    public void Invalid_code_shows_an_error_and_changes_nothing()
    {
        var editor = new EditorViewModel(new FakeDialogs(), new FakePrivileged());
        editor.KeyboardRows.SelectMany(r => r.Keys).First(k => k.VirtualKey == (byte)'A').SelectCommand.Execute(null);
        var row = editor.KeyEditor!.Rows.First(r => r.State == ShiftState.Base);
        row.Text = "U+ZZZZ";
        Assert.NotNull(row.Error);
        Assert.Equal("a", editor.Layout.FindKey((byte)'A')![ShiftState.Base]!.Text);
    }

    [Fact]
    public void Preview_types_with_the_edited_layout()
    {
        var editor = new EditorViewModel(new FakeDialogs(), new FakePrivileged());
        editor.HandlePreviewKey(0x15, false, shift: false, ctrl: false, alt: false, capsLock: false); // z on QWERTZ
        editor.HandlePreviewKey(0x29, false, false, false, false, false);                           // ^ (dead)
        editor.HandlePreviewKey(0x12, false, false, false, false, false);                           // e
        Assert.Equal("zê", editor.PreviewText);
        editor.HandlePreviewKey(0x0E, false, false, false, false, false);                           // backspace
        Assert.Equal("z", editor.PreviewText);
    }

    [Fact]
    public void Dead_key_entries_can_be_edited()
    {
        var editor = new EditorViewModel(new FakeDialogs(), new FakePrivileged());
        var circumflex = editor.DeadKeys.DeadKeys.First(d => d.AccentCode == "U+005E");
        circumflex.AddEntryCommand.Execute(null);
        var entry = circumflex.Entries[^1];
        entry.Base = "w";
        entry.Result = "ŵ";
        Assert.Equal('ŵ', editor.Layout.FindDeadKey('^')!.Find('w')!.Value.Result);
        entry.RemoveCommand.Execute(null);
        Assert.Null(editor.Layout.FindDeadKey('^')!.Find('w'));
    }

    [Fact]
    public void Manager_toggles_and_filters()
    {
        var system = new FakeSystem();
        var manager = new LayoutManagerViewModel(system, new FakePrivileged(), new FakeDialogs(), _ => { });
        manager.Refresh();
        Assert.Equal(2, manager.Layouts.Count);

        manager.Layouts[1].IsEnabled = true;
        Assert.Contains("enable A0000C07", system.Calls);

        manager.CustomOnly = true;
        Assert.Single(manager.Layouts);
        manager.CustomOnly = false;
        manager.Filter = "00000407";
        Assert.Equal("German", Assert.Single(manager.Layouts).Name);
    }

    [Fact]
    public async Task Manager_uninstalls_through_the_elevated_helper_after_confirmation()
    {
        var privileged = new FakePrivileged();
        var dialogs = new FakeDialogs();
        var manager = new LayoutManagerViewModel(new FakeSystem(), privileged, dialogs, _ => { });
        manager.Refresh();
        var custom = manager.Layouts.First(l => l.IsCustom);
        Assert.True(custom.UninstallCommand.CanExecute(null));
        Assert.False(manager.Layouts.First(l => !l.IsCustom).UninstallCommand.CanExecute(null));

        custom.UninstallCommand.Execute(null);
        await WaitUntil(() => privileged.Calls.Count > 0);
        Assert.Contains("uninstall A0000C07", privileged.Calls);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("U+0101", "ā")]
    [InlineData("\\u1E6D", "ṭ")]
    [InlineData("U+1D53B", "𝔻")]
    [InlineData("xU+0303", "x̃")]
    public void Output_text_parsing(string input, string expected)
    {
        Assert.True(OutputRowViewModel.TryParse(input, out var result));
        Assert.Equal(expected, result);
    }
}
