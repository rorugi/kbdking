using System.Collections.ObjectModel;
using System.Windows.Input;
using Keyservant.Binary;
using Keyservant.Binary.Packaging;
using Keyservant.Core.Compose;
using Keyservant.Core.Editing;
using Keyservant.Core.Formats;
using Keyservant.Core.Geometry;
using Keyservant.Core.Model;
using Keyservant.Core.Rendering;
using Keyservant.Core.Templates;
using Keyservant.Core.Validation;

namespace Keyservant.Presentation;

/// <summary>A shift state the editor can show, e.g. "Shift + AltGr".</summary>
public sealed record ShiftStateOption(ShiftState State)
{
    public string Label => State.DisplayName;
    public override string ToString() => Label;
}

/// <summary>A physical keyboard shape for the on-screen keyboard and printouts.</summary>
public sealed record KeyboardShapeOption(KeyboardShape Shape)
{
    public string Label => KeyboardGeometry.DisplayName(Shape);
    public override string ToString() => Label;
}

/// <summary>One row of the on-screen keyboard.</summary>
public sealed class KeyboardRowViewModel
{
    public ObservableCollection<KeyCapViewModel> Keys { get; } = [];
}

/// <summary>
/// The layout editor: keyboard view, key editing, dead keys, metadata, validation, live preview and build,
/// plus the tools on the other tabs (compose, assistant, analysis, tests, compare, scan codes, ...).
/// Every change goes through <see cref="MarkChanged"/>, which records an undo step.
/// </summary>
public sealed class EditorViewModel : ObservableObject
{
    private static readonly ShiftState[] StandardStates =
        [ShiftState.Base, ShiftState.Shift, ShiftState.AltGr, ShiftState.ShiftAltGr, ShiftState.Ctrl, ShiftState.ShiftCtrl];

    private readonly IDialogService _dialogs;
    private readonly IPrivilegedOperations _privileged;
    private readonly LayoutFormatRegistry _formats = LayoutDllFormat.CreateRegistry();
    private readonly List<KeyCapViewModel> _allKeys = [];
    private readonly LayoutHistory _history;
    private TypingSimulator? _simulator;
    private KeyboardLayout _layout;
    private string? _filePath;
    private bool _isDirty;
    private ShiftStateOption _selectedState;
    private KeyCapViewModel? _selectedKey;
    private KeyEditorViewModel? _keyEditor;
    private KeyboardShapeOption _shape;
    private string _previewText = "";
    private string? _status;
    private int _testMinutes = 30;

    public EditorViewModel(IDialogService dialogs, IPrivilegedOperations privileged)
    {
        _dialogs = dialogs;
        _privileged = privileged;
        _layout = LayoutTemplates.German("de-AT");
        _history = new LayoutHistory(_layout);
        _history.Changed += (_, _) => { UndoCommand?.NotifyCanExecuteChanged(); RedoCommand?.NotifyCanExecuteChanged(); };
        _selectedState = new ShiftStateOption(ShiftState.Base);
        _shape = Shapes[0];

        DeadKeys = new DeadKeysViewModel(this);
        Unicode = new UnicodePickerViewModel();
        ShiftStateEditor = new ShiftStatesViewModel(this);
        ScanCodes = new ScanCodesViewModel(this);
        KeyNames = new KeyNamesViewModel(this);
        Ligatures = new LigaturesViewModel(this);
        Compose = new ComposeViewModel(this);
        Assistant = new AssistantViewModel(this);
        Analysis = new AnalysisViewModel(this);
        Tests = new TestsViewModel(this);
        Compare = new CompareViewModel(this);

        NewCommand = new RelayCommand(p => New(p as string ?? "en-US"));
        OpenCommand = new AsyncRelayCommand(OpenAsync, onError: ReportError);
        SaveCommand = new AsyncRelayCommand(() => SaveAsync(saveAs: false), onError: ReportError);
        SaveAsCommand = new AsyncRelayCommand(() => SaveAsync(saveAs: true), onError: ReportError);
        ExportCommand = new AsyncRelayCommand(ExportAsync, onError: ReportError);
        ExportPictureCommand = new AsyncRelayCommand(() => ExportSvgAsync(RenderMode.Keyboard), onError: ReportError);
        ExportStickersCommand = new AsyncRelayCommand(() => ExportSvgAsync(RenderMode.Stickers), onError: ReportError);
        ValidateCommand = new RelayCommand(Validate);
        BuildCommand = new AsyncRelayCommand(BuildAsync, onError: ReportError);
        InstallCommand = new AsyncRelayCommand(InstallAsync, onError: ReportError);
        TestInstallCommand = new AsyncRelayCommand(TestInstallAsync, onError: ReportError);
        PackageCommand = new AsyncRelayCommand(PackageAsync, onError: ReportError);
        ClearPreviewCommand = new RelayCommand(() => { PreviewText = ""; _simulator?.Reset(); });
        UndoCommand = new RelayCommand(Undo, () => _history.CanUndo);
        RedoCommand = new RelayCommand(Redo, () => _history.CanRedo);
        Load(_layout, null, resetHistory: true);
    }

