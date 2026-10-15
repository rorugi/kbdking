using Keyservant.Core.Model;
using Keyservant.Core.Templates;

namespace Keyservant.Core.Editing;

/// <summary>A physical key that switches to an extra layer while held, like Shift does.</summary>
public sealed record LayerKey(int ScanCode, bool Extended, byte VirtualKey, ModifierBits Layer)
{
    public string Code => Extended ? $"E0 {ScanCode:X2}" : $"{ScanCode:X2}";
    public string LayerName => new ShiftState(Layer).DisplayName;
}

/// <summary>
/// Extra layers with layer keys of your own choice (e.g. Caps Lock or the &lt;&gt; key), built into the layout
/// itself: the key gets a free virtual key, and the layout's modifier table maps that virtual key to one of the
/// spare kbd.h modifier bits (Layer A/B/C). Holding it selects the layer's character column, exactly as
/// Shift does; Windows' own Neo-style layouts use the same mechanism. Layers combine with Shift and each other.
/// </summary>
public static class LayerKeys
{
    /// <summary>The spare modifier bits that can be used for layers.</summary>
    public static IReadOnlyList<ModifierBits> Layers { get; } = [ModifierBits.LayerA, ModifierBits.LayerB, ModifierBits.LayerC];

    /// <summary>Typical places for a layer key.</summary>
    public static IReadOnlyList<(string Name, int ScanCode, bool Extended)> CommonKeys { get; } =
    [
        ("Caps Lock", 0x3A, false),
        ("< > key (ISO, left of Y/Z)", 0x56, false),
        ("# key (ISO, left of Enter)", 0x2B, false),
        ("Right Alt", 0x38, true),
        ("Menu key", 0x5D, true),
        ("Right Windows key", 0x5C, true),
        ("Right Ctrl", 0x1D, true),
        ("Tab", 0x0F, false),
    ];

    // Virtual keys Windows leaves unassigned; layer keys get one of these so apps don't react to them.
    private static readonly byte[] Candidates = [0x97, 0x98, 0x99, 0x9A, 0x9B, 0x9C, 0x9D, 0x9E, 0x9F, 0xE8];

    /// <summary>All layer keys of the layout.</summary>
    public static IReadOnlyList<LayerKey> Find(KeyboardLayout layout)
    {
        var result = new List<LayerKey>();
        foreach (var bit in layout.Modifiers.VkToBits)
        {
            var layer = bit.Bits & (ModifierBits.LayerA | ModifierBits.LayerB | ModifierBits.LayerC);
            if (layer == ModifierBits.None) continue;
            for (var s = 1; s < layout.ScanCodes.Normal.Count; s++)
                if (layout.ScanCodes.Normal[s].VirtualKey == bit.VirtualKey) result.Add(new LayerKey(s, false, bit.VirtualKey, layer));
            foreach (var (s, t) in layout.ScanCodes.E0)
                if (t.VirtualKey == bit.VirtualKey) result.Add(new LayerKey(s, true, bit.VirtualKey, layer));
        }
        return result;
    }

