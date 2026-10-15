using System.Collections.ObjectModel;
using System.Windows.Input;
using Keyservant.Core.Compose;
using Keyservant.Core.Formats;

namespace Keyservant.Presentation;

/// <summary>A key that can act as the compose key.</summary>
public sealed record ComposeKeyOption(string Name, int ScanCode, bool Extended)
{
    public string Label => $"{Name} ({(Extended ? "E0 " : "")}{ScanCode:X2})";
    public override string ToString() => Label;
}

/// <summary>One compose sequence: keys typed after the compose key → result.</summary>
public sealed class ComposeSequenceViewModel : ObservableObject, ICharacterTarget
{
    private readonly ComposeViewModel _owner;
    private string _keys;
    private string _result;

    internal ComposeSequenceViewModel(ComposeViewModel owner, ComposeSequence sequence)
    {
        _owner = owner;
        _keys = sequence.Keys;
        _result = OutputRowViewModel.Format(sequence.Result);
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    /// <summary>The characters typed after the compose key, e.g. <c>.r</c> or <c>--</c>.</summary>
    public string Keys { get => _keys; set { if (SetProperty(ref _keys, value)) _owner.Commit(); } }

    public string Result { get => _result; set { if (SetProperty(ref _result, value)) { OnPropertyChanged(nameof(ResultName)); _owner.Commit(); } } }

    public string ResultName => OutputRowViewModel.Describe(_result);

    public ICommand RemoveCommand { get; }

    public void PickCharacter(string text) => Result = text;

    internal ComposeSequence? ToSequence() =>
        _keys.Length > 0 && OutputRowViewModel.TryParse(_result, out var r) && r.Length > 0 ? new ComposeSequence(_keys, r) : null;
}

/// <summary>
/// The compose tab. Compose sequences are written as the characters typed after the compose key ("Compose . r → ṛ");
/// Keyservant compiles them into chained dead keys when the layout is built, so they work in every app.
/// </summary>
public sealed class ComposeViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private bool _loading;
    private ComposeKeyOption? _selectedKey;
    private string _accent = "";
    private string _filter = "";
    private string _summary = "";
    private List<ComposeSequenceViewModel> _all = [];

    internal ComposeViewModel(EditorViewModel editor)
    {
        _editor = editor;
        AddCommand = new RelayCommand(Add);
        ImportCommand = new AsyncRelayCommand(ImportAsync, onError: editor.ReportError);
        ExportCommand = new AsyncRelayCommand(ExportAsync, onError: editor.ReportError);
    }

    public IReadOnlyList<ComposeKeyOption> KeyOptions { get; } = [.. ComposeDefinition.CommonKeys.Select(k => new ComposeKeyOption(k.Name, k.ScanCode, k.Extended))];

    public bool IsEnabled
    {
        get => _editor.Layout.Compose is not null;
        set
        {
            if (value == IsEnabled) return;
            _editor.Layout.Compose = value ? new ComposeDefinition() : null;
            _editor.MarkChanged();
            Load();
        }
    }

    public ComposeKeyOption? SelectedKey
    {
        get => _selectedKey;
        set
        {
            if (!SetProperty(ref _selectedKey, value) || _loading || value is null || _editor.Layout.Compose is not { } c) return;
            (c.ScanCode, c.Extended) = (value.ScanCode, value.Extended);
            _editor.MarkChanged();
            Compile();
        }
    }

