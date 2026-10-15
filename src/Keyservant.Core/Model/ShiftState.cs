using System.Globalization;
using System.Text;

namespace Keyservant.Core.Model;

/// <summary>Modifier bits as defined in Windows' kbd.h (KBDSHIFT, KBDCTRL, ...).</summary>
[Flags]
public enum ModifierBits : byte
{
    None = 0,
    Shift = 0x01,
    Ctrl = 0x02,
    Alt = 0x04,
    Kana = 0x08,
    Roya = 0x10,
    Loya = 0x20,
    GrpSelTap = 0x80,
    /// <summary>A custom layer key (kbd.h KBDROYA, free for other uses outside Japanese thumb-shift layouts).</summary>
    LayerA = Roya,
    /// <summary>A custom layer key (kbd.h KBDLOYA).</summary>
    LayerB = Loya,
    /// <summary>A custom layer key (the unnamed kbd.h bit 0x40).</summary>
    LayerC = 0x40,
    AltGr = Ctrl | Alt,
}

/// <summary>
/// A combination of modifier bits, such as Shift+AltGr. Each combination that produces characters
/// maps to one column of the layout's character tables.
/// Names: <c>base</c>, <c>shift</c>, <c>ctrl</c>, <c>shift+ctrl</c>, <c>altgr</c>, <c>shift+altgr</c>, <c>kana</c>, ...
/// </summary>
public readonly record struct ShiftState(ModifierBits Bits) : IComparable<ShiftState>
{
    public static ShiftState Base => new(ModifierBits.None);
    public static ShiftState Shift => new(ModifierBits.Shift);
    public static ShiftState Ctrl => new(ModifierBits.Ctrl);
    public static ShiftState ShiftCtrl => new(ModifierBits.Shift | ModifierBits.Ctrl);
    public static ShiftState AltGr => new(ModifierBits.AltGr);
    public static ShiftState ShiftAltGr => new(ModifierBits.Shift | ModifierBits.AltGr);

    public int Value => (int)Bits;

    public string Name
    {
        get
        {
            if (Bits == ModifierBits.None) return "base";
            var sb = new StringBuilder();
            void Add(string s) { if (sb.Length > 0) sb.Append('+'); sb.Append(s); }
            var b = Bits;
            if (b.HasFlag(ModifierBits.Shift)) Add("shift");
            if (b.HasFlag(ModifierBits.AltGr)) Add("altgr");
            else
            {
                if (b.HasFlag(ModifierBits.Ctrl)) Add("ctrl");
                if (b.HasFlag(ModifierBits.Alt)) Add("alt");
            }
            if (b.HasFlag(ModifierBits.Kana)) Add("kana");
            if (b.HasFlag(ModifierBits.LayerA)) Add("layer-a");
            if (b.HasFlag(ModifierBits.LayerB)) Add("layer-b");
            if (b.HasFlag(ModifierBits.LayerC)) Add("layer-c");
            if (b.HasFlag(ModifierBits.GrpSelTap)) Add("grpseltap");
            return sb.ToString();
        }
    }

    /// <summary>A label for people, e.g. "Shift + AltGr".</summary>
    public string DisplayName =>
        Bits == ModifierBits.None ? "Base" :
        string.Join(" + ", Name.Split('+').Select(p => p switch
        {
            "altgr" => "AltGr",
            "ctrl" => "Ctrl",
            "layer-a" => "Layer A",
            "layer-b" => "Layer B",
            "layer-c" => "Layer C",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(p),
        }));

    public override string ToString() => Name;

    public int CompareTo(ShiftState other) => Value.CompareTo(other.Value);

    public static ShiftState Parse(string text)
    {
        if (TryParse(text, out var s)) return s;
        throw new FormatException($"Unknown shift state '{text}'.");
    }

    public static bool TryParse(string? text, out ShiftState state)
    {
        state = Base;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var bits = ModifierBits.None;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "base": break;
                case "shift": bits |= ModifierBits.Shift; break;
                case "ctrl": case "control": bits |= ModifierBits.Ctrl; break;
                case "alt": bits |= ModifierBits.Alt; break;
                case "altgr": bits |= ModifierBits.AltGr; break;
                case "kana": bits |= ModifierBits.Kana; break;
                case "roya" or "layer-a" or "layera": bits |= ModifierBits.LayerA; break;
                case "loya" or "layer-b" or "layerb": bits |= ModifierBits.LayerB; break;
                case "layer-c" or "layerc": bits |= ModifierBits.LayerC; break;
                case "grpseltap": bits |= ModifierBits.GrpSelTap; break;
                case "0x40": bits |= ModifierBits.LayerC; break;
                default: return false;
            }
        }
        state = new ShiftState(bits);
        return true;
    }

    /// <summary>Reserved words in the per-key JSON objects; never valid shift-state names.</summary>
    internal static bool IsName(string text) => TryParse(text, out _);
}