    // ---------------------------------------------------------------- state

    public KeyboardLayout Layout => _layout;

    internal IDialogService Dialogs => _dialogs;

    internal LayoutFormatRegistry Formats => _formats;

    internal string? FilePath => _filePath;

    public string Title => $"{_layout.Metadata.Name}{(_isDirty ? " •" : "")} — {(_filePath is null ? "not saved" : Path.GetFileName(_filePath))}";

    public bool IsDirty { get => _isDirty; private set { if (SetProperty(ref _isDirty, value)) OnPropertyChanged(nameof(Title)); } }

    public string? Status { get => _status; internal set => SetProperty(ref _status, value); }

    public ObservableCollection<KeyboardRowViewModel> KeyboardRows { get; } = [];

    public ObservableCollection<ShiftStateOption> ShiftStates { get; } = [];

    public ShiftStateOption SelectedState
    {
        get => _selectedState;
        set
        {
            if (value is null || !SetProperty(ref _selectedState, value)) return;
            RefreshKeyboard();
        }
    }

    public IReadOnlyList<KeyboardShapeOption> Shapes { get; } = [.. Enum.GetValues<KeyboardShape>().Select(s => new KeyboardShapeOption(s))];

    public KeyboardShapeOption SelectedShape
    {
        get => _shape;
        set { if (value is not null && SetProperty(ref _shape, value)) BuildKeyboard(); }
    }

    public KeyboardShape Shape { get => _shape.Shape; set => SelectedShape = Shapes.First(s => s.Shape == value); }

    public KeyEditorViewModel? KeyEditor { get => _keyEditor; private set => SetProperty(ref _keyEditor, value); }

    public DeadKeysViewModel DeadKeys { get; }
    public UnicodePickerViewModel Unicode { get; }
    public ShiftStatesViewModel ShiftStateEditor { get; }
    public ScanCodesViewModel ScanCodes { get; }
    public KeyNamesViewModel KeyNames { get; }
    public LigaturesViewModel Ligatures { get; }
    public ComposeViewModel Compose { get; }
    public AssistantViewModel Assistant { get; }
    public AnalysisViewModel Analysis { get; }
    public TestsViewModel Tests { get; }
    public CompareViewModel Compare { get; }

    public ObservableCollection<ValidationIssue> Issues { get; } = [];

    /// <summary>Virtual keys to mark on the on-screen keyboard (set by the Compare tab).</summary>
    internal IReadOnlySet<byte> HighlightedKeys { get; private set; } = new HashSet<byte>();

    internal void Highlight(IEnumerable<byte> keys)
    {
        HighlightedKeys = keys.ToHashSet();
        RefreshKeyboard();
    }

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

    /// <summary>How long a test install stays before Windows removes it again.</summary>
    public int TestMinutes { get => _testMinutes; set => SetProperty(ref _testMinutes, Math.Clamp(value, 1, 7 * 24 * 60)); }

    /// <summary><see cref="TestMinutes"/> for number boxes.</summary>
    public double TestMinutesValue { get => _testMinutes; set { TestMinutes = double.IsNaN(value) ? 30 : (int)Math.Round(value); OnPropertyChanged(); } }

    // Metadata (bound to the "Layout" tab)
    public string LayoutName { get => _layout.Metadata.Name; set { _layout.Metadata.Name = value; MarkChanged("name"); OnPropertyChanged(nameof(Title)); } }
    public string DllName { get => _layout.Metadata.DllName; set { _layout.Metadata.DllName = value; MarkChanged("dll"); } }
    public string Locale { get => _layout.Metadata.Locale; set { _layout.Metadata.Locale = value; MarkChanged("locale"); } }
    public string LayoutVersion { get => _layout.Metadata.Version; set { _layout.Metadata.Version = value; MarkChanged("version"); } }
    public string? Author { get => _layout.Metadata.Author; set { _layout.Metadata.Author = value; MarkChanged("author"); } }
    public string? Description { get => _layout.Metadata.Description; set { _layout.Metadata.Description = value; MarkChanged("description"); } }
    public string? LayoutId => _layout.Metadata.LayoutId;

