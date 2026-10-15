using Keyservant.Binary;
using Keyservant.Binary.Packaging;
using Keyservant.Binary.Pe;
using Keyservant.Core.Analysis;
using Keyservant.Core.Assist;
using Keyservant.Core.Compose;
using Keyservant.Core.Diff;
using Keyservant.Core.Editing;
using Keyservant.Core.Formats;
using Keyservant.Core.Geometry;
using Keyservant.Core.Model;
using Keyservant.Core.Rendering;
using Keyservant.Core.Templates;
using Keyservant.Core.Unicode;
using Keyservant.Core.Validation;

namespace Keyservant.Tests;

public class UnicodeTests
{
    [Fact]
    public void Finds_characters_by_words_in_any_order()
    {
        var results = UnicodeCatalog.Search("macron a small");
        Assert.Equal("ā", results[0].Text);
        Assert.Contains(UnicodeCatalog.Search("dot below r"), r => r.Text == "ṛ");
    }

    [Fact]
    public void Finds_characters_by_code_and_by_pasting()
    {
        Assert.Equal("LATIN SMALL LETTER M WITH DOT BELOW", UnicodeCatalog.Search("U+1E43")[0].Name);
        Assert.Equal(0x1E43, UnicodeCatalog.Search("1e43")[0].CodePoint);
        Assert.Equal(0x1E6D, UnicodeCatalog.Search("ṭ")[0].CodePoint);
    }

    [Fact]
    public void Names_cover_algorithmic_ranges_and_marks()
    {
        Assert.Equal("HANGUL SYLLABLE GA", UnicodeCatalog.GetName(0xAC00));
        Assert.StartsWith("CJK UNIFIED IDEOGRAPH-", UnicodeCatalog.GetName(0x4E00));
        var mark = UnicodeCatalog.Get(0x0323)!;
        Assert.True(mark.IsMark);
        Assert.Equal("◌̣", mark.Display);
    }

    [Fact]
    public void Diacritic_compositions_are_nfc_letters()
    {
        var macron = Diacritics.Find('̄')!;
        var comps = Diacritics.Compositions(macron.Combining);
        Assert.Contains(('a', 'ā'), comps);
        Assert.Contains(('E', 'Ē'), comps);
    }
}

public class ComposeTests
{
    private static KeyboardLayout WithCompose(params (string Keys, string Result)[] sequences)
    {
        var layout = LayoutTemplates.Create("en-US");
        layout.Compose = new ComposeDefinition();
        foreach (var (k, r) in sequences) layout.Compose.Sequences.Add(new ComposeSequence(k, r));
        return layout;
    }

    [Fact]
    public void Sequences_compile_into_typeable_dead_keys()
    {
        var layout = WithCompose((".r", "ṛ"), (".rr", "ṝ"), ("--", "–"), ("-.", "·"));
        var compiled = ComposeCompiler.Compile(layout);
        Assert.Empty(compiled.Errors);
        Assert.Equal(3, compiled.CompiledSequences); // ".rr" can never be reached because ".r" already finishes
        Assert.Contains(compiled.Warnings, w => w.Contains(".rr", StringComparison.Ordinal));

        var sim = new TypingSimulator(compiled.Layout);
        string Type(params (int Sc, bool Shift)[] keys) =>
            string.Concat(keys.Select(k => sim.PressScanCode(k.Sc, k.Sc == 0x5D, k.Shift ? ShiftState.Shift : ShiftState.Base)));
        Assert.Equal("–", Type((0x5D, false), (0x0C, false), (0x0C, false)));
        Assert.Equal("ṛ", Type((0x5D, false), (0x34, false), (0x13, false)));
        Assert.Equal("·", Type((0x5D, false), (0x0C, false), (0x34, false)));
        // The original layout is not changed.
        Assert.Null(layout.FindKey(0xDF));
    }

    [Fact]
    public void Layout_without_sequences_is_returned_unchanged()
    {
        var layout = LayoutTemplates.Create("en-US");
        Assert.Same(layout, ComposeCompiler.Compile(layout).Layout);
    }

