using Kbdking.Cli;

namespace Kbdking.Tests;

public sealed partial class CliTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("kbdking-cli");

    public void Dispose() => _dir.Delete(recursive: true);

    private (int Code, string Output) Run(params string[] args)
    {
        var output = new StringWriter();
        var code = Program.Run(args, output);
        return (code, output.ToString());
    }

    private string P(string name) => Path.Combine(_dir.FullName, name);

    [Fact]
    public void New_validate_build_and_convert()
    {
        var file = P("iast.kbdking");
        Assert.Equal(0, Run("new", "de-AT", "-o", file, "--name", "IAST", "--dll", "kbdiast").Code);
        Assert.Contains("\"dllName\": \"kbdiast\"", File.ReadAllText(file));

        var (code, output) = Run("validate", file);
        Assert.Equal(0, code);
        Assert.Contains("OK", output);

        Assert.Equal(0, Run("build", file, "-o", P("out")).Code);
        Assert.True(File.Exists(P("out/x64/kbdiast.dll")));
        Assert.True(File.Exists(P("out/wow64/kbdiast.dll")));

        Assert.Equal(0, Run("convert", file, P("iast.klc")).Code);
        Assert.Equal(0, Run("convert", P("iast.klc"), P("roundtrip.kbdking")).Code);
        Assert.Equal(0, Run("convert", P("out/x64/kbdiast.dll"), P("fromdll.kbdking")).Code);
        Assert.Equal(0, Run("convert", file, P("iast.c")).Code);
        Assert.Contains("KbdLayerDescriptor", File.ReadAllText(P("iast.c")));
    }

    [Fact]
    public void Invalid_layout_returns_exit_code_3()
    {
        var file = P("bad.kbdking");
        Run("new", "en-US", "-o", file, "--dll", "this-is-not-valid");
        Assert.Equal(3, Run("validate", file).Code);
        Assert.Equal(3, Run("build", file).Code);
    }

    [Fact]
    public void Usage_errors_return_exit_code_2()
    {
        Assert.Equal(2, Run().Code);
        Assert.Equal(2, Run("frobnicate").Code);
        Assert.Equal(2, Run("new", "en-US").Code);                       // missing -o
        Assert.Equal(2, Run("validate", "x.kbdking", "--bogus", "1").Code);
    }

    [Fact]
    public void Missing_files_fail_with_a_message()
    {
        var (code, output) = Run("validate", P("nope.kbdking"));
        Assert.Equal(1, code);
        Assert.Contains("not found", output);
    }

    [Fact]
    public void Package_creates_an_installer_from_a_stub()
    {
        var file = P("us.kbdking");
        Run("new", "en-US", "-o", file);
        var stub = P("stub.exe");
        File.WriteAllBytes(stub, "MZ"u8.ToArray());
        Assert.Equal(0, Run("package", file, "-o", P("setup.exe"), "--stub", stub).Code);
        Assert.NotNull(Binary.Packaging.LayoutPackage.ReadFromInstaller(P("setup.exe")));
    }

    [Fact]
    public void Templates_are_listed()
    {
        var (code, output) = Run("templates");
        Assert.Equal(0, code);
        Assert.Contains("de-AT", output);
    }
}
