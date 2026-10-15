using System.Text;
using Kbdking.Binary.Kbd;
using Kbdking.Core.Formats;
using Kbdking.Core.Model;

namespace Kbdking.Binary;

/// <summary>Export-only format: C source for building the layout with MSVC or clang.</summary>
public sealed class KbdCSourceFormat : ILayoutFormat
{
    public string Id => "c";
    public string DisplayName => "C source (kbd.h tables)";
    public IReadOnlyList<string> Extensions { get; } = [".c"];
    public bool CanRead => false;
    public bool CanWrite => true;

    public KeyboardLayout Read(Stream input, ConversionReport report) =>
        throw new NotSupportedException("C sources can't be imported; build them and import the DLL instead.");

    public void Write(KeyboardLayout layout, Stream output, ConversionReport report)
    {
        var bytes = new UTF8Encoding(false).GetBytes(KbdCSourceWriter.Write(layout));
        output.Write(bytes);
    }
}
