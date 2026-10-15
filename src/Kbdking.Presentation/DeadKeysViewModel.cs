using System.Collections.ObjectModel;
using System.Windows.Input;
using Kbdking.Core.Model;

namespace Kbdking.Presentation;

/// <summary>One composition row: base → result.</summary>
public sealed class DeadKeyEntryViewModel : ObservableObject, ICharacterTarget
{
    private readonly DeadKeyViewModel _owner;
    private string _base;
    private string _result;
    private bool _chained;

    internal DeadKeyEntryViewModel(DeadKeyViewModel owner, DeadKeyEntry entry)
        : this(owner, OutputRowViewModel.Format(entry.Base.ToString()), OutputRowViewModel.Format(entry.Result.ToString()), entry.Chained)
    {
    }

    internal DeadKeyEntryViewModel(DeadKeyViewModel owner, string baseText, string resultText, bool chained)
    {
        _owner = owner;
        _base = baseText;
        _result = resultText;
        _chained = chained;
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    internal (string Base, string Result, bool Chained) RawText => (_base, _result, _chained);

    public ICommand RemoveCommand { get; }

    public string Base { get => _base; set { if (SetProperty(ref _base, value)) _owner.Commit(); } }

    public string Result { get => _result; set { if (SetProperty(ref _result, value)) { OnPropertyChanged(nameof(ResultName)); _owner.Commit(); } } }

    public string ResultName => OutputRowViewModel.Describe(_result);

    public void PickCharacter(string text) => Result = text;

    /// <summary>The result is itself a dead key (e.g. dot below, then macron → ṝ).</summary>
    public bool Chained { get => _chained; set { if (SetProperty(ref _chained, value)) _owner.Commit(); } }

    internal DeadKeyEntry? ToEntry() =>
        OutputRowViewModel.TryParse(_base, out var b) && OutputRowViewModel.TryParse(_result, out var r) && b.Length == 1 && r.Length == 1
            ? new DeadKeyEntry(b[0], r[0], _chained)
            : null;
}

/// <summary>One dead key and its compositions.</summary>
public sealed class DeadKeyViewModel : ObservableObject
{
    private readonly DeadKeysViewModel _owner;
    private readonly DeadKeyDefinition _definition;

    internal DeadKeyViewModel(DeadKeysViewModel owner, DeadKeyDefinition definition)
    {
        _owner = owner;
        _definition = definition;
        foreach (var e in definition.Entries) Entries.Add(new DeadKeyEntryViewModel(this, e));
        AddEntryCommand = new RelayCommand(() =>
        {
            Entries.Add(new DeadKeyEntryViewModel(this, new DeadKeyEntry(' ', definition.Accent)));
            Commit();
        });
    }

    public string Accent => KeyCapViewModel.Display(KeyOutput.Of(_definition.Accent));

    public string AccentCode => $"U+{(int)_definition.Accent:X4}";

    public string? Name
    {
        get => _definition.Name;
        set
        {
            if (_definition.Name == value) return;
            _definition.Name = string.IsNullOrWhiteSpace(value) ? null : value;
            _owner.Changed($"deadname:{(int)_definition.Accent}");
            OnPropertyChanged();
        }
    }

    public ObservableCollection<DeadKeyEntryViewModel> Entries { get; } = [];

    public int Count => Entries.Count;

    public ICommand AddEntryCommand { get; }

    internal void Remove(DeadKeyEntryViewModel entry)
    {
        if (Entries.Remove(entry)) Commit();
    }

    internal char AccentChar => _definition.Accent;

    /// <summary>Writes the rows back to the model; rows that don't parse yet are skipped until fixed.</summary>
    internal void Commit()
    {
        _definition.Entries.Clear();
        foreach (var e in Entries)
        {
            if (e.ToEntry() is { } entry && _definition.Find(entry.Base) is null) _definition.Entries.Add(entry);
        }
        OnPropertyChanged(nameof(Count));
        _owner.Changed($"dead:{(int)_definition.Accent}");
    }
}

/// <summary>The dead-key tab of the editor.</summary>
public sealed class DeadKeysViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private DeadKeyViewModel? _selected;
    private string _newAccent = "";

    internal DeadKeysViewModel(EditorViewModel editor)
    {
        _editor = editor;
        AddDeadKeyCommand = new RelayCommand(AddDeadKey);
        RemoveDeadKeyCommand = new RelayCommand(RemoveSelected, () => _selected is not null);
        AddCommonCommand = new RelayCommand(p => AddCommon(p as string));
    }

