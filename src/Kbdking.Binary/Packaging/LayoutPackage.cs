using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kbdking.Core.Formats;
using Kbdking.Core.Model;

namespace Kbdking.Binary.Packaging;

/// <summary>
/// Everything needed to install one layout on a PC: both DLLs, the source layout and a manifest.
/// Stored inside standalone installers and in kbdking's own store (used for repair).
/// </summary>
public sealed class LayoutPackage
{
    public const string ManifestEntry = "manifest.json";
    public const string LayoutEntry = "layout.kbdking";

    public required string Name { get; init; }
    public required string DllFileName { get; init; }
    public required string Locale { get; init; }
    public required ushort LanguageId { get; init; }
    public required string Version { get; init; }

    /// <summary>The KLID if the layout already has one; otherwise the installer assigns one.</summary>
    public string? LayoutId { get; init; }

    /// <summary>String ID of the layout name inside the DLL (1000 in packages made before it followed the name).</summary>
    public int DisplayNameId { get; init; } = 1000;

    /// <summary>The "Layout Display Name" registry value: where Windows finds the name.</summary>
    public string DisplayNameReference => $"@%SystemRoot%\\system32\\{DllFileName},-{DisplayNameId}";

    public required byte[] DllX64 { get; init; }
    public byte[]? DllWow64 { get; init; }
    public byte[]? DllArm64 { get; init; }

    /// <summary>The .kbdking source, so a repaired or exported layout keeps all its information.</summary>
    public required string LayoutSource { get; init; }

    public static LayoutPackage Create(KeyboardLayout layout, LayoutBuildResult build) => new()
    {
        Name = layout.Metadata.Name,
        DllFileName = build.FileName,
        Locale = layout.Metadata.Locale,
        LanguageId = layout.Metadata.LanguageId,
        Version = layout.Metadata.Version,
        LayoutId = layout.Metadata.LayoutId,
        DisplayNameId = Pe.ResourceSectionBuilder.DisplayNameStringId(build.DisplayName),
        DllX64 = build.X64,
        DllWow64 = build.Wow64,
        DllArm64 = build.Arm64,
        LayoutSource = KbdkingFormat.Serialize(layout),
    };

    public KeyboardLayout ReadLayout() => KbdkingFormat.Parse(LayoutSource);

    /// <summary>The same package with the DLLs installed under another file name.</summary>
    public LayoutPackage WithDllFileName(string fileName) => new()
    {
        Name = Name, DllFileName = fileName, Locale = Locale, LanguageId = LanguageId, Version = Version, LayoutId = LayoutId,
        DisplayNameId = DisplayNameId, DllX64 = DllX64, DllWow64 = DllWow64, DllArm64 = DllArm64, LayoutSource = LayoutSource,
    };

    // ---------------------------------------------------------------- container

    private static readonly byte[] HeaderMagic = "KSVTPKG1"u8.ToArray();
    private static readonly byte[] FooterMagic = "KSVTEND1"u8.ToArray();

    /// <summary>Writes the package as a stream of named entries.</summary>
    public void WriteTo(Stream output)
    {
        var manifest = new JsonObject
        {
            ["format"] = "kbdking-package",
            ["version"] = 1,
            ["name"] = Name,
            ["dllFileName"] = DllFileName,
            ["locale"] = Locale,
            ["languageId"] = (int)LanguageId,
            ["layoutVersion"] = Version,
            ["layoutId"] = LayoutId,
            ["hasWow64"] = DllWow64 is not null,
            ["displayNameId"] = DisplayNameId,
        };
        var entries = new List<(string Name, byte[] Data)>
        {
            (ManifestEntry, Encoding.UTF8.GetBytes(manifest.ToJsonString())),
            (LayoutEntry, Encoding.UTF8.GetBytes(LayoutSource)),
            ("x64/" + DllFileName, DllX64),
        };
        if (DllWow64 is not null) entries.Add(("wow64/" + DllFileName, DllWow64));
        if (DllArm64 is not null) entries.Add(("arm64/" + DllFileName, DllArm64));

        using var w = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        w.Write(HeaderMagic);
        w.Write(entries.Count);
        foreach (var (name, data) in entries)
        {
            w.Write(name);
            w.Write(data.Length);
            w.Write(data);
        }
    }

