using System.Globalization;
using System.Text;

namespace Keyservant.Core.Model;

/// <summary>Flags in the high byte of a scan-code mapping (kbd.h: KBDEXT, KBDMULTIVK, ...).</summary>
[Flags]
public enum ScanCodeFlags : ushort
{
    None = 0,
    Ext = 0x0100,
    MultiVk = 0x0200,
    Special = 0x0400,
    Numpad = 0x0800,
    Unicode = 0x1000,
    InjectedVk = 0x2000,
    MappedVk = 0x4000,
    Break = 0x8000,
}

/// <summary>The virtual key a scan code produces, plus processing flags.</summary>
public readonly record struct ScanCodeTarget(byte VirtualKey, ScanCodeFlags Flags)
{
    public const byte NoKey = 0xFF;

    public static ScanCodeTarget None => new(NoKey, ScanCodeFlags.None);

    public bool IsNone => VirtualKey == NoKey && Flags == ScanCodeFlags.None;

    public ushort Raw => (ushort)(VirtualKey | (ushort)Flags);

    public static ScanCodeTarget FromRaw(ushort raw) => new((byte)(raw & 0xFF), (ScanCodeFlags)(raw & 0xFF00));

    /// <summary>Formats as e.g. <c>RSHIFT|EXT</c>.</summary>
    public override string ToString()
    {
        if (IsNone) return "NONE";
        var sb = new StringBuilder(VirtualKey == NoKey ? "NONE" : VirtualKeys.GetName(VirtualKey));
        foreach (var flag in Enum.GetValues<ScanCodeFlags>())
        {
            if (flag != ScanCodeFlags.None && Flags.HasFlag(flag)) sb.Append('|').Append(flag.ToString().ToUpperInvariant());
        }
        return sb.ToString();
    }

    public static ScanCodeTarget Parse(string text)
    {
        var parts = text.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException("Empty scan code target.");
        var vk = parts[0].Equals("NONE", StringComparison.OrdinalIgnoreCase) ? NoKey : VirtualKeys.Parse(parts[0]);
        var flags = ScanCodeFlags.None;
        foreach (var p in parts.Skip(1))
        {
            if (!Enum.TryParse<ScanCodeFlags>(p, ignoreCase: true, out var f))
                throw new FormatException($"Unknown scan code flag '{p}'.");
            flags |= f;
        }
        return new ScanCodeTarget(vk, flags);
    }
}

/// <summary>Scan code to virtual key tables (kbd.h: pusVSCtoVK, pVSCtoVK_E0, pVSCtoVK_E1).</summary>
public sealed class ScanCodeMap
{
    /// <summary>Unprefixed scan codes; index = scan code.</summary>
    public List<ScanCodeTarget> Normal { get; } = [];

    /// <summary>Scan codes sent with an E0 prefix.</summary>
    public SortedDictionary<byte, ScanCodeTarget> E0 { get; } = new();

    /// <summary>Scan codes sent with an E1 prefix (Pause).</summary>
    public SortedDictionary<byte, ScanCodeTarget> E1 { get; } = new();

    public ScanCodeTarget this[int scanCode] => scanCode < Normal.Count ? Normal[scanCode] : ScanCodeTarget.None;

    /// <summary>The first unprefixed scan code that produces the virtual key, if any.</summary>
    public int? FindScanCode(byte vk)
    {
        for (var i = 0; i < Normal.Count; i++)
        {
            if (Normal[i].VirtualKey == vk) return i;
        }
        return null;
    }

    public void Set(int scanCode, ScanCodeTarget target)
    {
        while (Normal.Count <= scanCode) Normal.Add(ScanCodeTarget.None);
        Normal[scanCode] = target;
    }

    public ScanCodeMap Clone()
    {
        var m = new ScanCodeMap();
        m.Normal.AddRange(Normal);
        foreach (var (k, v) in E0) m.E0[k] = v;
        foreach (var (k, v) in E1) m.E1[k] = v;
        return m;
    }

    internal static string FormatScan(int sc) => sc.ToString("X2", CultureInfo.InvariantCulture);
}