    public ICommand RemoveDeadKeyCommand { get; }

    /// <summary>Adds a dead key for a well-known diacritic with all its Latin compositions (parameter: the combining mark).</summary>
    public ICommand AddCommonCommand { get; }

    /// <summary>Well-known diacritics for the "add with compositions" menu.</summary>
    public IReadOnlyList<Kbdking.Core.Unicode.Diacritic> CommonDiacritics => Kbdking.Core.Unicode.Diacritics.All;

    public ObservableCollection<DeadKeyViewModel> DeadKeys { get; } = [];

    public DeadKeyViewModel? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) ((RelayCommand)RemoveDeadKeyCommand).NotifyCanExecuteChanged(); }
    }

    /// <summary>Accent for a new dead key: a character or U+XXXX.</summary>
    public string NewAccent { get => _newAccent; set => SetProperty(ref _newAccent, value); }

    public ICommand AddDeadKeyCommand { get; }

    internal void Load(KeyboardLayout layout)
    {
        var selected = _selected?.AccentChar;
        // Rows still being typed (not valid yet, so not in the layout) survive the reload.
        var unfinished = DeadKeys.GroupBy(d => d.AccentChar).ToDictionary(g => g.Key, g => g.SelectMany(d => d.Entries.Where(e => e.ToEntry() is null).Select(e => e.RawText)).ToList());
        DeadKeys.Clear();
        foreach (var d in layout.DeadKeys)
        {
            var vm = new DeadKeyViewModel(this, d);
            foreach (var (b, r, c) in unfinished.GetValueOrDefault(d.Accent) ?? []) vm.Entries.Add(new DeadKeyEntryViewModel(vm, b, r, c));
            DeadKeys.Add(vm);
        }
        Selected = DeadKeys.FirstOrDefault(d => d.AccentChar == selected) ?? DeadKeys.FirstOrDefault();
    }

    private void AddDeadKey()
    {
        if (!OutputRowViewModel.TryParse(_newAccent, out var accent) || accent.Length != 1) return;
        var def = _editor.Layout.GetOrAddDeadKey(accent[0]);
        Load(_editor.Layout);
        Selected = DeadKeys.First(d => d.AccentChar == def.Accent);
        NewAccent = "";
        Changed();
    }

    private void RemoveSelected()
    {
        if (_selected is null) return;
        var accent = _selected.AccentChar;
        var layout = _editor.Layout;
        var users = layout.Keys.SelectMany(k => k.Outputs.Where(o => o.Value.IsDead && o.Value.Accent == accent).Select(o => (k.VirtualKey, o.Key))).ToList();
        foreach (var (vk, state) in users) layout.SetOutput(vk, state, null);
        foreach (var d in layout.DeadKeys)
            for (var i = d.Entries.Count - 1; i >= 0; i--)
                if (d.Entries[i].Chained && d.Entries[i].Result == accent) d.Entries.RemoveAt(i);
        layout.DeadKeys.RemoveAll(d => d.Accent == accent);
        _editor.LayoutChangedExternally(users.Count == 0 ? "Removed the dead key." : $"Removed the dead key and its {users.Count} key assignment(s).");
    }

    private void AddCommon(string? combining)
    {
        if (combining is not { Length: 1 } || Kbdking.Core.Unicode.Diacritics.Find(combining[0]) is not { } diacritic) return;
        var accent = diacritic.Spacing ?? diacritic.Combining;
        var def = _editor.Layout.GetOrAddDeadKey(accent);
        def.Name ??= diacritic.Name;
        if (def.Find(' ') is null) def.Entries.Add(new DeadKeyEntry(' ', accent));
        var added = 0;
        foreach (var (b, r) in Kbdking.Core.Unicode.Diacritics.Compositions(diacritic.Combining))
        {
            if (def.Find(b) is not null) continue;
            def.Entries.Add(new DeadKeyEntry(b, r));
            added++;
        }
        Load(_editor.Layout);
        Selected = DeadKeys.First(d => d.AccentChar == accent);
        _editor.MarkChanged();
        _editor.Status = $"Dead key {diacritic.Name} with {added} compositions. Put it on a key on the Keys tab (tick \"Dead key\").";
    }

    internal void Changed(string? group = null) => _editor.MarkChanged(group);
}
