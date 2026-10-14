using System.Globalization;
using System.Runtime.Versioning;
using Keyservant.Core.Analysis;
using Keyservant.Core.Assist;
using Keyservant.Core.Compose;
using Keyservant.Core.Diff;
using Keyservant.Core.Formats;
using Keyservant.Core.Geometry;
using Keyservant.Core.Model;
using Keyservant.Core.Rendering;
using Keyservant.Core.Unicode;
using Keyservant.Platform;

namespace Keyservant.Cli;

internal static partial class Program
{
    private static int RunTests(CommandLine cmd)
    {
        var path = cmd.Arg(0, "layout file");
        cmd.EnsureNoUnknownOptions();
        var layout = Load(path);
        var results = LayoutTestRunner.Run(layout);
        if (results.Count == 0)
        {
            Out.WriteLine("The layout has no tests. Add them under \"tests\": [{\"keys\": \"AltGr+. t\", \"expect\": \"ṭ\"}].");
            return Ok;
        }
        foreach (var r in results) Out.WriteLine($"{(r.Passed ? "pass" : "FAIL")}  {r.Test.Name ?? r.Test.Keys}  {(r.Passed ? "" : r.Message)}");
        var failed = results.Count(r => !r.Passed);
        Out.WriteLine(failed == 0 ? $"All {results.Count} tests passed." : $"{failed} of {results.Count} tests failed.");
        return failed == 0 ? Ok : Invalid;
    }

    private static int Suggest(CommandLine cmd)
    {
        var path = cmd.Arg(0, "layout file");
        var chars = cmd.Option("chars");
        var textFile = cmd.Option("text");
        var apply = cmd.Flag("apply");
        var output = cmd.Option("output", "o");
        cmd.EnsureNoUnknownOptions();
        if (chars is null && textFile is null) throw new UsageException("Give the characters with --chars \"…\" or a sample text with --text <file>.");

        var layout = Load(path);
        IReadOnlyDictionary<string, int>? frequencies = null;
        var wanted = chars ?? "";
        if (textFile is not null)
        {
            var text = File.ReadAllText(textFile);
            var analysis = TextAnalyzer.Analyze(layout, text);
            frequencies = analysis.Characters.ToDictionary(c => c.Text, c => c.Count);
            wanted += string.Concat(analysis.Characters.Select(c => c.Text));
        }
        var plan = PlacementPlanner.Plan(layout, wanted, new PlacementOptions { Frequencies = frequencies });
        Out.Write(plan.Summary());
        if (!apply && output is null) return Ok;

        plan.ApplyAll(layout);
        var target = output ?? path;
        if (!target.EndsWith(".keyservant", StringComparison.OrdinalIgnoreCase)) throw new UsageException("Changes can only be saved to a .keyservant file; pass -o <file>.keyservant.");
        File.WriteAllText(target, KeyservantFormat.Serialize(layout));
        Out.WriteLine($"Applied {plan.Changes.Count} changes and saved {target}.");
        return Ok;
    }

    private static int ComposeCommand(CommandLine cmd)
    {
        var action = cmd.Arg(0, "compose action (import, export or list)").ToLowerInvariant();
        var path = cmd.Arg(1, "layout file");
        switch (action)
        {
            case "list":
            {
                cmd.EnsureNoUnknownOptions();
                var layout = Load(path);
                if (layout.Compose is not { } c || c.Sequences.Count == 0) { Out.WriteLine("The layout has no compose sequences."); return Ok; }
                Out.WriteLine($"Compose key: {c.KeyText}, {c.Sequences.Count} sequences");
                foreach (var s in c.Sequences) Out.WriteLine($"  {s.Keys,-8} {s.Result}");
                foreach (var w in ComposeCompiler.Compile(layout).Warnings) Err.WriteLine($"warning: {w}");
                return Ok;
            }
            case "import":
            {
                var file = cmd.Arg(2, ".XCompose file");
                var key = cmd.Option("key");
                cmd.EnsureNoUnknownOptions();
                if (!path.EndsWith(".keyservant", StringComparison.OrdinalIgnoreCase)) throw new UsageException("Compose sequences can only be added to a .keyservant file.");
                var layout = Load(path);
                var report = new ConversionReport();
                var sequences = XComposeFile.Parse(File.ReadAllText(file), report);
                PrintReport(report);
                layout.Compose ??= new ComposeDefinition();
                if (key is not null) (layout.Compose.ScanCode, layout.Compose.Extended) = ComposeDefinition.ParseKey(key);
                var existing = layout.Compose.Sequences.Select(s => s.Keys).ToHashSet(StringComparer.Ordinal);
                var added = sequences.Where(s => existing.Add(s.Keys)).ToList();
                layout.Compose.Sequences.AddRange(added);
                var compiled = ComposeCompiler.Compile(layout);
                foreach (var w in compiled.Warnings.Take(20)) Err.WriteLine($"warning: {w}");
                if (compiled.Warnings.Count > 20) Err.WriteLine($"warning: … and {compiled.Warnings.Count - 20} more.");
                File.WriteAllText(path, KeyservantFormat.Serialize(layout));
                Out.WriteLine($"Added {added.Count} sequences ({compiled.CompiledSequences} usable) with the compose key on {layout.Compose.KeyText}.");
                return compiled.Errors.Count == 0 ? Ok : Invalid;
            }
            case "export":
            {
                var file = cmd.Arg(2, "output file");
                cmd.EnsureNoUnknownOptions();
                var layout = Load(path);
                File.WriteAllText(file, XComposeFile.Write(layout.Compose?.Sequences ?? []));
                Out.WriteLine($"Wrote {file}.");
                return Ok;
            }
            default:
                throw new UsageException($"Unknown compose action '{action}'. Use import, export or list.");
        }
    }

