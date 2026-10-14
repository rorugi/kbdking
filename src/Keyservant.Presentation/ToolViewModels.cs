using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Keyservant.Core.Analysis;
using Keyservant.Core.Assist;
using Keyservant.Core.Diff;
using Keyservant.Core.Formats;
using Keyservant.Core.Model;
using Keyservant.Core.Rendering;

namespace Keyservant.Presentation;

// ===================================================================== placement assistant

/// <summary>One proposed change, with a check box.</summary>
public sealed class ProposalViewModel(PlannedChange change) : ObservableObject
{
    private bool _isSelected = true;

    public PlannedChange Change { get; } = change;
    public string Description => Change.Description;
    public string Characters => string.Join(" ", Change.Characters);
    public string Kind => Change.Kind switch
    {
        PlannedChangeKind.DirectKey => "Key",
        PlannedChangeKind.DeadKey => "New dead key",
        PlannedChangeKind.DeadKeyEntries => "Dead key",
        PlannedChangeKind.Chain => "Chained dead key",
        _ => Change.Kind.ToString(),
    };

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>
/// "Start from the characters you need": enter characters or paste a sample text, get a plan for where to put
/// what, tick the proposals you like and apply them.
/// </summary>
public sealed class AssistantViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private string _characters = "";
    private string _sampleText = "";
    private int _threshold = 3;
    private string _summary = "Enter the characters you need (or paste a text in the language), then press Suggest.";
    private string _alreadyTypeable = "";

    internal AssistantViewModel(EditorViewModel editor)
    {
        _editor = editor;
        SuggestCommand = new RelayCommand(Suggest);
        ApplyCommand = new RelayCommand(Apply, () => Proposals.Any(p => p.IsSelected));
        PresetCommand = new RelayCommand(p => Characters = Presets.FirstOrDefault(x => x.Name == p as string)?.Characters ?? Characters);
    }

    /// <summary>Character sets to start from.</summary>
    public IReadOnlyList<CharacterPreset> Presets { get; } =
    [
        new("Sanskrit (IAST)", "āĀīĪūŪṛṚṝṜḷḶḹḸṅṄñÑṭṬḍḌṇṆśŚṣṢṃṂḥḤ"),
        new("Pali", "āĀīĪūŪṅṄñÑṭṬḍḌṇṆḷḶṃṂ"),
        new("Hindi transliteration (ISO 15919)", "āĀīĪūŪr̥ēĒōŌṅṄñÑṭṬḍḌṇṆśŚṣṢṁṀḥḤ"),
        new("Pinyin with tones", "āáǎàēéěèīíǐìōóǒòūúǔùǖǘǚǜüĀÁǍÀĒÉĚÈĪÍǏÌŌÓǑÒŪÚǓÙǕǗǙǛÜ"),
        new("Arabic transliteration (DMG)", "āĀīĪūŪṯṮḥḤḫḪḏḎšŠṣṢḍḌṭṬẓẒʿʾġĠ"),
        new("Vietnamese", "ăâđêôơưĂÂĐÊÔƠƯàảãáạằẳẵắặầẩẫấậèẻẽéẹềểễếệìỉĩíịòỏõóọồổỗốộờởỡớợùủũúụừửữứựỳỷỹýỵ"),
        new("Polish", "ąćęłńóśźżĄĆĘŁŃÓŚŹŻ"),
        new("Czech", "áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ"),
        new("Esperanto", "ĉĝĥĵŝŭĈĜĤĴŜŬ"),
        new("Typography", "–—‘’‚“”„…‹›«»×÷±≈≠≤≥€£¥©®™°§¶†‡•"),
    ];

    public string Characters { get => _characters; set => SetProperty(ref _characters, value); }

    /// <summary>Optional text in the target language; its letter frequencies decide what gets the easiest keys.</summary>
    public string SampleText { get => _sampleText; set => SetProperty(ref _sampleText, value); }

    /// <summary>A mark needs this many characters before it gets its own dead key.</summary>
    public int DeadKeyThreshold { get => _threshold; set => SetProperty(ref _threshold, Math.Clamp(value, 1, 50)); }

