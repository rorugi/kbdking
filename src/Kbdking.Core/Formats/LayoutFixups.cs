using Kbdking.Core.Model;

namespace Kbdking.Core.Formats;

/// <summary>Adjustments for layouts imported from formats that only know physical key positions.</summary>
internal static class LayoutFixups
{
    /// <summary>
    /// Gives letter keys the virtual key of the letter they type (as Windows and MSKLC do), e.g. the key
    /// typing "z" on a German keyboard becomes VK_Z even though it sits where US keyboards have Y.
    /// Apps use virtual keys for shortcuts, so Ctrl+Z must follow the letter.
    /// </summary>
    public static void AssignLetterVirtualKeys(KeyboardLayout layout)
    {
        var moves = new List<(KeyDefinition Key, byte Target)>();
        foreach (var key in layout.Keys)
        {
            if (key.Outputs.GetValueOrDefault(ShiftState.Base) is not { IsDead: false, Text.Length: 1 } o) continue;
            var c = char.ToUpperInvariant(o.Text[0]);
            if (c is < 'A' or > 'Z' || key.VirtualKey == c) continue;
            moves.Add((key, (byte)c));
        }
        if (moves.Count == 0) return;

        // Swap virtual keys pairwise (definitions and scan codes) so no key is lost.
        foreach (var (key, target) in moves)
        {
            var from = key.VirtualKey;
            if (from == target) continue;
            var other = layout.FindKey(target);
            key.VirtualKey = target;
            if (other is not null && other != key) other.VirtualKey = from;
            for (var i = 0; i < layout.ScanCodes.Normal.Count; i++)
            {
                var t = layout.ScanCodes.Normal[i];
                if (t.VirtualKey == from) layout.ScanCodes.Normal[i] = t with { VirtualKey = target };
                else if (t.VirtualKey == target) layout.ScanCodes.Normal[i] = t with { VirtualKey = from };
            }
        }
    }
}
