using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Keyservant.Core.Model;

namespace Keyservant.Presentation;

// The "advanced" tabs: shift states, scan codes, key names and ligatures. They edit the low-level tables
// directly, so each reads the editor's current layout in Load() and writes through MarkChanged().

/// <summary>One shift-state column of the layout.</summary>
public sealed class ShiftColumnViewModel
{
    internal ShiftColumnViewModel(ShiftStatesViewModel owner, KeyboardLayout layout, int column)
    {
        Column = column;
        State = layout.Modifiers.CanonicalState(column);
        var all = layout.Modifiers.StatesOf(column);
        Name = State.DisplayName;
        AlsoSelectedBy = all.Count > 1 ? "Also: " + string.Join(", ", all.Skip(1).Select(s => s.DisplayName)) : "";
        CharacterCount = layout.Keys.Count(k => k.Outputs.ContainsKey(State));
        CanRemove = State != ShiftState.Base && State != ShiftState.Shift;
        RemoveCommand = new RelayCommand(() => owner.Remove(this), () => CanRemove);
    }

    public int Column { get; }
    public ShiftState State { get; }
    public string Name { get; }
    public string AlsoSelectedBy { get; }
    public int CharacterCount { get; }
    public bool CanRemove { get; }
    public string Summary => $"Column {Column} · {CharacterCount} keys";
    public ICommand RemoveCommand { get; }
}

/// <summary>Shift-state management: which modifier combinations have their own characters, including Kana.</summary>
public sealed class ShiftStatesViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private bool _shift, _ctrl, _alt, _kana;
    private string? _message;

    internal ShiftStatesViewModel(EditorViewModel editor)
    {
        _editor = editor;
        AddCommand = new RelayCommand(Add);
    }

    public ObservableCollection<ShiftColumnViewModel> Columns { get; } = [];

    public bool AddShift { get => _shift; set => SetProperty(ref _shift, value); }
    public bool AddCtrl { get => _ctrl; set => SetProperty(ref _ctrl, value); }
    public bool AddAlt { get => _alt; set => SetProperty(ref _alt, value); }
    public bool AddKana { get => _kana; set => SetProperty(ref _kana, value); }

    public string? Message { get => _message; private set => SetProperty(ref _message, value); }

    public ICommand AddCommand { get; }

    internal void Load()
    {
        var layout = _editor.Layout;
        Columns.Clear();
        for (var c = 0; c < layout.Modifiers.ColumnCount; c++)
        {
            if (layout.Modifiers.StatesOf(c).Count > 0) Columns.Add(new ShiftColumnViewModel(this, layout, c));
        }
    }

    private void Add()
    {
        var bits = ModifierBits.None;
        if (_shift) bits |= ModifierBits.Shift;
        if (_ctrl) bits |= ModifierBits.Ctrl;
        if (_alt) bits |= ModifierBits.Alt;
        if (_kana) bits |= ModifierBits.Kana;
        var state = new ShiftState(bits);
        var layout = _editor.Layout;
        if (layout.Modifiers.ColumnOf(state) is not null) { Message = $"{state.DisplayName} already has a column."; return; }
        if (bits == ModifierBits.Alt) { Message = "Alt alone is reserved by Windows for menus and shortcuts; use AltGr (Ctrl + Alt) instead."; return; }
        layout.Modifiers.EnsureColumn(state);
        if (bits.HasFlag(ModifierBits.AltGr)) layout.Flags |= LocaleFlags.AltGr;
        Message = bits.HasFlag(ModifierBits.Kana) && layout.ScanCodes.FindScanCode(VirtualKeys.Kana) is null && !layout.ScanCodes.E0.Values.Any(t => t.VirtualKey == VirtualKeys.Kana)
            ? $"Added {state.DisplayName}. No key sends KANA yet: assign it to a key on the Scan codes tab (e.g. the right Alt or an unused key)."
            : $"Added {state.DisplayName}. Pick it in the shift state list on the Keys tab to fill it.";
        AddShift = AddCtrl = AddAlt = AddKana = false;
        _editor.LayoutChangedExternally();
    }

    internal void Remove(ShiftColumnViewModel column)
    {
        _editor.Layout.RemoveShiftState(column.State);
        Message = $"Removed {column.Name} and its {column.CharacterCount} characters. Undo brings them back.";
        _editor.LayoutChangedExternally();
    }
}

