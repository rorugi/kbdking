using Kbdking.Core.Model;
using Kbdking.Core.Templates;

namespace Kbdking.Core.Editing;

/// <summary>Where Ctrl+C, Ctrl+V, Ctrl+Z and other letter shortcuts are on the keyboard.</summary>
public enum ShortcutMode
{
    /// <summary>Shortcuts follow the letters: Ctrl+C is wherever the layout types "c" (Windows' default).</summary>
    FollowLetters,
    /// <summary>Shortcuts stay where they are on a US keyboard, whatever the keys type.</summary>
    Qwerty,
    /// <summary>Shortcuts stay where they are on a German keyboard (Z and Y swapped compared with US).</summary>
    Qwertz,
    /// <summary>Shortcuts stay where they are on a French keyboard.</summary>
    Azerty,
}

/// <summary>
/// Moves letter shortcuts independently of the characters. Apps recognise Ctrl+C by the virtual key (VK_C),
/// and the layout decides which physical key sends VK_C. Giving the physical keys the virtual keys of a
/// QWERTY keyboard, while each key keeps typing the same characters, keeps Ctrl+C/V/X/Z under the same
/// fingers on Colemak, Dvorak, Neo, Bone or any custom arrangement.
/// </summary>
public static class ShortcutPositions
{
    private static readonly string[] Rows = ["QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM"];
    private static readonly int[] RowStarts = [0x10, 0x1E, 0x2C];

    public static string DisplayName(ShortcutMode mode) => mode switch
    {
        ShortcutMode.FollowLetters => "Follow the letters (Windows default)",
        ShortcutMode.Qwerty => "Stay at QWERTY positions (US)",
        ShortcutMode.Qwertz => "Stay at QWERTZ positions (German)",
        ShortcutMode.Azerty => "Stay at AZERTY positions (French)",
        _ => mode.ToString(),
    };

    /// <summary>Scan code → letter for a fixed keyboard (null for <see cref="ShortcutMode.FollowLetters"/>).</summary>
    public static IReadOnlyDictionary<int, char>? FixedPositions(ShortcutMode mode)
    {
        if (mode == ShortcutMode.FollowLetters) return null;
        var map = new Dictionary<int, char>();
        for (var r = 0; r < Rows.Length; r++)
            for (var i = 0; i < Rows[r].Length; i++) map[RowStarts[r] + i] = Rows[r][i];
        if (mode == ShortcutMode.Qwertz)
        {
            map[0x15] = 'Z';
            map[0x2C] = 'Y';
        }
        else if (mode == ShortcutMode.Azerty)
        {
            map[0x10] = 'A';
            map[0x11] = 'Z';
            map[0x1E] = 'Q';
            map[0x2C] = 'W';
            map.Remove(0x32); // AZERTY has "," where QWERTY has M
            map[0x27] = 'M';
        }
        return map;
    }

    /// <summary>The modes the layout currently satisfies (several when they coincide, e.g. QWERTZ on a German layout).</summary>
    public static IReadOnlyList<ShortcutMode> Detect(KeyboardLayout layout)
    {
        var result = new List<ShortcutMode>();
        foreach (var mode in Enum.GetValues<ShortcutMode>())
        {
            var desired = Desired(layout, mode);
            if (desired.Count > 0 && desired.All(p => layout.ScanCodes[p.Key].VirtualKey == p.Value)) result.Add(mode);
        }
        return result;
    }

    /// <summary>
    /// Reassigns virtual keys so letter shortcuts follow <paramref name="mode"/>. Every physical key keeps typing
    /// exactly what it typed before; only the virtual keys (and the Ctrl control characters that belong to them)
    /// change. Returns the number of physical keys that got a new virtual key.
    /// </summary>
    public static int Apply(KeyboardLayout layout, ShortcutMode mode)
    {
        var desired = Desired(layout, mode);
        var scan = layout.ScanCodes;
        var current = new Dictionary<int, byte>();
        for (var s = 1; s < scan.Normal.Count; s++) current[s] = scan.Normal[s].VirtualKey;

        // Physical keys that keep or get a letter, plus keys holding a letter they must give up. The virtual keys
        // left over on these keys once the letters are placed go to the keys that gave up a letter.
        var newVk = desired.ToDictionary(p => p.Key, p => p.Value);
        var gaveUp = current.Where(p => IsLetter(p.Value) && !desired.ContainsKey(p.Key)).Select(p => p.Key).ToList();
        var freed = desired.Keys.Concat(gaveUp).Select(s => current.GetValueOrDefault(s, ScanCodeTarget.NoKey)).ToList();
        var missing = new List<char>();
        foreach (var letter in desired.Values)
        {
            if (!freed.Remove(letter)) missing.Add((char)letter);
        }
        if (missing.Count > 0)
            throw new InvalidOperationException($"No key sends {string.Join(", ", missing)}; the shortcuts can't be arranged like that.");

        var usual = LayoutTemplates.Create("en-US").ScanCodes;
        foreach (var s in gaveUp.OrderBy(s => s))
        {
            var preferred = usual[s].VirtualKey;
            var vk = freed.Contains(preferred) ? preferred : freed.FirstOrDefault(v => !IsLetter(v), freed[0]);
            freed.Remove(vk);
            newVk[s] = vk;
        }

        // Each physical key takes its character definition along to its new virtual key.
        var definitions = newVk.Keys.ToDictionary(s => s, s => layout.FindKey(current[s]));
        var changed = 0;
        var hasCtrl = layout.Modifiers.ColumnOf(ShiftState.Ctrl) is not null;
        foreach (var (s, vk) in newVk)
        {
            if (current[s] == vk) continue;
            changed++;
            scan.Normal[s] = scan.Normal[s] with { VirtualKey = vk };
            if (definitions[s] is not { } key) continue;
            key.VirtualKey = vk;
            // Ctrl+letter types a control character tied to the virtual key (Ctrl+C = U+0003 in consoles).
            if (IsLetter(vk))
            {
                if (hasCtrl) key.Outputs[ShiftState.Ctrl] = KeyOutput.Of(((char)(vk - 0x40)).ToString());
            }
            else if (key.Outputs.TryGetValue(ShiftState.Ctrl, out var ctrl) && ctrl.Text is [>= '\u0001' and <= '\u001A'])
            {
                key.Outputs.Remove(ShiftState.Ctrl);
            }
        }
        return changed;
    }

    private static Dictionary<int, byte> Desired(KeyboardLayout layout, ShortcutMode mode)
    {
        if (FixedPositions(mode) is { } positions)
            return positions.ToDictionary(p => p.Key, p => (byte)p.Value);

        // Follow the letters: every physical key that types a plain letter gets that letter's virtual key.
        var result = new Dictionary<int, byte>();
        for (var s = 1; s < layout.ScanCodes.Normal.Count; s++)
        {
            var vk = layout.ScanCodes.Normal[s].VirtualKey;
            if (layout.FindKey(vk)?.Outputs.GetValueOrDefault(ShiftState.Base) is not { IsDead: false, Text.Length: 1 } o) continue;
            var c = char.ToUpperInvariant(o.Text[0]);
            if (c is >= 'A' and <= 'Z' && !result.ContainsValue((byte)c)) result[s] = (byte)c;
        }
        return result;
    }

    private static bool IsLetter(byte vk) => vk is >= (byte)'A' and <= (byte)'Z';
}