    [Fact]
    public void Results_must_be_single_bmp_characters()
    {
        var compiled = ComposeCompiler.Compile(WithCompose(("dd", "𝔻"), ("ng", "ng")));
        Assert.Equal(0, compiled.CompiledSequences);
        Assert.Equal(2, compiled.Warnings.Count);
    }

    [Fact]
    public void XCompose_files_round_trip()
    {
        var text = """
            include "%L"
            <Multi_key> <period> <r> : "ṛ" U1E5B # comment
            <Multi_key> <minus> <minus> <minus> : "—" emdash
            <dead_acute> <a> : "á"
            """;
        var report = new ConversionReport();
        var sequences = XComposeFile.Parse(text, report);
        Assert.Equal(new[] { new ComposeSequence(".r", "ṛ"), new ComposeSequence("---", "—") }, sequences);
        Assert.NotEmpty(report.Warnings);
        Assert.Equal(sequences, XComposeFile.Parse(XComposeFile.Write(sequences), new ConversionReport()));
    }

    [Fact]
    public void Compose_and_tests_are_saved_in_keyservant_files()
    {
        var layout = WithCompose((".r", "ṛ"));
        layout.Compose!.ScanCode = 0x38;
        layout.Tests.Add(new LayoutTestCase("Compose . r", "ṛ", "dot below"));
        var back = KeyservantFormat.Parse(KeyservantFormat.Serialize(layout));
        Assert.Equal("E0 38", back.Compose!.KeyText);
        Assert.Equal(layout.Compose.Sequences, back.Compose.Sequences);
        Assert.Equal(layout.Tests, back.Tests);
    }

    [Fact]
    public void Compose_layouts_build_into_dlls()
    {
        var layout = WithCompose((".r", "ṛ"), ("--", "–"));
        var dll = LayoutDllBuilder.Build(layout);
        var back = LayoutDllReader.Read(dll.X64);
        Assert.Contains(back.DeadKeys, d => d.Accent == '⎄');
    }
}

public class LayoutTestTests
{
    [Fact]
    public void Key_sequences_parse_characters_modifiers_and_names()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var strokes = KeySequence.Parse("AltGr+. Shift+t", layout);
        Assert.Equal(2, strokes.Count);
        Assert.Equal(ShiftState.AltGr, strokes[0].State);
        Assert.Equal((byte)'T', strokes[1].VirtualKey);
        Assert.Equal(ShiftState.Shift, strokes[1].State);
        Assert.Equal(new[] { new KeyStroke(0xBB, ShiftState.Base) }, KeySequence.Parse("PLUS", layout));
    }

    [Fact]
    public void Tests_pass_and_fail_and_block_validation()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        Assert.All(LayoutTestRunner.Run(layout), r => Assert.True(r.Passed, r.Message)); // the sample's own tests
        layout.Tests.Clear();
        layout.Tests.Add(new LayoutTestCase("AltGr+. t", "ṭ"));
        layout.Tests.Add(new LayoutTestCase("a", "b", "wrong on purpose"));
        layout.Tests.Add(new LayoutTestCase("Bogus+a", "a"));
        var results = LayoutTestRunner.Run(layout);
        Assert.True(results[0].Passed, results[0].Message);
        Assert.False(results[1].Passed);
        Assert.Equal("typed \"a\", expected \"b\"", results[1].Message);
        Assert.NotNull(results[2].Error);

        var issues = LayoutValidator.Validate(layout).Issues;
        Assert.Contains(issues, i => i.Code == "KS070");
        Assert.Contains(issues, i => i.Code == "KS071");
    }
}

