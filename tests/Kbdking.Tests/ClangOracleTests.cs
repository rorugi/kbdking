using System.Diagnostics;
using Kbdking.Binary;
using Kbdking.Binary.Kbd;
using Kbdking.Core.Formats;
using Kbdking.Core.Model;
using Kbdking.Core.Templates;

namespace Kbdking.Tests;

/// <summary>Runs only when clang-cl and lld-link are on the PATH (they are in CI).</summary>
public sealed class ClangFactAttribute : FactAttribute
{
    public ClangFactAttribute()
    {
        if (ClangOracleTests.ClangCl is null || ClangOracleTests.LldLink is null)
            Skip = "clang-cl and lld-link are not installed.";
    }
}

/// <summary>
/// Cross-checks kbdking's PE writer against a real compiler: the same layout is emitted as C,
/// compiled with clang-cl and linked with lld-link, then both DLLs are read back and compared.
/// </summary>
public class ClangOracleTests
{
    internal static string? ClangCl { get; } = Find("clang-cl", "clang-cl-18", "clang-cl-19", "clang-cl-20");
    internal static string? LldLink { get; } = Find("lld-link", "lld-link-18", "lld-link-19", "lld-link-20");

    private static string? Find(params string[] names)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var name in names)
        {
            foreach (var dir in paths)
            {
                foreach (var candidate in new[] { Path.Combine(dir, name), Path.Combine(dir, name + ".exe") })
                {
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }

    private static byte[] CompileWithClang(KeyboardLayout layout)
    {
        var dir = Directory.CreateTempSubdirectory("kbdking-clang");
        try
        {
            var c = Path.Combine(dir.FullName, "layout.c");
            File.WriteAllText(c, KbdCSourceWriter.Write(layout));
            Exec(ClangCl!, "--target=x86_64-pc-windows-msvc", "/c", "/O1", "/GS-", "/Zl", c, "/Fo" + Path.Combine(dir.FullName, "layout.obj"));
            var dll = Path.Combine(dir.FullName, "layout.dll");
            Exec(LldLink!, "/dll", "/noentry", "/nodefaultlib", "/machine:x64", "/export:KbdLayerDescriptor",
                "/out:" + dll, Path.Combine(dir.FullName, "layout.obj"));
            return File.ReadAllBytes(dll);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static void Exec(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(exe)} failed: {output}");
    }

    private static void AssertSameAsCompiler(KeyboardLayout layout)
    {
        var expected = KbdkingFormat.Serialize(layout);
        var fromClang = LayoutDllReader.Read(CompileWithClang(layout));
        var fromKbdking = LayoutDllReader.Read(LayoutDllBuilder.BuildImage(layout, Binary.Pe.LayoutArchitecture.X64));
        fromClang.Metadata = layout.Metadata.Clone();
        fromKbdking.Metadata = layout.Metadata.Clone();
        Assert.Equal(expected, KbdkingFormat.Serialize(fromClang));
        Assert.Equal(expected, KbdkingFormat.Serialize(fromKbdking));
    }

    [ClangFact]
    public void German_template_matches_the_compiler() => AssertSameAsCompiler(LayoutTemplates.German());

    [ClangFact]
    public void Everything_layout_matches_the_compiler() => AssertSameAsCompiler(TestLayouts.Everything());

    [ClangFact]
    public void Iast_sample_matches_the_compiler()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        layout.Tests.Clear(); // tests live only in the .kbdking file, not in the DLL
        AssertSameAsCompiler(layout);
    }

    [ClangFact]
    public void Layer_keys_match_the_compiler() => AssertSameAsCompiler(TestLayouts.Layered());

    [ClangFact]
    public void Qwerty_shortcuts_on_colemak_match_the_compiler()
    {
        var layout = TestLayouts.Colemak();
        Core.Editing.ShortcutPositions.Apply(layout, Core.Editing.ShortcutMode.Qwerty);
        AssertSameAsCompiler(layout);
    }
}
