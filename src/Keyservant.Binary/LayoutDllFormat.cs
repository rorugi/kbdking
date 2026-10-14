using Keyservant.Binary.Kbd;
using Keyservant.Core.Formats;
using Keyservant.Core.Model;

namespace Keyservant.Binary;

/// <summary>
/// Windows layout DLLs as an import/export format. Export writes the 64-bit DLL only; use
/// <see cref="LayoutDllBuilder"/> (or <c>keyservant build</c>) to get the WOW64 DLL as well.
/// </summary>
public sealed class LayoutDllFormat : ILayoutFormat
{
    public string Id => "dll";
    public string DisplayName => "Windows layout DLL";
    public IReadOnlyList<string> Extensions { get; } = [".dll"];
    public bool CanRead => true;
    public bool CanWrite => true;

    public KeyboardLayout Read(Stream input, ConversionReport report)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        return LayoutDllReader.Read(ms.ToArray(), report);
    }

    public void Write(KeyboardLayout layout, Stream output, ConversionReport report)
    {
        var result = LayoutDllBuilder.Build(layout, new LayoutBuildOptions { IncludeWow64 = false });
        foreach (var w in result.Validation.Warnings) report.Warn(w.Message);
        report.Warn("Only the 64-bit DLL was written; 32-bit apps need the WOW64 DLL from 'keyservant build'.");
        output.Write(result.X64);
    }

    /// <summary>The core formats plus the DLL format.</summary>
    public static LayoutFormatRegistry CreateRegistry()
    {
        var r = LayoutFormatRegistry.CreateDefault();
        r.Register(new LayoutDllFormat());
        r.Register(new KbdCSourceFormat());
        return r;
    }
}
