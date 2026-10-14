using Keyservant.Binary.Kbd;
using Keyservant.Binary.Pe;
using Keyservant.Core.Model;
using Keyservant.Core.Validation;

namespace Keyservant.Binary;

/// <summary>Options for <see cref="LayoutDllBuilder"/>.</summary>
public sealed record LayoutBuildOptions
{
    /// <summary>Also build the 32-bit DLL for SysWOW64, used by 32-bit applications.</summary>
    public bool IncludeWow64 { get; init; } = true;

    /// <summary>Embed the display-name string and version resources.</summary>
    public bool IncludeResources { get; init; } = true;

    /// <summary>Build even if the validator reports errors. Only for tests and diagnostics.</summary>
    public bool IgnoreValidationErrors { get; init; }
}

/// <summary>The DLLs built from one layout.</summary>
public sealed class LayoutBuildResult
{
    public required string FileName { get; init; }
    public required byte[] X64 { get; init; }
    public byte[]? Wow64 { get; init; }
    public required ValidationResult Validation { get; init; }

    /// <summary>Registry value for "Layout Display Name", pointing at the string resource in the DLL.</summary>
    public string DisplayNameResource => $"@%SystemRoot%\\system32\\{FileName},-{ResourceSectionBuilder.DisplayNameStringId}";
}

/// <summary>The layout has validation errors and can't be built.</summary>
public sealed class LayoutBuildException(ValidationResult validation)
    : Exception("The layout has errors:" + Environment.NewLine + string.Join(Environment.NewLine, validation.Errors.Select(e => "  " + e)))
{
    public ValidationResult Validation { get; } = validation;
}

/// <summary>
/// Turns a <see cref="KeyboardLayout"/> into Windows keyboard layout DLLs, written directly as PE files.
/// No compiler, WDK or MSKLC is needed, and the build runs on any OS.
/// </summary>
public static class LayoutDllBuilder
{
    public static LayoutBuildResult Build(KeyboardLayout layout, LayoutBuildOptions? options = null)
    {
        options ??= new LayoutBuildOptions();
        var validation = LayoutValidator.Validate(layout);
        if (validation.HasErrors && !options.IgnoreValidationErrors) throw new LayoutBuildException(validation);

        var fileName = layout.Metadata.DllName.ToLowerInvariant() + ".dll";
        return new LayoutBuildResult
        {
            FileName = fileName,
            X64 = BuildImage(layout, LayoutArchitecture.X64, fileName, options),
            Wow64 = options.IncludeWow64 ? BuildImage(layout, LayoutArchitecture.X86Wow64, fileName, options) : null,
            Validation = validation,
        };
    }

    /// <summary>Builds one DLL for one architecture.</summary>
    public static byte[] BuildImage(KeyboardLayout layout, LayoutArchitecture architecture, string? fileName = null, LayoutBuildOptions? options = null)
    {
        options ??= new LayoutBuildOptions();
        fileName ??= layout.Metadata.DllName.ToLowerInvariant() + ".dll";
        var data = new SectionBuffer(".data");
        var tables = KbdTablesWriter.Write(layout, data);
        var version = ParseVersion(layout.Metadata.Version);

        var writer = new PeImageWriter
        {
            Architecture = architecture,
            DllFileName = fileName,
            Data = data,
            ExportTarget = tables,
            ExportName = KbdConstants.ExportName,
            ImageVersionMajor = (ushort)version.Major,
            ImageVersionMinor = (ushort)Math.Max(0, version.Minor),
            Resources = options.IncludeResources
                ? rva => ResourceSectionBuilder.Build(rva, layout.Metadata.Name, new VersionInfo(
                    version,
                    FileDescription: $"{layout.Metadata.Name} keyboard layout",
                    ProductName: layout.Metadata.Name,
                    CompanyName: layout.Metadata.Company ?? layout.Metadata.Author ?? "",
                    LegalCopyright: layout.Metadata.Copyright ?? "",
                    InternalName: Path.GetFileNameWithoutExtension(fileName),
                    OriginalFilename: fileName))
                : null,
        };
        return writer.Write();
    }

    private static Version ParseVersion(string text)
    {
        if (Version.TryParse(text, out var v)) return v;
        if (int.TryParse(text, out var major)) return new Version(major, 0);
        return new Version(1, 0);
    }
}