/// <summary>One scan code → virtual key mapping.</summary>
public sealed class ScanCodeRowViewModel : ObservableObject
{
    private readonly ScanCodesViewModel _owner;
    private string _target;
    private string? _error;

    internal ScanCodeRowViewModel(ScanCodesViewModel owner, int scanCode, bool extended, ScanCodeTarget target, string? keyName)
    {
        _owner = owner;
        ScanCode = scanCode;
        Extended = extended;
        _target = target.ToString();
        KeyName = keyName ?? "";
        RemoveCommand = new RelayCommand(() => _owner.Remove(this), () => Extended);
    }

    public int ScanCode { get; }
    public bool Extended { get; }
    public string Code => Extended ? $"E0 {ScanCode:X2}" : $"{ScanCode:X2}";
    public string KeyName { get; }

    /// <summary>Virtual key name plus optional flags, e.g. <c>OEM_8</c>, <c>RMENU|EXT</c> or <c>NONE</c>.</summary>
    public string Target
    {
        get => _target;
        set
        {
            if (!SetProperty(ref _target, value)) return;
            try
            {
                var parsed = ScanCodeTarget.Parse(value);
                Error = null;
                _owner.Set(this, parsed);
            }
            catch (FormatException ex)
            {
                Error = ex.Message;
            }
        }
    }

    public string? Error { get => _error; private set => SetProperty(ref _error, value); }

    public ICommand RemoveCommand { get; }
}

/// <summary>The low-level scan code table: which physical key sends which virtual key.</summary>
public sealed class ScanCodesViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private string _filter = "";
    private string _newCode = "";
    private string? _message;
    private List<ScanCodeRowViewModel> _all = [];

    internal ScanCodesViewModel(EditorViewModel editor)
    {
        _editor = editor;
        AddCommand = new RelayCommand(Add);
    }

    public ObservableCollection<ScanCodeRowViewModel> Rows { get; } = [];

    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) ApplyFilter(); } }

    /// <summary>A scan code to add, e.g. <c>E0 5C</c> or <c>73</c>.</summary>
    public string NewCode { get => _newCode; set => SetProperty(ref _newCode, value); }

    public string? Message { get => _message; private set => SetProperty(ref _message, value); }

    public ICommand AddCommand { get; }

    internal void Load()
    {
        var layout = _editor.Layout;
        _all = [];
        for (var sc = 1; sc < layout.ScanCodes.Normal.Count; sc++)
            _all.Add(new ScanCodeRowViewModel(this, sc, false, layout.ScanCodes.Normal[sc], layout.KeyNames.GetValueOrDefault((byte)sc)));
        foreach (var (sc, target) in layout.ScanCodes.E0)
            _all.Add(new ScanCodeRowViewModel(this, sc, true, target, layout.KeyNamesExt.GetValueOrDefault(sc)));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        var f = _filter.Trim();
        foreach (var r in _all)
        {
            if (f.Length > 0 && !r.Code.Contains(f, StringComparison.OrdinalIgnoreCase) && !r.Target.Contains(f, StringComparison.OrdinalIgnoreCase)
                && !r.KeyName.Contains(f, StringComparison.CurrentCultureIgnoreCase)) continue;
            Rows.Add(r);
        }
    }

    internal void Set(ScanCodeRowViewModel row, ScanCodeTarget target)
    {
        var layout = _editor.Layout;
        if (row.Extended) layout.ScanCodes.E0[(byte)row.ScanCode] = target;
        else layout.ScanCodes.Set(row.ScanCode, target);
        _editor.MarkChanged($"scan:{row.Code}");
        _editor.RefreshKeyboard();
    }

    internal void Remove(ScanCodeRowViewModel row)
    {
        if (!row.Extended) return;
        _editor.Layout.ScanCodes.E0.Remove((byte)row.ScanCode);
        _editor.MarkChanged();
        Load();
        _editor.RefreshKeyboard();
    }

    private void Add()
    {
        var text = _newCode.Trim();
        var extended = text.StartsWith("E0", StringComparison.OrdinalIgnoreCase) && text.Length > 2;
        var hex = extended ? text[2..].Trim() : text;
        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var sc) || sc is < 1 or > 0x7F)
        {
            Message = "Give a scan code in hex, e.g. 73 or E0 5C.";
            return;
        }
        var layout = _editor.Layout;
        if (extended) layout.ScanCodes.E0.TryAdd((byte)sc, ScanCodeTarget.None);
        else if (sc >= layout.ScanCodes.Normal.Count) layout.ScanCodes.Set(sc, ScanCodeTarget.None);
        Message = $"Added {(extended ? "E0 " : "")}{sc:X2}. Set its virtual key in the list.";
        NewCode = "";
        _editor.MarkChanged();
        Load();
    }
}