public class HistoryTests
{
    [Fact]
    public void Undo_and_redo_restore_snapshots()
    {
        var layout = LayoutTemplates.Create("en-US");
        var history = new LayoutHistory(layout);
        Assert.False(history.CanUndo);
        layout.SetOutput((byte)'A', ShiftState.AltGr, KeyOutput.Of("ā"));
        history.Record(layout);
        layout.Metadata.Name = "Changed";
        history.Record(layout);

        var undone = history.Undo();
        Assert.NotEqual("Changed", undone.Metadata.Name);
        Assert.Equal("ā", undone.FindKey((byte)'A')![ShiftState.AltGr]!.Text);
        var first = history.Undo();
        Assert.Null(first.FindKey((byte)'A')![ShiftState.AltGr]);
        Assert.False(history.CanUndo);
        Assert.Equal("ā", history.Redo().FindKey((byte)'A')![ShiftState.AltGr]!.Text);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void Changes_in_one_group_merge_into_one_step()
    {
        var now = new DateTime(2026, 1, 1);
        var layout = LayoutTemplates.Create("en-US");
        var history = new LayoutHistory(layout, clock: () => now);
        foreach (var name in new[] { "A", "AB", "ABC" })
        {
            layout.Metadata.Name = name;
            history.Record(layout, "name");
            now = now.AddMilliseconds(300);
        }
        Assert.Equal("ABC", KeyservantFormat.Parse(KeyservantFormat.Serialize(layout)).Metadata.Name);
        var undone = history.Undo();
        Assert.False(history.CanUndo);
        Assert.Equal(LayoutTemplates.Create("en-US").Metadata.Name, undone.Metadata.Name);
    }

    [Fact]
    public void Recording_after_undo_drops_the_redo_branch()
    {
        var layout = LayoutTemplates.Create("en-US");
        var history = new LayoutHistory(layout);
        layout.Metadata.Name = "one";
        history.Record(layout);
        layout = history.Undo();
        layout.Metadata.Name = "two";
        history.Record(layout);
        Assert.False(history.CanRedo);
    }
}

public class AnalysisTests
{
    [Fact]
    public void Finder_prefers_cheap_recipes_and_follows_dead_keys()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var finder = new KeystrokeFinder(layout);
        Assert.Single(finder.Find("a")!.Strokes);
        var t = finder.Find("ṭ")!;
        Assert.Equal(2, t.Strokes.Count);
        Assert.True(finder.Find("A")!.Effort > finder.Find("a")!.Effort);
        Assert.False(finder.CanType("☃"));
    }

    [Fact]
    public void Analysis_counts_effort_heat_and_untypeable_characters()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var analysis = TextAnalyzer.Analyze(layout, "kṛṣṇa ☃☃");
        Assert.Equal(8, analysis.TotalCharacters); // the space counts too
        Assert.Equal(2, analysis.Untypeable.Single().Count);
        Assert.True(analysis.EffortPerCharacter > 1);
        Assert.True(analysis.KeyPresses[(byte)'K'] >= 1);
        Assert.Contains("☃", analysis.Summary());
    }
}

public class AssistantTests
{
    [Fact]
    public void Plans_dead_keys_for_shared_marks_and_keys_for_the_rest()
    {
        var layout = LayoutTemplates.German("de-AT");
        var plan = PlacementPlanner.Plan(layout, "ṭḍṇṣḥṃ ñ");
        Assert.Contains(plan.Changes, c => c.Kind == PlannedChangeKind.DeadKey && c.Characters.Contains("ṭ"));
        Assert.Contains(plan.Changes, c => c.Characters.Contains("ñ"));
        Assert.Empty(plan.NotPlaced);

        plan.ApplyAll(layout);
        var finder = new KeystrokeFinder(layout);
        foreach (var c in "ṭḍṇṣḥṃñ") Assert.True(finder.CanType(c.ToString()), $"{c} should be typeable");
        Assert.DoesNotContain(LayoutValidator.Validate(layout).Issues, i => i.Severity == Severity.Error);
    }

    [Fact]
    public void Already_typeable_characters_are_left_alone()
    {
        var layout = LayoutTemplates.German("de-AT");
        var plan = PlacementPlanner.Plan(layout, "äöü");
        Assert.Empty(plan.Changes);
        Assert.Equal(new[] { "ä", "ö", "ü" }, plan.AlreadyTypeable);
    }
}