    private static int Analyze(CommandLine cmd)
    {
        var path = cmd.Arg(0, "layout file");
        var textFile = cmd.Arg(1, "text file");
        var heatmap = cmd.Option("heatmap");
        var compare = cmd.Option("compare");
        cmd.EnsureNoUnknownOptions();
        var layout = Load(path);
        var text = File.ReadAllText(textFile);
        var analysis = TextAnalyzer.Analyze(layout, text);
        Out.Write(analysis.Summary());
        if (compare is not null)
        {
            var other = TextAnalyzer.Analyze(Load(compare), text);
            Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Compared with {Path.GetFileName(compare)}: {other.EffortPerCharacter:0.00} per character ({(other.EffortPerCharacter - analysis.EffortPerCharacter) / Math.Max(0.0001, analysis.EffortPerCharacter):+0%;-0%;0%}), {other.Untypeable.Sum(c => c.Count)} untypeable."));
        }
        if (heatmap is not null)
        {
            File.WriteAllText(heatmap, KeyboardSvgRenderer.Render(layout, new RenderOptions { Heat = analysis.KeyPresses, Title = $"{layout.Metadata.Name} — {Path.GetFileName(textFile)}" }));
            Out.WriteLine($"Wrote {heatmap}.");
        }
        return Ok;
    }

    private static int Diff(CommandLine cmd)
    {
        var a = cmd.Arg(0, "first layout");
        var b = cmd.Arg(1, "second layout");
        var svg = cmd.Option("svg");
        cmd.EnsureNoUnknownOptions();
        var before = Load(a);
        var after = Load(b);
        var changes = LayoutDiff.Compare(before, after);
        Out.Write(LayoutDiff.Format(changes));
        if (svg is not null)
        {
            var keys = changes.Where(c => c.VirtualKey is not null).Select(c => c.VirtualKey!.Value).ToHashSet();
            File.WriteAllText(svg, KeyboardSvgRenderer.Render(after, new RenderOptions { Highlight = keys, Title = $"Changed keys: {Path.GetFileName(a)} → {Path.GetFileName(b)}" }));
            Out.WriteLine($"Wrote {svg}.");
        }
        return Ok;
    }

    private static int Render(CommandLine cmd)
    {
        var path = cmd.Arg(0, "layout file");
        var output = cmd.Option("output", "o") ?? throw new UsageException("Missing -o <file.svg>.");
        var stickers = cmd.Flag("stickers");
        var shapeText = cmd.Option("shape") ?? "iso";
        cmd.EnsureNoUnknownOptions();
        if (!Enum.TryParse<KeyboardShape>(shapeText, ignoreCase: true, out var shape)) throw new UsageException("--shape must be iso, ansi, jis or abnt.");
        var layout = Load(path);
        File.WriteAllText(output, KeyboardSvgRenderer.Render(layout, new RenderOptions
        {
            Mode = stickers ? RenderMode.Stickers : RenderMode.Keyboard,
            Shape = shape,
            Title = layout.Metadata.Name,
        }));
        Out.WriteLine($"Wrote {output}. Open it in a browser and print at 100% for real size.");
        return Ok;
    }

    private static int UnicodeSearch(CommandLine cmd)
    {
        var query = string.Join(' ', cmd.Positional);
        cmd.EnsureNoUnknownOptions();
        if (query.Length == 0) throw new UsageException("Give words or a code, e.g. keyservant unicode a macron");
        var results = UnicodeCatalog.Search(query, 40);
        foreach (var r in results) Out.WriteLine($"{r.Display}\t{r.Code}\t{r.Name}");
        if (results.Count == 0) Out.WriteLine("Nothing found.");
        return Ok;
    }

    [SupportedOSPlatform("windows")]
    private static int Keep(CommandLine cmd, string[] rawArgs)
    {
        var klid = cmd.Arg(0, "layout ID (KLID)");
        cmd.EnsureNoUnknownOptions();
        if (!Elevation.IsAdministrator) return Relaunch(rawArgs, null);
        new WindowsLayoutSystem().Keep(klid);
        Out.WriteLine($"{klid} is now installed permanently.");
        return Ok;
    }

    [SupportedOSPlatform("windows")]
    private static int Cleanup(CommandLine cmd, string[] rawArgs)
    {
        cmd.EnsureNoUnknownOptions();
        if (!Elevation.IsAdministrator) return Relaunch(rawArgs, null);
        var removed = new WindowsLayoutSystem().CleanupExpired();
        foreach (var r in removed) Out.WriteLine($"Removed test install {r}.");
        if (removed.Count == 0) Out.WriteLine("No expired test installs.");
        return Ok;
    }
}