    /// <summary>The character shown while compose waits for the next key (default ⎄).</summary>
    public string Accent
    {
        get => _accent;
        set
        {
            if (!SetProperty(ref _accent, value) || _loading || _editor.Layout.Compose is not { } c) return;
            if (OutputRowViewModel.TryParse(value, out var a) && a.Length == 1)
            {
                c.Accent = a[0];
                _editor.MarkChanged("compose-accent");
                Compile();
            }
        }
    }

    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) ApplyFilter(); } }

    public ObservableCollection<ComposeSequenceViewModel> Sequences { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public ICommand AddCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }

    internal void Load()
    {
        _loading = true;
        try
        {
            var c = _editor.Layout.Compose;
            var unfinished = _all.Where(s => s.ToSequence() is null).ToList(); // rows still being filled in
            _all = c is null ? [] : [.. unfinished, .. c.Sequences.Select(s => new ComposeSequenceViewModel(this, s))];
            SelectedKey = c is null ? null : KeyOptions.FirstOrDefault(k => k.ScanCode == c.ScanCode && k.Extended == c.Extended);
            Accent = c is null ? "" : c.Accent.ToString();
            ApplyFilter();
            OnPropertyChanged(nameof(IsEnabled));
            Compile();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyFilter()
    {
        Sequences.Clear();
        var f = _filter.Trim();
        foreach (var s in _all)
        {
            if (f.Length > 0 && !s.Keys.Contains(f, StringComparison.Ordinal) && !s.Result.Contains(f, StringComparison.Ordinal)
                && !s.ResultName.Contains(f, StringComparison.OrdinalIgnoreCase)) continue;
            Sequences.Add(s);
        }
    }

    private void Add()
    {
        if (_editor.Layout.Compose is null) IsEnabled = true; // records the change and loads the compose settings
        var row = new ComposeSequenceViewModel(this, new ComposeSequence("", ""));
        _all.Insert(0, row);
        Filter = "";
        ApplyFilter();
        OnPropertyChanged(nameof(IsEnabled));
    }

    internal void Remove(ComposeSequenceViewModel row)
    {
        _all.Remove(row);
        Sequences.Remove(row);
        Commit();
    }

    /// <summary>Writes the rows to the layout; incomplete rows are kept on screen but not saved.</summary>
    internal void Commit()
    {
        if (_loading || _editor.Layout.Compose is not { } c) return;
        c.Sequences.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in _all)
        {
            if (row.ToSequence() is { } s && seen.Add(s.Keys)) c.Sequences.Add(s);
        }
        _editor.MarkChanged("compose");
        Compile();
    }

    private void Compile()
    {
        Warnings.Clear();
        if (_editor.Layout.Compose is not { } c)
        {
            Summary = "Compose is off. Switch it on to type characters as short sequences, e.g. Compose . r → ṛ.";
            return;
        }
        var result = _editor.Compilation;
        foreach (var e in result.Errors) Warnings.Add("Error: " + e);
        foreach (var w in result.Warnings.Take(200)) Warnings.Add(w);
        if (result.Warnings.Count > 200) Warnings.Add($"… and {result.Warnings.Count - 200} more.");
        Summary = $"{c.Sequences.Count} sequences, {result.CompiledSequences} usable. Press {SelectedKey?.Name ?? c.KeyText}, then the keys.";
    }

    private async Task ImportAsync()
    {
        var path = await _editor.Dialogs.PickOpenFileAsync([".XCompose", ".txt", "*"]);
        if (path is null) return;
        var report = new ConversionReport();
        var sequences = XComposeFile.Parse(await File.ReadAllTextAsync(path), report);
        var c = _editor.Layout.Compose ??= new ComposeDefinition();
        var existing = c.Sequences.Select(s => s.Keys).ToHashSet(StringComparer.Ordinal);
        var added = sequences.Where(s => existing.Add(s.Keys)).ToList();
        c.Sequences.AddRange(added);
        _editor.MarkChanged();
        Load();
        _editor.Status = $"Imported {added.Count} sequences from {Path.GetFileName(path)}" + (report.Warnings.Count > 0 ? $" ({report.Warnings.Count} lines skipped)." : ".");
    }

    private async Task ExportAsync()
    {
        var path = await _editor.Dialogs.PickSaveFileAsync(".XCompose", [".XCompose"]);
        if (path is null) return;
        await File.WriteAllTextAsync(path, XComposeFile.Write(_editor.Layout.Compose?.Sequences ?? []));
        _editor.Status = $"Saved {Path.GetFileName(path)}. It also works with WinCompose and on Linux.";
    }
}
