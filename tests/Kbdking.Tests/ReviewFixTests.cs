using System.Xml.Linq;
using Kbdking.Binary;
using Kbdking.Binary.Kbd;
using Kbdking.Binary.Packaging;
using Kbdking.Binary.Pe;
using Kbdking.Core.Analysis;
using Kbdking.Core.Assist;
using Kbdking.Core.Compose;
using Kbdking.Core.Editing;
using Kbdking.Core.Formats;
using Kbdking.Core.Model;
using Kbdking.Core.Rendering;
using Kbdking.Core.Templates;
using Kbdking.Core.Unicode;
using Kbdking.Core.Validation;

namespace Kbdking.Tests;

/// <summary>Regression tests for problems found in the code review.</summary>
public class FormatFixTests
{
    [Fact]
    public void Klc_round_trips_characters_outside_the_bmp_and_private_use_characters()
    {
        var layout = LayoutTemplates.German("de-DE");
        layout.SetOutput((byte)'A', ShiftState.AltGr, KeyOutput.Of("𝔸"));
        layout.SetOutput((byte)'Q', ShiftState.Shift, KeyOutput.Of(""));
        var back = KlcFormat.Parse(KlcFormat.Serialize(layout, new ConversionReport()), new ConversionReport());
        Assert.Equal("𝔸", back.FindKey((byte)'A')![ShiftState.AltGr]!.Text);
        Assert.Equal("", back.FindKey((byte)'Q')![ShiftState.Shift]!.Text);
    }

    [Fact]
    public void Klc_export_keeps_names_on_one_line_and_warns_about_layer_keys()
    {
        var layout = LayoutTemplates.German("de-DE");
        layout.Metadata.Name = "Line1\nLAYOUT";
        layout.Metadata.Version = "1.0 beta";
        layout.KeyNames[0x29] = "Akzent \"Zirkumflex\"";
        LayerKeys.Add(layout, 0x3A, false, ModifierBits.LayerA);
        var report = new ConversionReport();
        var back = KlcFormat.Parse(KlcFormat.Serialize(layout, report), new ConversionReport());
        Assert.Contains(report.Warnings, w => w.Contains("Layer keys", StringComparison.Ordinal));
        Assert.Equal("Line1 LAYOUT", back.Metadata.Name);
        Assert.Equal("1.0 beta", back.Metadata.Version);
        Assert.Equal("Akzent 'Zirkumflex'", back.KeyNames[0x29]);
    }

    [Theory]
    [InlineData("""{"format":"kbdking-layout","schemaVersion":1,"scanCodes":{"count":2000000000}}""")]
    [InlineData("""{"format":"kbdking-layout","schemaVersion":1,"flags":[1]}""")]
    [InlineData("""{"format":"kbdking-layout","schemaVersion":1,"keyNames":{"01":5}}""")]
    [InlineData("""{"format":"kbdking-layout","schemaVersion":1,"modifiers":{"columns":[[null]]}}""")]
    [InlineData("""{"format":"kbdking-layout","schemaVersion":1,"keys":[{"vk":"A","base":"a"},{"vk":"A","base":"b"}]}""")]
    [InlineData("""{"format":"kbdking-layout","schemaVersion":1,"modifiers":{"maxModBits":100000000}}""")]
    public void Malformed_kbdking_files_give_format_errors(string json)
    {
        Assert.Throws<LayoutFormatException>(() => KbdkingFormat.Parse(json));
    }

    [Fact]
    public void A_sixteenth_column_is_rejected()
    {
        string[] states = ["base", "shift", "ctrl", "shift+ctrl", "altgr", "shift+altgr", "kana", "shift+kana", "layer-a", "shift+layer-a",
            "layer-b", "shift+layer-b", "layer-c", "shift+layer-c", "kana+layer-a", "kana+layer-b"];
        var columns = string.Join(",", states.Select(s => $"\"{s}\""));
        var json = "{\"format\":\"kbdking-layout\",\"schemaVersion\":1,\"modifiers\":{\"columns\":[" + columns + "]}}";
        Assert.Throws<LayoutFormatException>(() => KbdkingFormat.Parse(json));
    }

    [Fact]
    public void Keylayout_with_dashes_in_the_name_is_valid_xml_and_bad_files_give_format_errors()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        layout.Metadata.Name = "My--Layout-";
        var xml = MacKeylayoutFormat.Serialize(layout, new ConversionReport());
        Assert.Equal("My--Layout-", MacKeylayoutFormat.Parse(xml, new ConversionReport()).Metadata.Name);