public class DiffAndRenderTests
{
    [Fact]
    public void Diff_lists_changed_keys_and_metadata()
    {
        var before = LayoutTemplates.German("de-AT");
        var after = before.Clone();
        after.Metadata.Name = "Renamed";
        after.SetOutput((byte)'A', ShiftState.AltGr, KeyOutput.Of("ā"));
        after.SetOutput((byte)'Q', ShiftState.AltGr, null);
        var changes = LayoutDiff.Compare(before, after);
        Assert.Contains(changes, c => c.Area == "Metadata" && c.After == "Renamed");
        var a = Assert.Single(changes, c => c.VirtualKey == (byte)'A');
        Assert.Equal("added", a.Kind);
        Assert.Contains(changes, c => c.VirtualKey == (byte)'Q' && c.Kind == "removed");
        Assert.Empty(LayoutDiff.Compare(before, before.Clone()));
        Assert.Contains("ā", LayoutDiff.Format(changes));
    }

    [Theory]
    [InlineData(KeyboardShape.Iso)]
    [InlineData(KeyboardShape.Ansi)]
    [InlineData(KeyboardShape.Jis)]
    [InlineData(KeyboardShape.Abnt)]
    public void Shapes_render_as_svg(KeyboardShape shape)
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var svg = KeyboardSvgRenderer.Render(layout, new RenderOptions { Shape = shape, Title = "Test & <title>" });
        var doc = System.Xml.Linq.XDocument.Parse(svg);
        Assert.Equal("svg", doc.Root!.Name.LocalName);
        Assert.Contains("Test &amp; &lt;title&gt;", svg);
        Assert.Contains("mm\"", svg);
        Assert.NotEmpty(KeyboardGeometry.Rows(shape));
    }

    [Fact]
    public void Stickers_and_heatmaps_render()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var stickers = KeyboardSvgRenderer.Render(layout, new RenderOptions { Mode = RenderMode.Stickers });
        System.Xml.Linq.XDocument.Parse(stickers);
        Assert.Contains("◌̣", stickers); // dead keys show their accent
        var heat = KeyboardSvgRenderer.Render(layout, new RenderOptions { Heat = new Dictionary<byte, int> { [(byte)'A'] = 10 } });
        System.Xml.Linq.XDocument.Parse(heat);
    }

    [Fact]
    public void Jis_and_abnt_have_their_extra_keys()
    {
        Assert.Contains(KeyboardGeometry.Rows(KeyboardShape.Jis).SelectMany(r => r), k => k.ScanCode == 0x7D);
        Assert.Contains(KeyboardGeometry.Rows(KeyboardShape.Abnt).SelectMany(r => r), k => k.ScanCode == 0x73);
    }
}

public class PortabilityTests
{
    [Fact]
    public void Keylayout_round_trips_dead_keys_and_altgr()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var report = new ConversionReport();
        var xml = MacKeylayoutFormat.Serialize(layout, report);
        Assert.StartsWith("<?xml version=\"1.1\"", xml);
        var back = MacKeylayoutFormat.Parse(xml, new ConversionReport());
        var finder = new KeystrokeFinder(back);
        foreach (var c in new[] { "ā", "ṭ", "ṝ", "ś", "ß", "@" }) Assert.True(finder.CanType(c), c);
        Assert.Equal((byte)'Z', back.ScanCodes[0x15].VirtualKey); // German QWERTZ: Z next to T
    }

    [Fact]
    public void Xkb_round_trips_four_levels_and_writes_compose_file()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var report = new ConversionReport();
        var symbols = XkbSymbolsFormat.Serialize(layout, report);
        Assert.Contains("key <AD01>", symbols);
        Assert.Contains("FOUR_LEVEL", symbols);
        var compose = XkbSymbolsFormat.WriteXCompose(layout);
        Assert.Contains("ṝ", compose);

        var back = XkbSymbolsFormat.Parse(symbols, new ConversionReport());
        Assert.Equal("ā", back.FindKey((byte)'A')![ShiftState.AltGr]?.Text);
        Assert.Equal((byte)'Z', back.ScanCodes[0x15].VirtualKey);
    }

    [Fact]
    public void Physical_key_codes_map_between_platforms()
    {
        Assert.Equal((50, "TLDE"), (PhysicalKeyCodes.ByScanCode(0x29)!.MacCode, PhysicalKeyCodes.ByScanCode(0x29)!.XkbName));
        Assert.Equal(0x56, PhysicalKeyCodes.ByMacCode(10)!.ScanCode);
    }
}