/// <summary>A key name Windows shows in shortcut settings (GetKeyNameText), e.g. "STRG" for Ctrl.</summary>
public sealed class KeyNameRowViewModel : ObservableObject
{
    private readonly KeyNamesViewModel _owner;
    private string _name;

    internal KeyNameRowViewModel(KeyNamesViewModel owner, byte scanCode, bool extended, string name)
    {
        _owner = owner;
        ScanCode = scanCode;
        Extended = extended;
        _name = name;
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    public byte ScanCode { get; }
    public bool Extended { get; }
    public string Code => Extended ? $"E0 {ScanCode:X2}" : $"{ScanCode:X2}";

    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value)) _owner.Set(this); }
    }

    public ICommand RemoveCommand { get; }
}

/// <summary>The key-name tables: names for keys without characters (Esc, Enter, F1...), in the layout's language.</summary>
public sealed class KeyNamesViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private string _newCode = "";
    private string _newName = "";
    private string? _message;

    internal KeyNamesViewModel(EditorViewModel editor)
    {
        _editor = editor;
        AddCommand = new RelayCommand(Add);
        TranslateCommand = new RelayCommand(p => Translate(p as string));
    }

    public ObservableCollection<KeyNameRowViewModel> Rows { get; } = [];

    public string NewCode { get => _newCode; set => SetProperty(ref _newCode, value); }
    public string NewName { get => _newName; set => SetProperty(ref _newName, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }

    public ICommand AddCommand { get; }

    /// <summary>Replaces the names with a standard set (parameter "en" or "de").</summary>
    public ICommand TranslateCommand { get; }

    internal void Load()
    {
        var layout = _editor.Layout;
        Rows.Clear();
        foreach (var (sc, name) in layout.KeyNames) Rows.Add(new KeyNameRowViewModel(this, sc, false, name));
        foreach (var (sc, name) in layout.KeyNamesExt) Rows.Add(new KeyNameRowViewModel(this, sc, true, name));
    }

    internal void Set(KeyNameRowViewModel row)
    {
        var table = row.Extended ? _editor.Layout.KeyNamesExt : _editor.Layout.KeyNames;
        if (string.IsNullOrEmpty(row.Name)) table.Remove(row.ScanCode);
        else table[row.ScanCode] = row.Name;
        _editor.MarkChanged($"keyname:{row.Code}");
    }

    internal void Remove(KeyNameRowViewModel row)
    {
        (row.Extended ? _editor.Layout.KeyNamesExt : _editor.Layout.KeyNames).Remove(row.ScanCode);
        _editor.MarkChanged();
        Load();
    }

    private void Add()
    {
        var text = _newCode.Trim();
        var extended = text.StartsWith("E0", StringComparison.OrdinalIgnoreCase) && text.Length > 2;
        var hex = extended ? text[2..].Trim() : text;
        if (!byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var sc) || _newName.Trim().Length == 0)
        {
            Message = "Give a scan code in hex (e.g. 01 or E0 1C) and a name.";
            return;
        }
        (extended ? _editor.Layout.KeyNamesExt : _editor.Layout.KeyNames)[sc] = _newName.Trim();
        NewCode = NewName = "";
        Message = null;
        _editor.MarkChanged();
        Load();
    }

    private void Translate(string? language)
    {
        var layout = _editor.Layout;
        var (names, ext) = StandardKeyNames.For(language ?? "en");
        layout.KeyNames.Clear();
        layout.KeyNamesExt.Clear();
        foreach (var (k, v) in names) layout.KeyNames[k] = v;
        foreach (var (k, v) in ext) layout.KeyNamesExt[k] = v;
        Message = $"Replaced the key names with the standard {(language == "de" ? "German" : "English")} names.";
        _editor.MarkChanged();
        Load();
    }
}