        var broken = xml.Replace("mapIndex=\"0\"", "mapIndex=\"x\"", StringComparison.Ordinal);
        Assert.Throws<LayoutFormatException>(() => MacKeylayoutFormat.Parse(broken, new ConversionReport()));
    }

    [Fact]
    public void XCompose_lines_with_angle_brackets_after_the_colon_are_read()
    {
        var sequences = XComposeFile.Parse("""
            <Multi_key> <minus> <greater> : "→" U2192 # -> arrow
            <Multi_key> <a> <a> : ">"
            <Multi_key> <q> <q> : "\"" quotedbl
            """, new ConversionReport());
        Assert.Equal(new[] { new ComposeSequence("->", "→"), new ComposeSequence("aa", ">"), new ComposeSequence("qq", "\"") }, sequences);
        Assert.Equal(sequences, XComposeFile.Parse(XComposeFile.Write(sequences), new ConversionReport()));
    }

    [Fact]
    public void Xkb_compose_companion_escapes_quotes_and_backslashes()
    {
        var layout = LayoutTemplates.Create("en-US");
        layout.SetOutput((byte)'Q', ShiftState.AltGr, KeyOutput.Dead('"'));
        var d = layout.GetOrAddDeadKey('"');
        d.Set(' ', '"');
        d.Set('x', '\\');
        var text = XkbSymbolsFormat.WriteXCompose(layout);
        Assert.Contains("""<space> : "\"" U0022""", text);
        Assert.Contains("""<x> : "\\" U005C""", text);
    }

    [Fact]
    public void Xkb_import_uses_group_one_and_rejects_malformed_files()
    {
        var text = """
            xkb_symbols "basic" {
              key <AC02> { symbols[Group1] = [ w, W ], symbols[Group2] = [ x, X ] };
              key <AC01> { [ q, Q ], [ Cyrillic_ef, Cyrillic_EF ] };
            };
            """;
        var layout = XkbSymbolsFormat.Parse(text, new ConversionReport());
        Assert.Equal("w", layout.FindKey((byte)'W')![ShiftState.Base]!.Text);
        Assert.Equal("q", layout.FindKey((byte)'Q')![ShiftState.Base]!.Text);
        Assert.Throws<LayoutFormatException>(() => XkbSymbolsFormat.Parse("xkb_symbols \"basic\"", new ConversionReport()));
        Assert.Null(XkbSymbolsFormat.Parse("xkb_symbols \"x\" { key <AC01> { [ UD800 ] }; };", new ConversionReport()).FindKey((byte)'A')?[ShiftState.Base]);
    }
}

public class FeatureFixTests
{
    private static KeyboardLayout WithCompose(KeyboardLayout layout, params (string, string)[] sequences)
    {
        layout.Compose = new ComposeDefinition();
        foreach (var (k, r) in sequences) layout.Compose.Sequences.Add(new ComposeSequence(k, r));
        return layout;
    }

    [Fact]
    public void Compose_key_on_a_layer_key_or_with_a_used_accent_is_an_error()
    {
        var layout = WithCompose(TestLayouts.Sample("german-austria-iast"), (".r", "ṛ"));
        LayerKeys.Add(layout, 0x5D, true, ModifierBits.LayerA);
        Assert.NotEmpty(ComposeCompiler.Compile(layout).Errors);
        Assert.Contains(LayoutValidator.Validate(layout).Issues, i => i.Code == "KS081");

        var accent = WithCompose(TestLayouts.Sample("german-austria-iast"), ("a", "Q"));
        accent.Compose!.Accent = '´';
        Assert.Contains(ComposeCompiler.Compile(accent).Errors, e => e.Contains("accent", StringComparison.Ordinal));

        var altGr = WithCompose(LayoutTemplates.German("de-DE"), ("--", "–"));
        altGr.Compose!.ScanCode = 0x38;
        Assert.NotEmpty(ComposeCompiler.Compile(altGr).Errors);
    }

    [Fact]
    public void Copilot_key_can_be_the_compose_key()
    {
        Assert.Contains(ComposeDefinition.CommonKeys, k => k.Name == "Copilot key" && k.ScanCode == 0x6E && !k.Extended);
        var layout = WithCompose(TestLayouts.Sample("german-austria-iast"), (".r", "ṛ"));
        layout.Compose!.ScanCode = 0x6E;
        layout.Compose.Extended = false;
        var compiled = ComposeCompiler.Compile(layout);
        Assert.Empty(compiled.Errors);
        Assert.Equal(ComposeCompiler.CopilotWarning, Assert.Single(compiled.Warnings)); // Windows may catch the key first
        var sim = new TypingSimulator(compiled.Layout);
        Assert.Equal("", sim.PressScanCode(0x6E, false, ShiftState.Shift)); // the keyboard holds Shift (and Win)
        Assert.Equal("ṛ", sim.PressScanCode(0x34, false, ShiftState.Base) + sim.PressScanCode(0x13, false, ShiftState.Base));
    }