public class ArmAndPackagingTests
{
    [Fact]
    public void Arm64_dll_has_the_right_machine_and_no_relocations()
    {
        var layout = TestLayouts.Everything();
        var dll = LayoutDllBuilder.BuildImage(layout, LayoutArchitecture.Arm64);
        var pe = PeImage.Parse(dll);
        Assert.Equal(0xAA64, pe.Machine);
        Assert.True(pe.IsPe32Plus);
        // adrp/add/ret: the stub computes the address itself, so the tables need only data relocations.
        var stub = pe.Read(pe.FindExport("KbdLayerDescriptor"), 12).ToArray();
        Assert.Equal(0xD65F03C0u, BitConverter.ToUInt32(stub, 8));
        Assert.Equal(KeyservantFormat.Serialize(layout), Normalize(LayoutDllReader.Read(dll), layout.Metadata));
    }

    private static string Normalize(KeyboardLayout layout, LayoutMetadata metadata)
    {
        layout.Metadata = metadata.Clone();
        return KeyservantFormat.Serialize(layout);
    }

    [Fact]
    public void Build_includes_arm64_by_default()
    {
        var result = LayoutDllBuilder.Build(LayoutTemplates.Create("en-US"));
        Assert.NotNull(result.Arm64);
        Assert.Null(LayoutDllBuilder.Build(LayoutTemplates.Create("en-US"), new LayoutBuildOptions { IncludeArm64 = false }).Arm64);
    }

    [Fact]
    public void Packages_carry_arm64_and_enterprise_files()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var package = LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout));
        var stream = new MemoryStream();
        package.WriteTo(stream);
        stream.Position = 0;
        Assert.NotNull(LayoutPackage.ReadFrom(stream).DllArm64);

        var dir = Directory.CreateTempSubdirectory("keyservant-msi");
        try
        {
            var wxs = File.ReadAllText(EnterprisePackaging.WriteWixProject(package, dir.FullName));
            System.Xml.Linq.XDocument.Parse(wxs);
            Assert.Contains(EnterprisePackaging.MsiKlid(package), wxs);
            Assert.Matches("^5[0-9A-F]{3}$", EnterprisePackaging.MsiLayoutId(package));
            Assert.True(File.Exists(Path.Combine(dir.FullName, "x64", package.DllFileName)));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}

public class ShiftStateTests
{
    [Fact]
    public void Removing_a_shift_state_drops_its_column_and_characters()
    {
        var layout = LayoutTemplates.German("de-AT");
        layout.SetOutput((byte)'A', ShiftState.ShiftAltGr, KeyOutput.Of("Ā"));
        var columns = layout.Modifiers.ColumnCount;
        layout.RemoveShiftState(ShiftState.Ctrl);
        Assert.Equal(columns - 1, layout.Modifiers.ColumnCount);
        Assert.Null(layout.Modifiers.ColumnOf(ShiftState.Ctrl));
        Assert.Equal("Ā", layout.FindKey((byte)'A')![ShiftState.ShiftAltGr]!.Text);
        Assert.DoesNotContain(layout.Keys, k => k.Outputs.ContainsKey(ShiftState.Ctrl));
        var dll = LayoutDllBuilder.Build(layout);
        Assert.Equal("Ā", LayoutDllReader.Read(dll.X64).FindKey((byte)'A')![ShiftState.ShiftAltGr]!.Text);
    }

    [Fact]
    public void Kana_states_get_their_own_column_and_modifier_key()
    {
        var layout = LayoutTemplates.Create("en-US");
        var kana = new ShiftState(ModifierBits.Kana);
        layout.SetOutput((byte)'A', kana, KeyOutput.Of("ち"));
        Assert.NotNull(layout.Modifiers.ColumnOf(kana));
        Assert.Contains(layout.Modifiers.VkToBits, v => v.VirtualKey == VirtualKeys.Kana);
        var back = LayoutDllReader.Read(LayoutDllBuilder.Build(layout).X64);
        Assert.Equal("ち", back.FindKey((byte)'A')![kana]!.Text);
    }
}