/// <summary>Standard key names as Windows' own layouts use them.</summary>
internal static class StandardKeyNames
{
    public static (Dictionary<byte, string> Normal, Dictionary<byte, string> Extended) For(string language)
    {
        var de = language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        var n = new Dictionary<byte, string>
        {
            [0x01] = de ? "ESC" : "Esc", [0x0E] = de ? "RÜCKTASTE" : "Backspace", [0x0F] = de ? "TABULATOR" : "Tab",
            [0x1C] = de ? "EINGABE" : "Enter", [0x1D] = de ? "STRG" : "Ctrl", [0x2A] = de ? "UMSCHALT" : "Shift",
            [0x36] = de ? "UMSCHALT RECHTS" : "Right Shift", [0x37] = de ? "* (ZEHNERTASTATUR)" : "Num *",
            [0x38] = "Alt", [0x39] = de ? "LEER" : "Space", [0x3A] = de ? "FESTSTELL" : "Caps Lock",
            [0x45] = de ? "PAUSE" : "Pause", [0x46] = de ? "ROLLEN-FESTSTELL" : "Scroll Lock",
            [0x47] = de ? "7 (ZEHNERTASTATUR)" : "Num 7", [0x48] = de ? "8 (ZEHNERTASTATUR)" : "Num 8",
            [0x49] = de ? "9 (ZEHNERTASTATUR)" : "Num 9", [0x4A] = de ? "- (ZEHNERTASTATUR)" : "Num -",
            [0x4B] = de ? "4 (ZEHNERTASTATUR)" : "Num 4", [0x4C] = de ? "5 (ZEHNERTASTATUR)" : "Num 5",
            [0x4D] = de ? "6 (ZEHNERTASTATUR)" : "Num 6", [0x4E] = de ? "+ (ZEHNERTASTATUR)" : "Num +",
            [0x4F] = de ? "1 (ZEHNERTASTATUR)" : "Num 1", [0x50] = de ? "2 (ZEHNERTASTATUR)" : "Num 2",
            [0x51] = de ? "3 (ZEHNERTASTATUR)" : "Num 3", [0x52] = de ? "0 (ZEHNERTASTATUR)" : "Num 0",
            [0x53] = de ? "KOMMA (ZEHNERTASTATUR)" : "Num Del",
        };
        for (var i = 0; i < 10; i++) n[(byte)(0x3B + i)] = "F" + (i + 1);
        n[0x57] = "F11";
        n[0x58] = "F12";
        var e = new Dictionary<byte, string>
        {
            [0x1C] = de ? "EINGABE (ZEHNERTASTATUR)" : "Num Enter", [0x1D] = de ? "STRG-RECHTS" : "Right Ctrl",
            [0x35] = de ? "/ (ZEHNERTASTATUR)" : "Num /", [0x37] = de ? "DRUCK" : "Prnt Scrn", [0x38] = de ? "ALT GR" : "Right Alt",
            [0x45] = de ? "NUM-FESTSTELL" : "Num Lock", [0x46] = de ? "UNTBR" : "Break", [0x47] = de ? "POS1" : "Home",
            [0x48] = de ? "NACH-OBEN" : "Up", [0x49] = de ? "BILD-NACH-OBEN" : "Page Up", [0x4B] = de ? "NACH-LINKS" : "Left",
            [0x4D] = de ? "NACH-RECHTS" : "Right", [0x4F] = de ? "ENDE" : "End", [0x50] = de ? "NACH-UNTEN" : "Down",
            [0x51] = de ? "BILD-NACH-UNTEN" : "Page Down", [0x52] = de ? "EINFG" : "Insert", [0x53] = de ? "ENTF" : "Delete",
            [0x54] = "<00>", [0x56] = de ? "HILFE" : "Help", [0x5B] = de ? "LINKE WINDOWS" : "Left Windows",
            [0x5C] = de ? "RECHTE WINDOWS" : "Right Windows", [0x5D] = de ? "ANWENDUNG" : "Application",
        };
        return (n, e);
    }
}

/// <summary>One ligature: a key that types several characters at once.</summary>
public sealed class LigatureRowViewModel : ObservableObject
{
    private readonly LigaturesViewModel _owner;
    private string _text;
    private string? _error;

