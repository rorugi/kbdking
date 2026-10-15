using Keyservant.Core.Formats;

namespace Keyservant.Tests;

public sealed partial class CliTests
{
    private string Sample()
    {
        var file = P("iast.keyservant");
        File.Copy(Path.Combine(TestLayouts.SamplesDirectory, "german-austria-iast.keyservant"), file, overwrite: true);
        return file;
    }

    [Fact]
    public void Build_writes_all_three_architectures()
    {
        var file = Sample();
        Assert.Equal(0, Run("build", file, "-o", P("out")).Code);
        foreach (var arch in new[] { "x64", "wow64", "arm64" })
            Assert.True(File.Exists(P($"out/{arch}/kbdiast.dll")), arch);
        Assert.Equal(0, Run("build", file, "-o", P("out2"), "--no-arm64").Code);
        Assert.False(Directory.Exists(P("out2/arm64")));
    }

    [Fact]
    public void Convert_to_macos_and_linux()
    {
        var file = Sample();
        Assert.Equal(0, Run("convert", file, P("iast.keylayout")).Code);
        Assert.Contains("<keyboard", File.ReadAllText(P("iast.keylayout")));
        Assert.Equal(0, Run("convert", file, P("iast.xkb")).Code);
        Assert.True(File.Exists(P("iast.XCompose")));
        Assert.Equal(0, Run("convert", P("iast.keylayout"), P("back.keyservant")).Code);
    }

    [Fact]
    public void Test_command_reports_failures()
    {
        var file = Sample();
        var layout = KeyservantFormat.Parse(File.ReadAllText(file));
        layout.Tests.Add(new Core.Model.LayoutTestCase("AltGr+. t", "ṭ"));
        File.WriteAllText(file, KeyservantFormat.Serialize(layout));
        var (code, output) = Run("test", file);
        Assert.Equal(0, code);
        Assert.Contains("All 6 tests passed", output); // the sample has five of its own

        layout.Tests.Add(new Core.Model.LayoutTestCase("a", "b"));
        File.WriteAllText(file, KeyservantFormat.Serialize(layout));
        Assert.Equal(3, Run("test", file).Code);
    }

    [Fact]
    public void Suggest_applies_a_plan()
    {
        var file = Sample();
        var (code, output) = Run("suggest", file, "--chars", "ēō", "-o", P("more.keyservant"));
        Assert.Equal(0, code);
        Assert.Contains("ē", output);
        var layout = KeyservantFormat.Parse(File.ReadAllText(P("more.keyservant")));
        Assert.True(new Core.Analysis.KeystrokeFinder(layout).CanType("ō"));
    }

    [Fact]
    public void Compose_import_list_and_export()
    {
        var file = Sample();
        File.WriteAllText(P("my.XCompose"), "<Multi_key> <minus> <minus> : \"–\"\n<Multi_key> <o> <c> : \"©\"\n");
        Assert.Equal(0, Run("compose", "import", file, P("my.XCompose")).Code);
        var (code, list) = Run("compose", "list", file);
        Assert.Equal(0, code);
        Assert.Contains("©", list);
        Assert.Equal(0, Run("compose", "export", file, P("out.XCompose")).Code);
        Assert.Contains("<Multi_key> <o> <c>", File.ReadAllText(P("out.XCompose")));
        Assert.Equal(0, Run("validate", file).Code);
    }

    [Fact]
    public void Analyze_diff_render_and_unicode()
    {
        var file = Sample();
        File.WriteAllText(P("text.txt"), "kṛṣṇa arjuna");
        var (code, output) = Run("analyze", file, P("text.txt"), "--heatmap", P("heat.svg"));
        Assert.Equal(0, code);
        Assert.Contains("per character", output);
        Assert.True(File.Exists(P("heat.svg")));

        Assert.Equal(0, Run("new", "de-AT", "-o", P("plain.keyservant")).Code);
        (code, output) = Run("diff", P("plain.keyservant"), file, "--svg", P("diff.svg"));
        Assert.Equal(0, code);
        Assert.Contains("ā", output);

        Assert.Equal(0, Run("render", file, "-o", P("stickers.svg"), "--stickers", "--shape", "jis").Code);
        Assert.Equal(2, Run("render", file, "-o", P("x.svg"), "--shape", "round").Code);

        (code, output) = Run("unicode", "a", "macron");
        Assert.Equal(0, code);
        Assert.Contains("U+0101", output);
    }

    [Fact]
    public void Package_writes_wix_source()
    {
        var file = Sample();
        Assert.Equal(0, Run("package", file, "--wix", P("wix")).Code);
        Assert.Contains("<Package", File.ReadAllText(P("wix/Package.wxs")));
    }
}
