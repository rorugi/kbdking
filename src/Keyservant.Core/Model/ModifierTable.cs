namespace Keyservant.Core.Model;

/// <summary>A virtual key that acts as a modifier and the bit it sets (kbd.h: VK_TO_BIT).</summary>
public readonly record struct VkModifierBit(byte VirtualKey, ModifierBits Bits);

/// <summary>
/// Which keys are modifiers and how each modifier combination maps to a character-table column
/// (kbd.h: MODIFIERS). Keeps the exact Windows mapping so imported layouts round-trip unchanged.
/// </summary>
public sealed class ModifierTable
{
    public const byte Invalid = 0x0F; // SHFT_INVALID

    public List<VkModifierBit> VkToBits { get; } = [];

    /// <summary>Index = modifier bit combination, value = column number or <see cref="Invalid"/>.</summary>
    public List<byte> ModNumber { get; } = [];

    public int ColumnCount => ModNumber.Where(m => m != Invalid).Select(m => m + 1).DefaultIfEmpty(0).Max();

    /// <summary>The standard table: Shift, Ctrl, Alt with columns base, shift, ctrl, altgr, shift+altgr.</summary>
    public static ModifierTable CreateStandard(bool withAltGr = true, bool withShiftCtrl = false)
    {
        var t = new ModifierTable();
        t.VkToBits.Add(new(VirtualKeys.Shift, ModifierBits.Shift));
        t.VkToBits.Add(new(VirtualKeys.Control, ModifierBits.Ctrl));
        t.VkToBits.Add(new(VirtualKeys.Menu, ModifierBits.Alt));
        t.EnsureColumn(ShiftState.Base);
        t.EnsureColumn(ShiftState.Shift);
        t.EnsureColumn(ShiftState.Ctrl);
        if (withShiftCtrl) t.EnsureColumn(ShiftState.ShiftCtrl);
        if (withAltGr)
        {
            t.EnsureColumn(ShiftState.AltGr);
            t.EnsureColumn(ShiftState.ShiftAltGr);
        }
        return t;
    }

    public int? ColumnOf(ShiftState state)
    {
        var i = state.Value;
        if (i >= ModNumber.Count) return null;
        var c = ModNumber[i];
        return c == Invalid ? null : c;
    }

    /// <summary>All modifier combinations that select the given column, lowest first.</summary>
    public IReadOnlyList<ShiftState> StatesOf(int column) =>
        [.. Enumerable.Range(0, ModNumber.Count).Where(i => ModNumber[i] == column).Select(i => new ShiftState((ModifierBits)i))];

    /// <summary>The canonical (lowest) modifier combination for a column; key outputs are stored under it.</summary>
    public ShiftState CanonicalState(int column)
    {
        var states = StatesOf(column);
        return states.Count > 0 ? states[0] : throw new ArgumentOutOfRangeException(nameof(column), column, "Column is not used.");
    }

    /// <summary>The canonical states in column order.</summary>
    public IReadOnlyList<ShiftState> Columns => [.. Enumerable.Range(0, ColumnCount).Select(CanonicalState)];

    /// <summary>Maps a state to the canonical state of its column, or null if the state produces nothing.</summary>
    public ShiftState? Canonicalize(ShiftState state) => ColumnOf(state) is { } c ? CanonicalState(c) : null;

    /// <summary>Makes sure the state has a column, adding one at the end if needed. Returns the column.</summary>
    public int EnsureColumn(ShiftState state)
    {
        if (ColumnOf(state) is { } existing) return existing;
        var column = ColumnCount;
        while (ModNumber.Count <= state.Value) ModNumber.Add(Invalid);
        ModNumber[state.Value] = (byte)column;
        EnsureModifierKeys(state.Bits);
        return column;
    }

    /// <summary>
    /// Removes a column; later columns move down by one. Callers must also drop key outputs stored under the
    /// column's state (see <see cref="KeyboardLayout.RemoveShiftState"/>).
    /// </summary>
    public void RemoveColumn(int column)
    {
        for (var i = 0; i < ModNumber.Count; i++)
        {
            if (ModNumber[i] == Invalid) continue;
            if (ModNumber[i] == column) ModNumber[i] = Invalid;
            else if (ModNumber[i] > column) ModNumber[i]--;
        }
        while (ModNumber.Count > 1 && ModNumber[^1] == Invalid) ModNumber.RemoveAt(ModNumber.Count - 1);
    }

    private void EnsureModifierKeys(ModifierBits bits)
    {
        void Need(ModifierBits bit, byte vk)
        {
            if (bits.HasFlag(bit) && !VkToBits.Any(v => v.Bits.HasFlag(bit))) VkToBits.Add(new(vk, bit));
        }
        Need(ModifierBits.Shift, VirtualKeys.Shift);
        Need(ModifierBits.Ctrl, VirtualKeys.Control);
        Need(ModifierBits.Alt, VirtualKeys.Menu);
        Need(ModifierBits.Kana, VirtualKeys.Kana);
    }

    public ModifierTable Clone()
    {
        var t = new ModifierTable();
        t.VkToBits.AddRange(VkToBits);
        t.ModNumber.AddRange(ModNumber);
        return t;
    }
}