    internal LigatureRowViewModel(LigaturesViewModel owner, byte vk, ShiftState state, string text)
    {
        _owner = owner;
        VirtualKey = vk;
        State = state;
        _text = OutputRowViewModel.Format(text);
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    public byte VirtualKey { get; }
    public ShiftState State { get; }
    public string KeyName => VirtualKeys.GetName(VirtualKey);
    public string StateName => State.DisplayName;

    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value)) return;
            OnPropertyChanged(nameof(CodePoints));
            Error = _owner.Set(this);
        }
    }

    public string CodePoints => OutputRowViewModel.Describe(_text);

    public string? Error { get => _error; private set => SetProperty(ref _error, value); }

    public ICommand RemoveCommand { get; }
}

/// <summary>
/// All ligatures in one place. A ligature types a whole sequence (e.g. "ṛ" written as r + U+0323, or "ng")
/// with one keystroke; Windows allows up to 16 UTF-16 units, MSKLC up to 4.
/// </summary>
public sealed class LigaturesViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private string _newKey = "";
    private ShiftStateOption? _newState;
    private string _newText = "";
    private string? _message;

    internal LigaturesViewModel(EditorViewModel editor)
    {
        _editor = editor;
        AddCommand = new RelayCommand(Add);
    }

    public ObservableCollection<LigatureRowViewModel> Rows { get; } = [];

    /// <summary>The key for a new ligature: a character it types now (e.g. "q") or a virtual key name.</summary>
    public string NewKey { get => _newKey; set => SetProperty(ref _newKey, value); }

    public ShiftStateOption? NewState { get => _newState; set => SetProperty(ref _newState, value); }

    public string NewText { get => _newText; set => SetProperty(ref _newText, value); }

    public string? Message { get => _message; private set => SetProperty(ref _message, value); }

    public ICommand AddCommand { get; }

    internal void Load()
    {
        Rows.Clear();
        foreach (var key in _editor.Layout.Keys)
        {
            foreach (var (state, output) in key.Outputs)
            {
                if (output.IsLigature) Rows.Add(new LigatureRowViewModel(this, key.VirtualKey, state, output.Text));
            }
        }
        OnPropertyChanged(nameof(Count));
    }

    public int Count => Rows.Count;

    internal string? Set(LigatureRowViewModel row)
    {
        if (!OutputRowViewModel.TryParse(row.Text, out var text)) return "Use characters, or codes like U+0323.";
        if (text.Length == 0) return "Empty; use the delete button to remove the ligature.";
        if (text.Length > Keyservant.Core.Validation.LayoutValidator.MaxLigature) return $"At most {Keyservant.Core.Validation.LayoutValidator.MaxLigature} UTF-16 units.";
        _editor.Layout.SetOutput(row.VirtualKey, row.State, KeyOutput.Of(text));
        _editor.MarkChanged($"lig:{row.VirtualKey}:{row.State.Value}");
        _editor.RefreshKeyboard();
        return text.Length > Keyservant.Core.Validation.LayoutValidator.RecommendedMaxLigature ? "Longer than 4 units: works in Windows, but MSKLC can't open it." : null;
    }

    internal void Remove(LigatureRowViewModel row)
    {
        _editor.Layout.SetOutput(row.VirtualKey, row.State, null);
        _editor.MarkChanged();
        _editor.RefreshKeyboard();
        Load();
    }

    private void Add()
    {
        var layout = _editor.Layout;
        if (!TryFindKey(layout, _newKey, out var vk)) { Message = "Name the key by what it types now (e.g. q) or its virtual key (e.g. OEM_8)."; return; }
        if (!OutputRowViewModel.TryParse(_newText, out var text) || text.Length < 2) { Message = "A ligature has at least two UTF-16 units, e.g. ng or r + U+0323."; return; }
        var state = _newState?.State ?? ShiftState.Base;
        layout.SetOutput(vk, state, KeyOutput.Of(text));
        Message = $"{VirtualKeys.GetName(vk)} ({state.DisplayName}) now types {text}.";
        NewText = "";
        _editor.MarkChanged();
        _editor.RefreshKeyboard();
        Load();
    }

    internal static bool TryFindKey(KeyboardLayout layout, string text, out byte vk)
    {
        text = text.Trim();
        if (text.Length == 1 && layout.Keys.FirstOrDefault(k => k.Outputs.GetValueOrDefault(ShiftState.Base)?.Text == text) is { } byChar)
        {
            vk = byChar.VirtualKey;
            return true;
        }
        return VirtualKeys.TryParse(text, out vk);
    }
}