    public bool AltGrFlag
    {
        get => _layout.Flags.HasFlag(LocaleFlags.AltGr);
        set { _layout.Flags = value ? _layout.Flags | LocaleFlags.AltGr : _layout.Flags & ~LocaleFlags.AltGr; MarkChanged(); RefreshKeyboard(); }
    }

    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ExportPictureCommand { get; }
    public ICommand ExportStickersCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand BuildCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand TestInstallCommand { get; }
    public ICommand PackageCommand { get; }
    public ICommand ClearPreviewCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }

    // ---------------------------------------------------------------- loading

    public void New(string templateId)
    {
        var layout = LayoutTemplates.Create(templateId);
        layout.Metadata.Name = $"{layout.Metadata.Name} (custom)";
        Load(layout, null, resetHistory: true);
        IsDirty = true;
    }

    /// <summary>Opens any readable format: .keyservant, .klc, .keylayout, .xkb or a layout DLL.</summary>
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
        Load(layout, isNative ? path : null, resetHistory: true);
        IsDirty = !isNative;
        Status = report.IsLossless ? $"Opened {Path.GetFileName(path)}." : $"Opened {Path.GetFileName(path)} with {report.Warnings.Count} warning(s): {string.Join(" ", report.Warnings)}";
    }

    private void Load(KeyboardLayout layout, string? path, bool resetHistory)
    {
        _layout = layout;
        _filePath = path;
        _simulator = null;
        PreviewText = "";
        if (resetHistory) _history.Reset(layout);

        ReloadShiftStates();
        DeadKeys.Load(layout);
        ShiftStateEditor.Load();
        ScanCodes.Load();
        KeyNames.Load();
        Ligatures.Load();
        Compose.Load();
        Tests.Load();
        Assistant.LayoutReplaced();
        Analysis.LayoutReplaced();
        Compare.LayoutReplaced();
        IsDirty = false;
        foreach (var p in new[] { nameof(LayoutName), nameof(DllName), nameof(Locale), nameof(LayoutVersion), nameof(Author), nameof(Description), nameof(LayoutId), nameof(AltGrFlag), nameof(Title) })
            OnPropertyChanged(p);
        Validate();
    }

    /// <summary>Rebuilds the shift-state list (after adding or removing a state) and the keyboard.</summary>
    internal void ReloadShiftStates()
    {
        var previous = _selectedState.State;
        ShiftStates.Clear();
        foreach (var s in StandardStates.Concat(_layout.Modifiers.Columns).Distinct())
            ShiftStates.Add(new ShiftStateOption(s));
        _selectedState = ShiftStates.FirstOrDefault(s => s.State == previous) ?? ShiftStates[0];
        OnPropertyChanged(nameof(SelectedState));
        BuildKeyboard();
    }

    private void BuildKeyboard()
    {
        var selected = _selectedKey?.Key;
        KeyboardRows.Clear();
        _allKeys.Clear();
        _selectedKey = null;
        KeyEditor = null;
        foreach (var row in KeyboardGeometry.Rows(_shape.Shape))
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
        RefreshKeyboard();
        if (selected is not null && _allKeys.FirstOrDefault(k => k.Key.ScanCode == selected.ScanCode && k.Key.Extended == selected.Extended) is { } again)
            SelectKey(again);
    }

    /// <summary>Updates every key cap from the layout.</summary>
    internal void RefreshKeyboard()
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
            Status = cap.Key.FixedLabel is { } label ? $"{label} is a modifier or control key and has no characters." : "This key has no virtual key in this layout (see the Scan codes tab).";
            return;
        }
        KeyEditor = new KeyEditorViewModel(this, _layout, _layout.GetOrAddKey(vk), ShiftStates.Select(s => s.State));
    }

    internal void SetOutput(byte vk, ShiftState state, KeyOutput? output)
    {
        _layout.SetOutput(vk, state, output);
        if (output is { IsDead: true }) DeadKeys.Load(_layout);
        MarkChanged($"output:{vk}:{state.Value}");
        RefreshKeyboard();
        Ligatures.Load();
    }

    /// <summary>
    /// Call after every change to the layout. Records an undo step (changes with the same
    /// <paramref name="group"/> in quick succession, like typing into one field, become one step),
    /// re-validates and re-runs the layout's tests.
    /// </summary>
    internal void MarkChanged(string? group = null)
    {
        IsDirty = true;
        _simulator = null;
        _history.Record(_layout, group);
        Validate();
        Tests.UpdateResults();
    }

    /// <summary>After a tool changed many parts of the layout at once (assistant, import): record and refresh all tabs.</summary>
    internal void LayoutChangedExternally(string? status = null)
    {
        MarkChanged();
        DeadKeys.Load(_layout);
        ReloadShiftStates();
        ShiftStateEditor.Load();
        ScanCodes.Load();
        KeyNames.Load();
        Ligatures.Load();
        Compose.Load();
        Tests.Load();
        if (status is not null) Status = status;
    }

    private void Undo() => ApplyHistory(_history.Undo(), "Undone.");

    private void Redo() => ApplyHistory(_history.Redo(), "Redone.");

    private void ApplyHistory(KeyboardLayout layout, string status)
    {
        var path = _filePath;
        Load(layout, path, resetHistory: false);
        IsDirty = true;
        Status = status;
    }

    private void Validate()
    {
        Issues.Clear();
        foreach (var i in LayoutValidator.Validate(_layout).Issues.OrderByDescending(i => i.Severity)) Issues.Add(i);
        OnPropertyChanged(nameof(IssueSummary));
    }

    /// <summary>The layout as it will be built: compose sequences compiled into dead keys.</summary>
    internal KeyboardLayout EffectiveLayout() => ComposeCompiler.Compile(_layout).Layout;

    // ---------------------------------------------------------------- live preview

    /// <summary>
    /// Handles a keystroke in the preview box, using the layout being edited (including its compose key)
    /// instead of the active Windows layout. Returns true if the key was handled.
    /// </summary>
    public bool HandlePreviewKey(int scanCode, bool extended, bool shift, bool ctrl, bool alt, bool capsLock)
    {
        _simulator ??= new TypingSimulator(EffectiveLayout());
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

    internal static readonly string[] OpenExtensions = [".keyservant", ".klc", ".dll", ".keylayout", ".xkb"];

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var path = await _dialogs.PickOpenFileAsync(OpenExtensions);
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
        var path = await _dialogs.PickSaveFileAsync(_layout.Metadata.DllName + ".klc", [".klc", ".keylayout", ".xkb", ".c"]);
        if (path is null) return;
        var report = new ConversionReport();
        _formats.WriteFile(_layout, path, report);
        var extra = "";
        if (path.EndsWith(".xkb", StringComparison.OrdinalIgnoreCase) && XkbSymbolsFormat.WriteXCompose(_layout) is { Length: > 0 } compose)
        {
            var composePath = Path.ChangeExtension(path, ".XCompose");
            await File.WriteAllTextAsync(composePath, compose);
            extra = $" Dead keys and compose sequences are in {Path.GetFileName(composePath)}.";
        }
        Status = (report.IsLossless ? $"Exported {Path.GetFileName(path)}." : $"Exported with {report.Warnings.Count} warning(s): {string.Join(" ", report.Warnings)}") + extra;
    }

    private async Task ExportSvgAsync(RenderMode mode)
    {
        var suffix = mode == RenderMode.Stickers ? "-stickers" : "";
        var path = await _dialogs.PickSaveFileAsync($"{_layout.Metadata.DllName}{suffix}.svg", [".svg"]);
        if (path is null) return;
        var svg = KeyboardSvgRenderer.Render(EffectiveLayout(), new RenderOptions { Mode = mode, Shape = _shape.Shape, Title = _layout.Metadata.Name });
        await File.WriteAllTextAsync(path, svg);
        Status = mode == RenderMode.Stickers
            ? $"Saved {Path.GetFileName(path)}. Print it at 100% on sticker paper and cut along the lines."
            : $"Saved {Path.GetFileName(path)}. Print it at 100% for real size.";
    }

    private async Task BuildAsync()
    {
        var folder = await _dialogs.PickFolderAsync();
        if (folder is null) return;
        var result = LayoutDllBuilder.Build(_layout);
        async Task Write(string arch, byte[]? dll)
        {
            if (dll is null) return;
            Directory.CreateDirectory(Path.Combine(folder, arch));
            await File.WriteAllBytesAsync(Path.Combine(folder, arch, result.FileName), dll);
        }
        await Write("x64", result.X64);
        await Write("wow64", result.Wow64);
        await Write("arm64", result.Arm64);
        Status = $"Built {result.FileName} (x64, 32-bit and ARM64) in {folder}.";
    }

    private async Task InstallAsync()
    {
        if (!await PrepareInstallAsync()) return;
        var result = await _privileged.InstallAsync(_filePath!);
        await FinishInstallAsync(result);
    }

    private async Task TestInstallAsync()
    {
        if (!await PrepareInstallAsync()) return;
        var result = await _privileged.InstallTemporaryAsync(_filePath!, _testMinutes);
        await FinishInstallAsync(result);
    }

    private async Task<bool> PrepareInstallAsync()
    {
        if (!EnsureBuildable()) return false;
        if ((_isDirty || _filePath is null) && !await SaveAsync(saveAs: false)) return false;
        Status = "Waiting for administrator approval...";
        return true;
    }

    private async Task FinishInstallAsync(OperationResult result)
    {
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

    internal void ReportError(Exception ex)
    {
        Status = ex.Message;
        _ = _dialogs.ShowMessageAsync("Something went wrong", ex.Message);
    }
}
