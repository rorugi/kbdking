using System.Collections.ObjectModel;
using System.Windows.Input;
using Keyservant.Binary;
using Keyservant.Binary.Packaging;
using Keyservant.Core.Formats;
using Keyservant.Core.Model;
using Keyservant.Core.Templates;
using Keyservant.Core.Validation;

namespace Keyservant.Presentation;

/// <summary>A shift state the editor can show, e.g. "Shift + AltGr".</summary>
public sealed record ShiftStateOption(ShiftState State)
{
    public string Label => State.DisplayName;
    public override string ToString() => Label;
}

/// <summary>One row of the on-screen keyboard.</summary>
public sealed class KeyboardRowViewModel
{
    public ObservableCollection<KeyCapViewModel> Keys { get; } = [];
}

/// <summary>The layout editor: keyboard view, key editing, dead keys, metadata, validation, live preview and build.</summary>
public sealed class EditorViewModel : ObservableObject
{
    private static readonly ShiftState[] StandardStates =
        [ShiftState.Base, ShiftState.Shift, ShiftState.AltGr, ShiftState.ShiftAltGr, ShiftState.Ctrl, ShiftState.ShiftCtrl];

    private readonly IDialogService _dialogs;
    private readonly IPrivilegedOperations _privileged;
    private readonly LayoutFormatRegistry _formats = LayoutDllFormat.CreateRegistry();
    private readonly List<KeyCapViewModel> _allKeys = [];
    private TypingSimulator _simulator;
    private KeyboardLayout _layout;
    private string? _filePath;
    private bool _isDirty;
    private ShiftStateOption _selectedState;
    private KeyCapViewModel? _selectedKey;
    private KeyEditorViewModel? _keyEditor;
    private KeyboardShape _shape = KeyboardShape.Iso;
    private string _previewText = "";
    private string? _status;

    public EditorViewModel(IDialogService dialogs, IPrivilegedOperations privileged)
    {
        _dialogs = dialogs;
        _privileged = privileged;
        _layout = LayoutTemplates.German("de-AT");
        _simulator = new TypingSimulator(_layout);
        _selectedState = new ShiftStateOption(ShiftState.Base);
        DeadKeys = new DeadKeysViewModel(this);

        NewCommand = new RelayCommand(p => New(p as string ?? "en-US"));
        OpenCommand = new AsyncRelayCommand(OpenAsync, onError: ReportError);
        SaveCommand = new AsyncRelayCommand(() => SaveAsync(saveAs: false), onError: ReportError);
        SaveAsCommand = new AsyncRelayCommand(() => SaveAsync(saveAs: true), onError: ReportError);
        ExportCommand = new AsyncRelayCommand(ExportAsync, onError: ReportError);
        ValidateCommand = new RelayCommand(Validate);
        BuildCommand = new AsyncRelayCommand(BuildAsync, onError: ReportError);
        InstallCommand = new AsyncRelayCommand(InstallAsync, onError: ReportError);
        PackageCommand = new AsyncRelayCommand(PackageAsync, onError: ReportError);
        ClearPreviewCommand = new RelayCommand(() => { PreviewText = ""; _simulator.Reset(); });
        Load(_layout, null);
    }

    // ---------------------------------------------------------------- state

    public KeyboardLayout Layout => _layout;

    public string Title => $"{_layout.Metadata.Name}{(_isDirty ? " •" : "")} — {(_filePath is null ? "not saved" : Path.GetFileName(_filePath))}";

    public bool IsDirty { get => _isDirty; private set { if (SetProperty(ref _isDirty, value)) OnPropertyChanged(nameof(Title)); } }

    public string? Status { get => _status; private set => SetProperty(ref _status, value); }

    public ObservableCollection<KeyboardRowViewModel> KeyboardRows { get; } = [];

    public ObservableCollection<ShiftStateOption> ShiftStates { get; } = [];

    public ShiftStateOption SelectedState
    {
        get => _selectedState;
        set
        {
            if (value is null || !SetProperty(ref _selectedState, value)) return;
            RefreshKeys();
        }
    }