    [Fact]
    public void Compiling_twice_changes_nothing_and_duplicates_count_once()
    {
        var layout = WithCompose(LayoutTemplates.Create("en-US"), ("--", "–"), ("--", "–"), (".r", "ṛ"));
        var first = ComposeCompiler.Compile(layout);
        Assert.Equal(2, first.CompiledSequences);
        var second = ComposeCompiler.Compile(first.Layout);
        Assert.Empty(second.Errors);
        Assert.Same(first.Layout, second.Layout);
    }

    [Fact]
    public void Planner_handles_marks_in_both_orders_and_shared_columns()
    {
        var layout = LayoutTemplates.Create("en-US");
        PlacementPlanner.Plan(layout, "ǖṻ").ApplyAll(layout);
        var finder = new KeystrokeFinder(layout);
        Assert.True(finder.CanType("ǖ"));
        Assert.True(finder.CanType("ṻ"));

        var shared = LayoutTemplates.Create("en-US");
        var column = shared.Modifiers.EnsureColumn(ShiftState.AltGr);
        while (shared.Modifiers.ModNumber.Count <= ShiftState.ShiftAltGr.Value) shared.Modifiers.ModNumber.Add(ModifierTable.Invalid);
        shared.Modifiers.ModNumber[ShiftState.ShiftAltGr.Value] = (byte)column; // Shift+AltGr shares the AltGr column
        shared.SetOutput((byte)'U', ShiftState.AltGr, KeyOutput.Of("µ"));
        PlacementPlanner.Plan(shared, "Ú").ApplyAll(shared);
        Assert.Equal("µ", shared.FindKey((byte)'U')![ShiftState.AltGr]!.Text);
    }

    [Fact]
    public void Removing_a_layer_key_restores_what_the_key_was()
    {
        var layout = LayoutTemplates.German("de-DE");
        var before = layout.ScanCodes[0x2B];
        LayerKeys.Add(layout, 0x2B, false, ModifierBits.LayerA);
        LayerKeys.Add(layout, 0x2B, false, ModifierBits.LayerB); // switching layers keeps one key
        Assert.Single(LayerKeys.Find(layout));
        Assert.Equal(ModifierBits.LayerB, LayerKeys.Find(layout)[0].Layer);
        LayerKeys.Remove(layout, LayerKeys.Find(layout)[0]);
        Assert.Equal(before.VirtualKey, layout.ScanCodes[0x2B].VirtualKey);
        Assert.Equal("#", new TypingSimulator(layout).PressScanCode(0x2B, false, ShiftState.Base));
    }

    [Fact]
    public void Finder_ignores_unreachable_keys_and_types_tab()
    {
        var layout = LayoutTemplates.Create("en-US");
        layout.SetOutput(0x92, ShiftState.Base, KeyOutput.Of("ā")); // no scan code sends 0x92
        var finder = new KeystrokeFinder(layout);
        Assert.False(finder.CanType("ā"));
        Assert.True(finder.CanType("\t"));
        Assert.Empty(TextAnalyzer.Analyze(layout, "a\tb").Untypeable);
        Assert.Contains("ā", PlacementPlanner.Plan(layout, "ā").Changes.SelectMany(c => c.Characters));
    }

    [Fact]
    public void Typing_and_deleting_leaves_no_undo_step_and_keeps_the_base()
    {
        var now = new DateTime(2026, 1, 1);
        var layout = LayoutTemplates.Create("en-US");
        var original = layout.Metadata.Name;
        var history = new LayoutHistory(layout, clock: () => now);
        layout.Metadata.Name = original + "x";
        history.Record(layout, "name");
        layout.Metadata.Name = original;
        history.Record(layout, "name");
        Assert.False(history.CanUndo);
        layout.Metadata.Name = original + "y";
        history.Record(layout, "name");
        Assert.Equal(original, history.Undo().Metadata.Name);
    }

    [Fact]
    public void Svg_output_stays_valid_xml()
    {
        var layout = LayoutTemplates.Create("en-US");
        layout.SetOutput((byte)'Q', ShiftState.Base, KeyOutput.Of("a\u0001"));
        layout.SetOutput((byte)'W', ShiftState.Base, KeyOutput.Of(""));
        var svg = KeyboardSvgRenderer.Render(layout);
        XDocument.Parse(svg);
        Assert.Contains("", svg);
        XDocument.Parse(KeyboardSvgRenderer.Render(layout, new RenderOptions { Mode = RenderMode.Stickers, StickerSize = 200 }));
    }