    /// <summary>
    /// Makes the physical key a layer key and adds the layer's columns (the layer alone and with Shift).
    /// Returns a note about what the key did before, or null.
    /// </summary>
    public static string? Add(KeyboardLayout layout, int scanCode, bool extended, ModifierBits layer, bool withShift = true)
    {
        if (!Layers.Contains(layer)) throw new ArgumentException("Use Layer A, B or C.", nameof(layer));
        if (scanCode is < 1 or > 0x7F) throw new ArgumentOutOfRangeException(nameof(scanCode));
        if (extended && scanCode == 0x38 && layout.Flags.HasFlag(LocaleFlags.AltGr) && layout.Modifiers.Columns.Any(c => c.Bits.HasFlag(ModifierBits.AltGr) && layout.Keys.Any(k => k.Outputs.ContainsKey(c))))
            throw new InvalidOperationException("Right Alt is the AltGr key of this layout. Remove the AltGr characters or pick another key.");

        var previous = extended ? layout.ScanCodes.E0.GetValueOrDefault((byte)scanCode, ScanCodeTarget.None) : layout.ScanCodes[scanCode];
        if (Find(layout).FirstOrDefault(k => k.ScanCode == scanCode && k.Extended == extended) is { } existing)
        {
            // Already a layer key: just switch it to the new layer.
            layout.Modifiers.VkToBits.RemoveAll(b => b.VirtualKey == existing.VirtualKey);
            layout.Modifiers.VkToBits.Add(new VkModifierBit(existing.VirtualKey, layer));
            layout.Modifiers.EnsureColumn(new ShiftState(layer));
            if (withShift) layout.Modifiers.EnsureColumn(new ShiftState(layer | ModifierBits.Shift));
            return null;
        }
        var used = layout.ScanCodes.Normal.Select(t => t.VirtualKey).Concat(layout.ScanCodes.E0.Values.Select(t => t.VirtualKey))
            .Concat(layout.Keys.Select(k => k.VirtualKey)).Concat(layout.Modifiers.VkToBits.Select(b => b.VirtualKey)).ToHashSet();
        var vk = Candidates.FirstOrDefault(c => !used.Contains(c));
        if (vk == 0) throw new InvalidOperationException("No free virtual key is left for another layer key.");

        var target = new ScanCodeTarget(vk, extended ? ScanCodeFlags.Ext : ScanCodeFlags.None);
        if (extended) layout.ScanCodes.E0[(byte)scanCode] = target;
        else layout.ScanCodes.Set(scanCode, target);
        layout.Modifiers.VkToBits.Add(new VkModifierBit(vk, layer));
        layout.Modifiers.EnsureColumn(new ShiftState(layer));
        if (withShift) layout.Modifiers.EnsureColumn(new ShiftState(layer | ModifierBits.Shift));
        if (extended && scanCode == 0x38) layout.Flags &= ~LocaleFlags.AltGr;

        if (previous.IsNone) return null;
        var name = VirtualKeys.GetName(previous.VirtualKey);
        return layout.FindKey(previous.VirtualKey) is { IsEmpty: false }
            ? $"The key typed characters before (virtual key {name}); they are no longer reachable from it."
            : $"The key was {name} before.";
    }

    /// <summary>
    /// Turns a layer key back into a normal key. The layer's characters stay. The key gets back the virtual key
    /// whose characters became unreachable when it was made a layer key, or else what it is on a standard keyboard.
    /// </summary>
    public static void Remove(KeyboardLayout layout, LayerKey key)
    {
        layout.Modifiers.VkToBits.RemoveAll(b => b.VirtualKey == key.VirtualKey);
        var sent = layout.ScanCodes.Normal.Select(t => t.VirtualKey).Concat(layout.ScanCodes.E0.Values.Select(t => t.VirtualKey)).ToHashSet();
        var templateId = LayoutTemplates.All.FirstOrDefault(t => t.Id.Equals(layout.Metadata.Locale, StringComparison.OrdinalIgnoreCase))?.Id
            ?? (layout.Metadata.Locale.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "de-DE" : "en-US");
        var standard = LayoutTemplates.Create(templateId).ScanCodes;
        var original = key.Extended ? standard.E0.GetValueOrDefault((byte)key.ScanCode, ScanCodeTarget.None) : standard[key.ScanCode];

        // Character keys that no physical key sends any more: one of them is what this key typed before.
        var orphans = layout.Keys.Where(k => !k.IsEmpty && !sent.Contains(k.VirtualKey) && !VirtualKeys.IsImplicitControlKey(k.VirtualKey)
            && k.VirtualKey != VirtualKeys.Decimal).Select(k => k.VirtualKey).ToList();
        byte? restore = orphans.Contains(original.VirtualKey) ? original.VirtualKey
            : !original.IsNone && !sent.Contains(original.VirtualKey) ? null
            : orphans.Count > 0 ? orphans[0] : null;
        if (restore is { } vk && vk != original.VirtualKey)
            original = new ScanCodeTarget(vk, key.Extended ? ScanCodeFlags.Ext : ScanCodeFlags.None);
        if (key.Extended)
        {
            if (original.IsNone) layout.ScanCodes.E0.Remove((byte)key.ScanCode);
            else layout.ScanCodes.E0[(byte)key.ScanCode] = original;
        }
        else
        {
            layout.ScanCodes.Set(key.ScanCode, original);
        }
    }

    /// <summary>The layer bits held when these scan codes are down (for previews and tests).</summary>
    public static ModifierBits HeldLayers(KeyboardLayout layout, IEnumerable<(int ScanCode, bool Extended)> down)
    {
        var bits = ModifierBits.None;
        foreach (var (sc, ext) in down)
        {
            var t = ext ? layout.ScanCodes.E0.GetValueOrDefault((byte)sc, ScanCodeTarget.None) : layout.ScanCodes[sc];
            foreach (var b in layout.Modifiers.VkToBits)
                if (b.VirtualKey == t.VirtualKey) bits |= b.Bits & (ModifierBits.LayerA | ModifierBits.LayerB | ModifierBits.LayerC | ModifierBits.Kana);
        }
        return bits;
    }
}