    /// <summary><see cref="DeadKeyThreshold"/> for number boxes.</summary>
    public double DeadKeyThresholdValue { get => _threshold; set { DeadKeyThreshold = double.IsNaN(value) ? 3 : (int)Math.Round(value); OnPropertyChanged(); } }

    public ObservableCollection<ProposalViewModel> Proposals { get; } = [];

    public ObservableCollection<string> NotPlaced { get; } = [];

    public string AlreadyTypeable { get => _alreadyTypeable; private set => SetProperty(ref _alreadyTypeable, value); }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public ICommand SuggestCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public ICommand PresetCommand { get; }

    internal void LayoutReplaced()
    {
        Proposals.Clear();
        NotPlaced.Clear();
        AlreadyTypeable = "";
        ApplyCommand?.NotifyCanExecuteChanged();
    }

    private void Suggest()
    {
        var layout = _editor.Layout;
        var wanted = _characters;
        IReadOnlyDictionary<string, int>? frequencies = null;
        if (_sampleText.Trim().Length > 0)
        {
            var analysis = TextAnalyzer.Analyze(layout, _sampleText);
            frequencies = analysis.Characters.ToDictionary(c => c.Text, c => c.Count);
            wanted += string.Concat(analysis.Characters.Select(c => c.Text));
        }
        if (wanted.Trim().Length == 0) { Summary = "Enter some characters or a sample text first."; return; }

        var plan = PlacementPlanner.Plan(layout, wanted, new PlacementOptions { Frequencies = frequencies, DeadKeyThreshold = _threshold });
        Proposals.Clear();
        foreach (var c in plan.Changes)
        {
            var p = new ProposalViewModel(c);
            p.PropertyChanged += (_, _) => ApplyCommand.NotifyCanExecuteChanged();
            Proposals.Add(p);
        }
        NotPlaced.Clear();
        foreach (var (ch, reason) in plan.NotPlaced) NotPlaced.Add($"{ch}  {reason}");
        AlreadyTypeable = plan.AlreadyTypeable.Count == 0 ? "" : "Already typeable: " + string.Join(" ", plan.AlreadyTypeable);
        Summary = plan.Changes.Count == 0
            ? (plan.NotPlaced.Count == 0 ? "Everything is already typeable." : "No free places found for the remaining characters.")
            : $"{plan.Changes.Count} proposals. Untick what you don't want, then Apply.";
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void Apply()
    {
        var layout = _editor.Layout;
        var applied = 0;
        var failed = new List<string>();
        foreach (var p in Proposals.Where(p => p.IsSelected))
        {
            try
            {
                p.Change.Apply(layout);
                applied++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
            {
                failed.Add($"{p.Description}: {ex.Message}");
            }
        }
        _editor.LayoutChangedExternally($"Applied {applied} proposals." + (failed.Count > 0 ? $" {failed.Count} failed: {string.Join("; ", failed)}" : " Undo reverts them."));
        Suggest();
    }
}

/// <summary>A named character set for the assistant.</summary>
public sealed record CharacterPreset(string Name, string Characters)
{
    public override string ToString() => Name;
}

// ===================================================================== text analysis

/// <summary>A character in the analysis tables.</summary>
public sealed record CharacterRow(string Character, int Count, string Keys, string Effort)
{
    public string Name => OutputRowViewModel.Describe(Character);
    public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>"Optimize for real text": how much effort a text takes with this layout, heatmap, missing characters.</summary>
public sealed class AnalysisViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private string _text = "";
    private string _summary = "Paste a text you type often (or load a file) and press Analyze.";
    private string? _heatmapSvg;
    private string? _comparison;
    private TextAnalysis? _last;

    internal AnalysisViewModel(EditorViewModel editor)
    {
        _editor = editor;
        AnalyzeCommand = new RelayCommand(Analyze);
        LoadTextCommand = new AsyncRelayCommand(LoadTextAsync, onError: editor.ReportError);
        CompareCommand = new AsyncRelayCommand(CompareAsync, onError: editor.ReportError);
        SaveHeatmapCommand = new AsyncRelayCommand(SaveHeatmapAsync, () => _heatmapSvg is not null, editor.ReportError);
    }

    public string Text { get => _text; set => SetProperty(ref _text, value); }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    /// <summary>The heatmap as SVG; the view renders it.</summary>
    public string? HeatmapSvg { get => _heatmapSvg; private set { if (SetProperty(ref _heatmapSvg, value)) ((AsyncRelayCommand)SaveHeatmapCommand).NotifyCanExecuteChanged(); } }

    public string? Comparison { get => _comparison; private set => SetProperty(ref _comparison, value); }

    public ObservableCollection<CharacterRow> Expensive { get; } = [];

    public ObservableCollection<CharacterRow> Untypeable { get; } = [];

    public ICommand AnalyzeCommand { get; }
    public ICommand LoadTextCommand { get; }
    public ICommand CompareCommand { get; }
    public ICommand SaveHeatmapCommand { get; }

    internal void LayoutReplaced()
    {
        if (_last is not null) Analyze();
    }

    internal void Analyze()
    {
        if (_text.Length == 0) { Summary = "Paste or load a text first."; return; }
        var layout = _editor.EffectiveLayout();
        var analysis = TextAnalyzer.Analyze(layout, _text);
        _last = analysis;
        var share = analysis.TotalCharacters == 0 ? 1 : (double)analysis.TypeableCharacters / analysis.TotalCharacters;
        Summary = string.Create(CultureInfo.CurrentCulture,
            $"{analysis.TotalCharacters:N0} characters, {share:P1} typeable. Effort {analysis.EffortPerCharacter:0.00} per character (1.0 ≈ one home-row key).");
        Expensive.Clear();
        foreach (var c in analysis.ExpensiveCharacters(15))
            Expensive.Add(new CharacterRow(c.Text, c.Count, c.Recipe is { } r ? KeySequence.Format(r.Strokes, layout) : "", c.Effort.ToString("0.0", CultureInfo.CurrentCulture)));
        Untypeable.Clear();
        foreach (var c in analysis.Untypeable.OrderByDescending(c => c.Count))
            Untypeable.Add(new CharacterRow(c.Text, c.Count, "", "—"));
        HeatmapSvg = KeyboardSvgRenderer.Render(layout, new RenderOptions { Heat = analysis.KeyPresses, Shape = _editor.Shape, Title = "Key presses" });
    }

    /// <summary>Sends the untypeable characters to the assistant.</summary>
    public void SendUntypeableToAssistant()
    {
        _editor.Assistant.Characters = string.Concat(Untypeable.Select(u => u.Character));
        _editor.Assistant.SampleText = _text;
        _editor.Status = Untypeable.Count == 0 ? "Everything in the text is typeable." : $"{Untypeable.Count} characters are waiting on the Assistant tab; press Suggest there.";
    }

    private async Task LoadTextAsync()
    {
        var path = await _editor.Dialogs.PickOpenFileAsync([".txt", ".md", "*"]);
        if (path is null) return;
        Text = await File.ReadAllTextAsync(path);
        Analyze();
    }

    private async Task CompareAsync()
    {
        if (_text.Length == 0) { Summary = "Paste or load a text first."; return; }
        var path = await _editor.Dialogs.PickOpenFileAsync(EditorViewModel.OpenExtensions);
        if (path is null) return;
        var other = Keyservant.Core.Compose.ComposeCompiler.Compile(_editor.Formats.ReadFile(path, new ConversionReport())).Layout;
        var mine = TextAnalyzer.Analyze(_editor.EffectiveLayout(), _text);
        var theirs = TextAnalyzer.Analyze(other, _text);
        var delta = mine.EffortPerCharacter == 0 ? 0 : (theirs.EffortPerCharacter - mine.EffortPerCharacter) / mine.EffortPerCharacter;
        Comparison = string.Create(CultureInfo.CurrentCulture,
            $"{Path.GetFileName(path)}: {theirs.EffortPerCharacter:0.00} per character ({delta:+0%;-0%;±0%} compared with this layout), {theirs.Untypeable.Sum(c => c.Count):N0} untypeable characters (this layout: {mine.Untypeable.Sum(c => c.Count):N0}).");
    }

    private async Task SaveHeatmapAsync()
    {
        if (_heatmapSvg is null) return;
        var path = await _editor.Dialogs.PickSaveFileAsync(_editor.Layout.Metadata.DllName + "-heatmap.svg", [".svg"]);
        if (path is null) return;
        await File.WriteAllTextAsync(path, _heatmapSvg);
        _editor.Status = $"Saved {Path.GetFileName(path)}.";
    }
}

// ===================================================================== layout tests

/// <summary>One test: these keys must type this text.</summary>
public sealed class TestCaseViewModel : ObservableObject
{
    private readonly TestsViewModel _owner;
    private string _name;
    private string _keys;
    private string _expect;
    private bool? _passed;
    private string _message = "";

    internal TestCaseViewModel(TestsViewModel owner, LayoutTestCase test)
    {
        _owner = owner;
        _name = test.Name ?? "";
        _keys = test.Keys;
        _expect = test.Expect;
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    public string Name { get => _name; set { if (SetProperty(ref _name, value)) _owner.Commit(); } }

    /// <summary>Keys as in <see cref="KeySequence"/>: "AltGr+. t", "Shift+a", "Compose . r".</summary>
    public string Keys { get => _keys; set { if (SetProperty(ref _keys, value)) _owner.Commit(); } }

    public string Expect { get => _expect; set { if (SetProperty(ref _expect, value)) _owner.Commit(); } }

    /// <summary>True passed, false failed, null not run (incomplete row).</summary>
    public bool? Passed { get => _passed; private set => SetProperty(ref _passed, value); }

    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public string StatusGlyph => _passed switch { true => "✓", false => "✗", null => "…" };

    public ICommand RemoveCommand { get; }

    internal LayoutTestCase? ToTest() => _keys.Trim().Length > 0 ? new LayoutTestCase(_keys.Trim(), _expect, _name.Trim().Length == 0 ? null : _name.Trim()) : null;

    internal void SetResult(LayoutTestResult? result)
    {
        Passed = result?.Passed;
        Message = result is null ? "Enter keys and the expected text." : result.Passed ? $"types \"{result.Actual}\"" : result.Message;
        OnPropertyChanged(nameof(StatusGlyph));
    }
}

/// <summary>Layout tests, saved in the .keyservant file and re-run after every change.</summary>
public sealed class TestsViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private string _summary = "";
    private bool _loading;

    internal TestsViewModel(EditorViewModel editor)
    {
        _editor = editor;
        AddCommand = new RelayCommand(() => { Tests.Add(new TestCaseViewModel(this, new LayoutTestCase("", ""))); UpdateResults(); });
        AddDefaultsCommand = new RelayCommand(AddDefaults);
    }

    public ObservableCollection<TestCaseViewModel> Tests { get; } = [];

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public ICommand AddCommand { get; }

    /// <summary>Adds a test for every character the layout types now, so later edits can't break them unnoticed.</summary>
    public ICommand AddDefaultsCommand { get; }

    internal void Load()
    {
        _loading = true;
        Tests.Clear();
        foreach (var t in _editor.Layout.Tests) Tests.Add(new TestCaseViewModel(this, t));
        _loading = false;
        UpdateResults();
    }

    internal void Remove(TestCaseViewModel test)
    {
        Tests.Remove(test);
        Commit();
    }

    internal void Commit()
    {
        if (_loading) return;
        var layout = _editor.Layout;
        layout.Tests.Clear();
        foreach (var t in Tests) if (t.ToTest() is { } test) layout.Tests.Add(test);
        _editor.MarkChanged("tests");
    }

    internal void UpdateResults()
    {
        if (Tests.Count == 0) { Summary = "No tests yet. A test says which keys must type which text, e.g. AltGr+. t → ṭ."; return; }
        var effective = _editor.EffectiveLayout();
        int passed = 0, failed = 0;
        foreach (var t in Tests)
        {
            var test = t.ToTest();
            var result = test is null ? null : LayoutTestRunner.Run(effective, test);
            t.SetResult(result);
            if (result is { Passed: true }) passed++;
            else if (result is not null) failed++;
        }
        Summary = failed == 0 ? $"All {passed} tests pass." : $"{failed} of {passed + failed} tests fail.";
    }

    private void AddDefaults()
    {
        var layout = _editor.Layout;
        var finder = new KeystrokeFinder(_editor.EffectiveLayout());
        var existing = layout.Tests.Select(t => t.Expect).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var (text, recipe) in finder.All.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (text.Length == 0 || text.Any(char.IsControl) || text.All(c => c < 0x80) || !existing.Add(text)) continue;
            layout.Tests.Add(new LayoutTestCase(KeySequence.Format(recipe.Strokes, layout), text));
            added++;
        }
        _editor.MarkChanged();
        Load();
        _editor.Status = $"Added {added} tests for the non-ASCII characters this layout types now.";
    }
}

// ===================================================================== compare (visual diff)

/// <summary>Compares the layout with another file or the saved version, and marks changed keys on the keyboard.</summary>
public sealed class CompareViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private string _summary = "Compare this layout with another layout file, or with the version saved on disk.";
    private string? _diffSvg;
    private KeyboardLayout? _other;
    private string? _otherName;
    private bool _highlightOnKeyboard = true;