    public static LayoutPackage ReadFrom(Stream input)
    {
        using var r = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        if (!r.ReadBytes(8).AsSpan().SequenceEqual(HeaderMagic)) throw new InvalidDataException("Not a kbdking layout package.");
        var count = r.ReadInt32();
        if (count is < 1 or > 16) throw new InvalidDataException("Corrupt layout package.");
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var name = r.ReadString();
            var length = r.ReadInt32();
            if (length is < 0 or > 64 * 1024 * 1024) throw new InvalidDataException("Corrupt layout package.");
            var data = r.ReadBytes(length);
            if (data.Length != length) throw new InvalidDataException("The layout package is truncated.");
            entries[name] = data;
        }

        try
        {
            return FromEntries(entries);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or JsonException or NullReferenceException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new InvalidDataException($"Corrupt layout package: {ex.Message}", ex);
        }
    }

    private static LayoutPackage FromEntries(Dictionary<string, byte[]> entries)
    {
        var manifest = JsonNode.Parse(entries[ManifestEntry]) as JsonObject ?? throw new InvalidDataException("Missing manifest.");
        var dll = manifest["dllFileName"]!.GetValue<string>();
        // Only plain names like kbdiast.dll: no paths, drive letters or alternate data streams.
        if (!System.Text.RegularExpressions.Regex.IsMatch(dll, "^[A-Za-z0-9_]{1,8}\\.[Dd][Ll][Ll]$"))
            throw new InvalidDataException("Invalid DLL name in package.");
        var layoutId = manifest["layoutId"]?.GetValue<string>();
        if (layoutId is not null && !System.Text.RegularExpressions.Regex.IsMatch(layoutId, "^[0-9A-Fa-f]{8}$"))
            throw new InvalidDataException("Invalid layout ID in package.");
        return new LayoutPackage
        {
            Name = manifest["name"]!.GetValue<string>(),
            DllFileName = dll,
            Locale = manifest["locale"]!.GetValue<string>(),
            LanguageId = (ushort)manifest["languageId"]!.GetValue<int>(),
            Version = manifest["layoutVersion"]?.GetValue<string>() ?? "1.0",
            LayoutId = layoutId,
            DisplayNameId = manifest["displayNameId"]?.GetValue<int>() is int id and >= 1 and <= 65535 ? id : 1000,
            DllX64 = entries["x64/" + dll],
            DllWow64 = entries.GetValueOrDefault("wow64/" + dll),
            DllArm64 = entries.GetValueOrDefault("arm64/" + dll),
            LayoutSource = Encoding.UTF8.GetString(entries[LayoutEntry]),
        };
    }

    // ---------------------------------------------------------------- standalone installers

    /// <summary>
    /// Creates a standalone installer by appending this package to a copy of the prebuilt installer stub.
    /// </summary>
    public void WriteInstaller(string stubPath, string outputPath)
    {
        using var output = File.Create(outputPath);
        using (var stub = File.OpenRead(stubPath)) stub.CopyTo(output);
        var payloadOffset = output.Position;
        WriteTo(output);
        using var w = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        w.Write(payloadOffset);
        w.Write(FooterMagic);
    }

    /// <summary>Reads the package appended to an installer executable, or null if there is none.</summary>
    public static LayoutPackage? ReadFromInstaller(string exePath)
    {
        using var f = File.OpenRead(exePath);
        if (f.Length < 16) return null;
        f.Seek(-16, SeekOrigin.End);
        using var r = new BinaryReader(f, Encoding.UTF8, leaveOpen: true);
        var offset = r.ReadInt64();
        if (!r.ReadBytes(8).AsSpan().SequenceEqual(FooterMagic)) return null;
        if (offset < 0 || offset >= f.Length - 16) throw new InvalidDataException("Corrupt installer payload.");
        f.Seek(offset, SeekOrigin.Begin);
        return ReadFrom(f);
    }
}