    public KeyboardShape Shape
    {
        get => _shape;
        set { if (SetProperty(ref _shape, value)) BuildKeyboard(); }
    }

    public bool IsIso { get => _shape == KeyboardShape.Iso; set => Shape = value ? KeyboardShape.Iso : KeyboardShape.Ansi; }

    public KeyEditorViewModel? KeyEditor { get => _keyEditor; private set => SetProperty(ref _keyEditor, value); }

    public DeadKeysViewModel DeadKeys { get; }

    public ObservableCollection<ValidationIssue> Issues { get; } = [];

    public string IssueSummary
    {
        get
        {
            var e = Issues.Count(i => i.Severity == Severity.Error);
            var w = Issues.Count(i => i.Severity == Severity.Warning);
            return e == 0 && w == 0 ? "No problems found." : $"{e} error(s), {w} warning(s)";
        }
    }

    public string PreviewText { get => _previewText; set => SetProperty(ref _previewText, value); }

    public IReadOnlyList<LayoutTemplate> Templates => LayoutTemplates.All;

    // Metadata (bound to the "Layout" tab)
    public string LayoutName { get => _layout.Metadata.Name; set { _layout.Metadata.Name = value; MarkChanged(); OnPropertyChanged(nameof(Title)); } }
    public string DllName { get => _layout.Metadata.DllName; set { _layout.Metadata.DllName = value; MarkChanged(); } }
    public string Locale { get => _layout.Metadata.Locale; set { _layout.Metadata.Locale = value; MarkChanged(); } }
    public string LayoutVersion { get => _layout.Metadata.Version; set { _layout.Metadata.Version = value; MarkChanged(); } }
    public string? Author { get => _layout.Metadata.Author; set { _layout.Metadata.Author = value; MarkChanged(); } }
    public string? Description { get => _layout.Metadata.Description; set { _layout.Metadata.Description = value; MarkChanged(); } }
    public string? LayoutId => _layout.Metadata.LayoutId;

    public bool AltGrFlag
    {
        get => _layout.Flags.HasFlag(LocaleFlags.AltGr);
        set { _layout.Flags = value ? _layout.Flags | LocaleFlags.AltGr : _layout.Flags & ~LocaleFlags.AltGr; MarkChanged(); RefreshKeys(); }
    }

    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand BuildCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand PackageCommand { get; }
    public ICommand ClearPreviewCommand { get; }

    // ---------------------------------------------------------------- loading

    public void New(string templateId)
    {
        var layout = LayoutTemplates.Create(templateId);
        layout.Metadata.Name = $"{layout.Metadata.Name} (custom)";
        Load(layout, null);
        IsDirty = true;
    }