    internal CompareViewModel(EditorViewModel editor)
    {
        _editor = editor;
        CompareWithFileCommand = new AsyncRelayCommand(CompareWithFileAsync, onError: editor.ReportError);
        CompareWithSavedCommand = new RelayCommand(CompareWithSaved, () => _editor.FilePath is not null);
        ClearCommand = new RelayCommand(Clear);
    }

    public ObservableCollection<LayoutChange> Changes { get; } = [];

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    /// <summary>The current layout with changed keys highlighted, as SVG.</summary>
    public string? DiffSvg { get => _diffSvg; private set => SetProperty(ref _diffSvg, value); }

    public bool HighlightOnKeyboard
    {
        get => _highlightOnKeyboard;
        set { if (SetProperty(ref _highlightOnKeyboard, value)) Run(); }
    }

    public ICommand CompareWithFileCommand { get; }
    public RelayCommand CompareWithSavedCommand { get; }
    public ICommand ClearCommand { get; }

    internal void LayoutReplaced()
    {
        CompareWithSavedCommand?.NotifyCanExecuteChanged();
        if (_other is not null) Run();
    }

    /// <summary>Compares with an already loaded layout (also used by tests).</summary>
    public void CompareWith(KeyboardLayout other, string name)
    {
        _other = other;
        _otherName = name;
        Run();
    }