    [Fact]
    public void Unicode_search_and_names_cover_more_cases()
    {
        Assert.False((UnicodeCatalog.Search("cafe").FirstOrDefault()?.Name ?? "").StartsWith("HANGUL", StringComparison.Ordinal));
        Assert.Equal("CJK COMPATIBILITY IDEOGRAPH-F900", UnicodeCatalog.GetName(0xF900));
        Assert.Throws<FormatException>(() => ComposeDefinition.ParseKey("E0"));
        Assert.Throws<FormatException>(() => ComposeDefinition.ParseKey("9D"));
    }

    [Fact]
    public void Versions_above_65535_are_errors()
    {
        var layout = LayoutTemplates.Create("en-US");
        layout.Metadata.Version = "70000.1";
        Assert.Contains(LayoutValidator.Validate(layout).Issues, i => i.Code == "KS006" && i.Severity == Severity.Error);
    }

    [Fact]
    public void Formatted_sequences_name_the_compose_key()
    {
        var layout = WithCompose(LayoutTemplates.Create("en-US"), (".r", "ṛ"));
        var compiled = ComposeCompiler.Compile(layout).Layout;
        var recipe = new KeystrokeFinder(compiled).Find("ṛ")!;
        var text = KeySequence.Format(recipe.Strokes, layout);
        Assert.StartsWith("Compose", text);
        layout.Tests.Add(new LayoutTestCase(text, "ṛ"));
        Assert.True(LayoutTestRunner.Run(layout)[0].Passed);
    }

    [Fact]
    public void Dead_key_loops_are_found_quickly_in_dense_graphs()
    {
        var layout = LayoutTemplates.Create("en-US");
        var accents = Enumerable.Range(0xE100, 40).Select(i => (char)i).ToList();
        foreach (var a in accents)
        {
            var d = layout.GetOrAddDeadKey(a);
            foreach (var b in accents) if (b != a) d.Set(b, b, chained: true);
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var issues = LayoutValidator.Validate(layout).Issues;
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Contains(issues, i => i.Code == "KS047");
    }
}

public class BinaryFixTests
{
    [Fact]
    public void A_jump_loop_in_the_export_is_rejected_not_followed_forever()
    {
        var dll = LayoutDllBuilder.BuildImage(LayoutTemplates.Create("en-US"), LayoutArchitecture.X64);
        var pe = PeImage.Parse(dll);
        var offset = pe.RvaToOffset(pe.FindExport("KbdLayerDescriptor"));
        byte[] jumpToItself = [0xE9, 0xFB, 0xFF, 0xFF, 0xFF];
        jumpToItself.CopyTo(dll, offset);
        Assert.Throws<InvalidLayoutDllException>(() => LayoutDllReader.Read(dll));
    }

    [Fact]
    public void Damaged_packages_give_invalid_data_errors()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var package = LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout));
        var ms = new MemoryStream();
        package.WriteTo(ms);
        var bytes = ms.ToArray();
        Assert.Throws<InvalidDataException>(() => LayoutPackage.ReadFrom(new MemoryStream(bytes[..^100])));

        var renamed = new LayoutPackage
        {
            Name = "x", DllFileName = "C:x.dll", Locale = "en-US", LanguageId = 0x409, Version = "1.0",
            DllX64 = package.DllX64, LayoutSource = package.LayoutSource,
        };
        var ms2 = new MemoryStream();
        renamed.WriteTo(ms2);
        ms2.Position = 0;
        Assert.Throws<InvalidDataException>(() => LayoutPackage.ReadFrom(ms2));
    }

    [Fact]
    public void Generated_scripts_and_sources_are_safe_with_odd_names()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        layout.Metadata.Name = "Evil\nStart-Process calc # [IAST] $(var) */ int x; /*";
        layout.Metadata.Version = "1.0.300";
        var package = LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout));
        var dir = Directory.CreateTempSubdirectory("kbdking-safe");
        try
        {
            var wxs = File.ReadAllText(EnterprisePackaging.WriteWixProject(package, dir.FullName));
            XDocument.Parse(wxs);
            Assert.Contains("Version=\"1.0.300\"", wxs);
            Assert.Contains("[\\[]IAST[\\]]", wxs);
            Assert.Contains("$$(var)", wxs);

            var stub = Path.Combine(dir.FullName, "stub.exe");
            File.WriteAllBytes(stub, [0x4D, 0x5A]);
            EnterprisePackaging.WriteIntuneFolder(package, stub, Path.Combine(dir.FullName, "intune"));
            var detect = File.ReadAllLines(Path.Combine(dir.FullName, "intune", "detect.ps1"));
            Assert.DoesNotContain(detect, l => l.TrimStart().StartsWith("Start-Process", StringComparison.Ordinal));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
        var c = KbdCSourceWriter.Write(layout);
        Assert.DoesNotContain("*/ int x;", c);
    }
}