    /// <summary>Opens any readable format: .keyservant, .klc or a layout DLL.</summary>
    public void OpenFile(string path)
    {
        var report = new ConversionReport();
        var layout = _formats.ReadFile(path, report);
        var isNative = path.EndsWith(".keyservant", StringComparison.OrdinalIgnoreCase);
        if (!isNative)
        {
            // Imported copies get a fresh identity so they don't replace the original when installed.
            layout.Metadata.LayoutId = null;
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) layout.Metadata.DllName = "kbdks";
        }
        Load(layout, isNative ? path : null);
        IsDirty = !isNative;
        Status = report.IsLossless ? $"Opened {Path.GetFileName(path)}." : $"Opened {Path.GetFileName(path)} with {report.Warnings.Count} warning(s): {string.Join(" ", report.Warnings)}";
    }

    private void Load(KeyboardLayout layout, string? path)
    {
        _layout = layout;
        _filePath = path;
        _simulator = new TypingSimulator(layout);
        PreviewText = "";

        ShiftStates.Clear();
        foreach (var s in StandardStates.Concat(layout.Modifiers.Columns).Distinct())
            ShiftStates.Add(new ShiftStateOption(s));
        _selectedState = ShiftStates[0];
        OnPropertyChanged(nameof(SelectedState));

        BuildKeyboard();
        DeadKeys.Load(layout);
        KeyEditor = null;
        IsDirty = false;
        foreach (var p in new[] { nameof(LayoutName), nameof(DllName), nameof(Locale), nameof(LayoutVersion), nameof(Author), nameof(Description), nameof(LayoutId), nameof(AltGrFlag), nameof(Title) })
            OnPropertyChanged(p);
        Validate();
    }

    private void BuildKeyboard()
    {
        KeyboardRows.Clear();
        _allKeys.Clear();
        foreach (var row in KeyboardGeometry.Rows(_shape))
        {
            var r = new KeyboardRowViewModel();
            foreach (var k in row)
            {
                var cap = new KeyCapViewModel(this, k);
                r.Keys.Add(cap);
                _allKeys.Add(cap);
            }
            KeyboardRows.Add(r);
        }
        RefreshKeys();
        OnPropertyChanged(nameof(IsIso));
    }

    private void RefreshKeys()
    {
        foreach (var k in _allKeys) k.Refresh(_layout, _selectedState.State);
    }

    // ---------------------------------------------------------------- editing

    internal void SelectKey(KeyCapViewModel cap)
    {
        if (_selectedKey is not null) _selectedKey.IsSelected = false;
        _selectedKey = cap;
        cap.IsSelected = true;
        if (cap.VirtualKey is not { } vk || !cap.IsCharacterKey)
        {
            KeyEditor = null;
            Status = cap.Key.FixedLabel is { } label ? $"{label} is a modifier or control key and has no characters." : "This key has no virtual key in this layout.";
            return;
        }
        KeyEditor = new KeyEditorViewModel(this, _layout, _layout.GetOrAddKey(vk), ShiftStates.Select(s => s.State));
    }

    internal void SetOutput(byte vk, ShiftState state, KeyOutput? output)
    {
        _layout.SetOutput(vk, state, output);
        if (output is { IsDead: true }) DeadKeys.Load(_layout);
        MarkChanged();
        RefreshKeys();
    }

    internal void MarkChanged()
    {
        IsDirty = true;
        Validate();
    }

    private void Validate()
    {
        Issues.Clear();
        foreach (var i in LayoutValidator.Validate(_layout).Issues.OrderByDescending(i => i.Severity)) Issues.Add(i);
        OnPropertyChanged(nameof(IssueSummary));
    }

    // ---------------------------------------------------------------- live preview

    /// <summary>
    /// Handles a keystroke in the preview box, using the layout being edited instead of the active
    /// Windows layout. Returns true if the key was handled.
    /// </summary>
    public bool HandlePreviewKey(int scanCode, bool extended, bool shift, bool ctrl, bool alt, bool capsLock)
    {
        if (scanCode == 0x0E && !extended) // Backspace
        {
            if (PreviewText.Length > 0) PreviewText = PreviewText[..^(char.IsLowSurrogate(PreviewText[^1]) && PreviewText.Length > 1 ? 2 : 1)];
            _simulator.Reset();
            return true;
        }
        var bits = ModifierBits.None;
        if (shift) bits |= ModifierBits.Shift;
        if (ctrl) bits |= ModifierBits.Ctrl;
        if (alt) bits |= ModifierBits.Alt;
        var text = _simulator.PressScanCode(scanCode, extended, new ShiftState(bits), capsLock);
        if (text.Length == 0) return _simulator.PendingDeadKey is not null;
        // Control characters (Enter, Tab, Ctrl+letter) are not useful in the preview, except new lines.
        if (text is "\r") text = Environment.NewLine;
        else if (text.Any(char.IsControl) && text != "\t") return true;
        PreviewText += text;
        return true;
    }

    // ---------------------------------------------------------------- files

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var path = await _dialogs.PickOpenFileAsync([".keyservant", ".klc", ".dll"]);
        if (path is not null) OpenFile(path);
    }

    private async Task<bool> ConfirmDiscardAsync() =>
        !_isDirty || await _dialogs.ConfirmAsync("Discard changes?", "The current layout has unsaved changes.", "Discard");

    /// <summary>Saves to the current file, or asks for one. Returns false if cancelled.</summary>
    public async Task<bool> SaveAsync(bool saveAs)
    {
        var path = _filePath;
        if (saveAs || path is null)
        {
            path = await _dialogs.PickSaveFileAsync(_layout.Metadata.DllName + ".keyservant", [".keyservant"]);
            if (path is null) return false;
        }
        await File.WriteAllTextAsync(path, KeyservantFormat.Serialize(_layout));
        _filePath = path;
        IsDirty = false;
        OnPropertyChanged(nameof(Title));
        Status = $"Saved {Path.GetFileName(path)}.";
        return true;
    }

    private async Task ExportAsync()
    {
        var path = await _dialogs.PickSaveFileAsync(_layout.Metadata.DllName + ".klc", [".klc", ".c"]);
        if (path is null) return;
        var report = new ConversionReport();
        _formats.WriteFile(_layout, path, report);
        Status = report.IsLossless ? $"Exported {Path.GetFileName(path)}." : $"Exported with {report.Warnings.Count} warning(s): {string.Join(" ", report.Warnings)}";
    }

    private async Task BuildAsync()
    {
        var folder = await _dialogs.PickFolderAsync();
        if (folder is null) return;
        var result = LayoutDllBuilder.Build(_layout);
        Directory.CreateDirectory(Path.Combine(folder, "x64"));
        await File.WriteAllBytesAsync(Path.Combine(folder, "x64", result.FileName), result.X64);
        if (result.Wow64 is not null)
        {
            Directory.CreateDirectory(Path.Combine(folder, "wow64"));
            await File.WriteAllBytesAsync(Path.Combine(folder, "wow64", result.FileName), result.Wow64);
        }
        Status = $"Built {result.FileName} (64-bit and 32-bit) in {folder}.";
    }

    private async Task InstallAsync()
    {
        if (!EnsureBuildable()) return;
        if ((_isDirty || _filePath is null) && !await SaveAsync(saveAs: false)) return;
        Status = "Waiting for administrator approval...";
        var result = await _privileged.InstallAsync(_filePath!);
        if (result.Cancelled) { Status = "Cancelled."; return; }
        if (!result.Succeeded) { await _dialogs.ShowMessageAsync("Installation failed", result.Output); Status = "Installation failed."; return; }
        // The installer stores the assigned layout ID in the file; reload it so the next install updates in place.
        var saved = KeyservantFormat.Parse(await File.ReadAllTextAsync(_filePath!));
        _layout.Metadata.LayoutId = saved.Metadata.LayoutId;
        OnPropertyChanged(nameof(LayoutId));
        Status = result.Output.Trim();
    }

    private async Task PackageAsync()
    {
        if (!EnsureBuildable()) return;
        var stub = Path.Combine(AppContext.BaseDirectory, "Keyservant.Stub.exe");
        if (!File.Exists(stub)) { await _dialogs.ShowMessageAsync("Installer stub missing", $"Keyservant.Stub.exe was not found next to the app ({AppContext.BaseDirectory})."); return; }
        var path = await _dialogs.PickSaveFileAsync($"{_layout.Metadata.DllName}-setup.exe", [".exe"]);
        if (path is null) return;
        var result = LayoutDllBuilder.Build(_layout);
        LayoutPackage.Create(_layout, result).WriteInstaller(stub, path);
        Status = $"Created {Path.GetFileName(path)}. Run it on any Windows PC to install the layout.";
    }

    private bool EnsureBuildable()
    {
        Validate();
        if (!Issues.Any(i => i.Severity == Severity.Error)) return true;
        Status = "Fix the errors in the Problems list first.";
        _ = _dialogs.ShowMessageAsync("The layout has errors", string.Join(Environment.NewLine, Issues.Where(i => i.Severity == Severity.Error).Select(i => i.Message)));
        return false;
    }

    private void ReportError(Exception ex)
    {
        Status = ex.Message;
        _ = _dialogs.ShowMessageAsync("Something went wrong", ex.Message);
    }
}
