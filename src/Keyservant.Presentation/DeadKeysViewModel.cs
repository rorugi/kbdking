using System.Collections.ObjectModel;
using System.Windows.Input;
using Keyservant.Core.Model;

namespace Keyservant.Presentation;

/// <summary>One composition row: base → result.</summary>
public sealed class DeadKeyEntryViewModel : ObservableObject
{
    private readonly DeadKeyViewModel _owner;
    private string _base;
    private string _result;
    private bool _chained;

    internal DeadKeyEntryViewModel(DeadKeyViewModel owner, DeadKeyEntry entry)
    {
        _owner = owner;
        _base = OutputRowViewModel.Format(entry.Base.ToString());
        _result = OutputRowViewModel.Format(entry.Result.ToString());
        _chained = entry.Chained;
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    public ICommand RemoveCommand { get; }

    public string Base { get => _base; set { if (SetProperty(ref _base, value)) _owner.Commit(); } }

    public string Result { get => _result; set { if (SetProperty(ref _result, value)) _owner.Commit(); } }

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
            _owner.Changed();
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
        _owner.Changed();
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
    }

    public ObservableCollection<DeadKeyViewModel> DeadKeys { get; } = [];

    public DeadKeyViewModel? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    /// <summary>Accent for a new dead key: a character or U+XXXX.</summary>
    public string NewAccent { get => _newAccent; set => SetProperty(ref _newAccent, value); }

    public ICommand AddDeadKeyCommand { get; }

    internal void Load(KeyboardLayout layout)
    {
        var selected = _selected?.AccentChar;
        DeadKeys.Clear();
        foreach (var d in layout.DeadKeys) DeadKeys.Add(new DeadKeyViewModel(this, d));
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

    internal void Changed() => _editor.MarkChanged();
}
