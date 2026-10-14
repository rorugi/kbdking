namespace Keyservant.Core.Model;

/// <summary>Attributes of a key in the character tables (kbd.h: CAPLOK, CAPLOKALTGR, KANALOK, GRPSELTAP).</summary>
[Flags]
public enum KeyAttributes : byte
{
    None = 0,
    /// <summary>Caps Lock acts like Shift for this key.</summary>
    CapsLock = 0x01,
    /// <summary>Set automatically when <see cref="KeyDefinition.CapsOutputs"/> is present (kbd.h: SGCAPS).</summary>
    SgCaps = 0x02,
    /// <summary>Caps Lock also acts like Shift in the AltGr states.</summary>
    CapsLockAltGr = 0x04,
    KanaLock = 0x08,
    GrpSelTap = 0x80,
}

/// <summary>The characters one virtual key produces in each shift state.</summary>
public sealed class KeyDefinition
{
    public KeyDefinition(byte virtualKey) => VirtualKey = virtualKey;

    public byte VirtualKey { get; set; }

    /// <summary>Attributes except <see cref="KeyAttributes.SgCaps"/>, which is derived from <see cref="CapsOutputs"/>.</summary>
    public KeyAttributes Attributes { get; set; }

    /// <summary>Output per shift state. Shift states without an entry produce nothing.</summary>
    public SortedDictionary<ShiftState, KeyOutput> Outputs { get; } = new();

    /// <summary>
    /// Optional separate outputs while Caps Lock is on (kbd.h: SGCAPS), as used by Swiss and Czech layouts.
    /// Null when Caps Lock only acts through <see cref="KeyAttributes.CapsLock"/>.
    /// </summary>
    public SortedDictionary<ShiftState, KeyOutput>? CapsOutputs { get; set; }

    public string Name => VirtualKeys.GetName(VirtualKey);

    public KeyOutput? this[ShiftState state]
    {
        get => Outputs.GetValueOrDefault(state);
        set
        {
            if (value is null) Outputs.Remove(state);
            else Outputs[state] = value;
        }
    }

    public bool IsEmpty => Outputs.Count == 0 && (CapsOutputs is null || CapsOutputs.Count == 0);

    public KeyDefinition Clone()
    {
        var k = new KeyDefinition(VirtualKey) { Attributes = Attributes };
        foreach (var (s, o) in Outputs) k.Outputs[s] = o;
        if (CapsOutputs is not null) k.CapsOutputs = new SortedDictionary<ShiftState, KeyOutput>(CapsOutputs);
        return k;
    }

    public override string ToString() => $"{Name}: " + string.Join(", ", Outputs.Select(p => $"{p.Key}={p.Value}"));
}