    private async Task CompareWithFileAsync()
    {
        var path = await _editor.Dialogs.PickOpenFileAsync(EditorViewModel.OpenExtensions);
        if (path is null) return;
        CompareWith(_editor.Formats.ReadFile(path, new ConversionReport()), Path.GetFileName(path));
    }

    private void CompareWithSaved()
    {
        if (_editor.FilePath is not { } path) return;
        CompareWith(KeyservantFormat.Parse(File.ReadAllText(path)), "the saved file");
    }

    private void Clear()
    {
        _other = null;
        Changes.Clear();
        DiffSvg = null;
        Summary = "Compare this layout with another layout file, or with the version saved on disk.";
        _editor.Highlight([]);
    }

    private void Run()
    {
        if (_other is null) return;
        var changes = LayoutDiff.Compare(_other, _editor.Layout);
        Changes.Clear();
        foreach (var c in changes) Changes.Add(c);
        var keys = changes.Where(c => c.VirtualKey is not null).Select(c => c.VirtualKey!.Value).ToHashSet();
        Summary = changes.Count == 0 ? $"No differences from {_otherName}." : $"{changes.Count} differences from {_otherName} ({keys.Count} keys). Changed keys are marked.";
        DiffSvg = KeyboardSvgRenderer.Render(_editor.Layout, new RenderOptions { Highlight = keys, Shape = _editor.Shape, Title = $"Changed since {_otherName}" });
        _editor.Highlight(_highlightOnKeyboard ? keys : []);
    }
}
