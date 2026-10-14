using Keyservant.Binary;
using Keyservant.Binary.Packaging;
using Keyservant.Core.Templates;
using Keyservant.Platform;

namespace Keyservant.Tests;

public class PackagingAndPlatformTests
{
    [Fact]
    public void Package_round_trips()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        var package = LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout));
        using var ms = new MemoryStream();
        package.WriteTo(ms);
        ms.Position = 0;
        var back = LayoutPackage.ReadFrom(ms);

        Assert.Equal(package.Name, back.Name);
        Assert.Equal("kbdiast.dll", back.DllFileName);
        Assert.Equal((ushort)0x0C07, back.LanguageId);
        Assert.Equal(package.DllX64, back.DllX64);
        Assert.Equal(package.DllWow64, back.DllWow64);
        Assert.Equal(layout.Metadata.Name, back.ReadLayout().Metadata.Name);
    }

    [Fact]
    public void Installer_payload_is_appended_and_found_again()
    {
        var dir = Directory.CreateTempSubdirectory("keyservant-test");
        try
        {
            var stub = Path.Combine(dir.FullName, "stub.exe");
            File.WriteAllBytes(stub, "MZ fake stub"u8.ToArray());
            var layout = LayoutTemplates.UnitedStates();
            var package = LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout));
            var setup = Path.Combine(dir.FullName, "setup.exe");
            package.WriteInstaller(stub, setup);

            Assert.StartsWith("MZ fake stub", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(setup)[..12]));
            var back = LayoutPackage.ReadFromInstaller(setup);
            Assert.NotNull(back);
            Assert.Equal(package.DllX64, back!.DllX64);
            Assert.Null(LayoutPackage.ReadFromInstaller(stub));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Package_rejects_path_traversal_in_dll_name()
    {
        var layout = LayoutTemplates.UnitedStates();
        var package = LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout));
        using var ms = new MemoryStream();
        new LayoutPackage
        {
            Name = package.Name, DllFileName = "..\\evil.dll", Locale = package.Locale, LanguageId = package.LanguageId,
            Version = package.Version, DllX64 = package.DllX64, LayoutSource = package.LayoutSource,
        }.WriteTo(ms);
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => LayoutPackage.ReadFrom(ms));
    }

    [Fact]
    public void Klid_allocation_uses_msklc_style_ids()
    {
        Assert.Equal("A0000C07", WindowsLayoutSystem.AllocateKlid(["00000407", "00000C07"], 0x0C07));
        Assert.Equal("A0010C07", WindowsLayoutSystem.AllocateKlid(["A0000C07"], 0x0C07));
        Assert.Equal("A0000409", WindowsLayoutSystem.AllocateKlid(["a0000c07"], 0x0409));
    }

    [Fact]
    public void Layout_id_allocation_picks_one_above_the_maximum()
    {
        Assert.Equal("00C2", WindowsLayoutSystem.AllocateLayoutId(["0001", null, "00C1", "junk"]));
        Assert.Equal("0001", WindowsLayoutSystem.AllocateLayoutId([]));
    }

    [Fact]
    public void Input_tip_uses_the_language_in_the_klid()
    {
        var l = new InstalledLayout { Klid = "A0000C07", LayoutFile = "kbdiast.dll", Name = "IAST" };
        Assert.Equal((ushort)0x0C07, l.LanguageId);
        Assert.Equal("0C07:A0000C07", l.InputTip());
    }
}
