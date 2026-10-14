using Keyservant.Core.Model;

namespace Keyservant.Core.Formats;

/// <summary>
/// macOS .keylayout (XML state machine). Planned for milestone M5; registered now so the editor and
/// CLI can list it and tell users it is coming.
/// </summary>
public sealed class MacKeylayoutFormat : ILayoutFormat
{
    public string Id => "keylayout";
    public string DisplayName => "macOS keyboard layout";
    public IReadOnlyList<string> Extensions { get; } = [".keylayout"];
    public bool CanRead => false;
    public bool CanWrite => false;

    public KeyboardLayout Read(Stream input, ConversionReport report) =>
        throw new NotSupportedException("macOS .keylayout import is planned for milestone M5.");

    public void Write(KeyboardLayout layout, Stream output, ConversionReport report) =>
        throw new NotSupportedException("macOS .keylayout export is planned for milestone M5.");
}

/// <summary>Linux XKB symbols files. Planned for milestone M5.</summary>
public sealed class XkbSymbolsFormat : ILayoutFormat
{
    public string Id => "xkb";
    public string DisplayName => "Linux XKB symbols";
    public IReadOnlyList<string> Extensions { get; } = [".xkb"];
    public bool CanRead => false;
    public bool CanWrite => false;

    public KeyboardLayout Read(Stream input, ConversionReport report) =>
        throw new NotSupportedException("XKB import is planned for milestone M5.");

    public void Write(KeyboardLayout layout, Stream output, ConversionReport report) =>
        throw new NotSupportedException("XKB export is planned for milestone M5.");
}
