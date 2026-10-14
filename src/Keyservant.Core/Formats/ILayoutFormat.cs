using Keyservant.Core.Model;

namespace Keyservant.Core.Formats;

/// <summary>
/// What an import or export could not carry over. Conversions never drop data silently:
/// every loss or approximation is listed here so the user can see it.
/// </summary>
public sealed class ConversionReport
{
    public List<string> Warnings { get; } = [];

    public bool IsLossless => Warnings.Count == 0;

    public void Warn(string message) => Warnings.Add(message);

    public override string ToString() => IsLossless ? "No losses." : string.Join(Environment.NewLine, Warnings);
}

/// <summary>A file format that keyboard layouts can be read from and/or written to.</summary>
public interface ILayoutFormat
{
    /// <summary>Short identifier used on the command line, e.g. <c>klc</c>.</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>File extensions including the dot, e.g. <c>.klc</c>.</summary>
    IReadOnlyList<string> Extensions { get; }

    bool CanRead { get; }

    bool CanWrite { get; }

    KeyboardLayout Read(Stream input, ConversionReport report);

    void Write(KeyboardLayout layout, Stream output, ConversionReport report);
}

/// <summary>The set of formats available to the editor and the CLI.</summary>
public sealed class LayoutFormatRegistry
{
    private readonly List<ILayoutFormat> _formats = [];

    public IReadOnlyList<ILayoutFormat> Formats => _formats;

    /// <summary>Formats implemented in Keyservant.Core. Keyservant.Binary adds the DLL format.</summary>
    public static LayoutFormatRegistry CreateDefault()
    {
        var r = new LayoutFormatRegistry();
        r.Register(new KeyservantFormat());
        r.Register(new KlcFormat());
        r.Register(new MacKeylayoutFormat());
        r.Register(new XkbSymbolsFormat());
        return r;
    }

    public void Register(ILayoutFormat format) => _formats.Add(format);

    public ILayoutFormat? FindById(string id) =>
        _formats.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public ILayoutFormat? FindByPath(string path)
    {
        var name = Path.GetFileName(path);
        return _formats.FirstOrDefault(f => f.Extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
    }

    public KeyboardLayout ReadFile(string path, ConversionReport report)
    {
        var format = FindByPath(path) ?? throw new NotSupportedException($"No format recognises '{Path.GetFileName(path)}'.");
        if (!format.CanRead) throw new NotSupportedException($"{format.DisplayName} files can't be imported yet.");
        using var stream = File.OpenRead(path);
        return format.Read(stream, report);
    }

    public void WriteFile(KeyboardLayout layout, string path, ConversionReport report, ILayoutFormat? format = null)
    {
        format ??= FindByPath(path) ?? throw new NotSupportedException($"No format recognises '{Path.GetFileName(path)}'.");
        if (!format.CanWrite) throw new NotSupportedException($"{format.DisplayName} files can't be exported yet.");
        using var stream = File.Create(path);
        format.Write(layout, stream, report);
    }
}
