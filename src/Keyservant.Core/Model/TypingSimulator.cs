namespace Keyservant.Core.Model;

/// <summary>
/// Simulates what Windows types for a sequence of keystrokes with a layout, including Caps Lock,
/// SGCAPS, ligatures and (chained) dead keys. Used by the editor's live preview, so layouts can be
/// tried before they are installed, and by tests.
/// </summary>
public sealed class TypingSimulator(KeyboardLayout layout)
{
    /// <summary>The accent of the dead key waiting for the next keystroke, if any.</summary>
    public char? PendingDeadKey { get; private set; }

    public void Reset() => PendingDeadKey = null;

    /// <summary>Presses a key identified by scan code (as a physical keyboard reports it).</summary>
    public string PressScanCode(int scanCode, bool extended, ShiftState state, bool capsLock = false)
    {
        var target = extended
            ? layout.ScanCodes.E0.GetValueOrDefault((byte)scanCode, ScanCodeTarget.None)
            : layout.ScanCodes[scanCode];
        if (target.IsNone) return "";
        var vk = target.VirtualKey;
        // The numpad's navigation keys type digits/decimal when Num Lock is on; the preview assumes it is.
        if (!extended && target.Flags.HasFlag(ScanCodeFlags.Numpad))
        {
            vk = scanCode switch
            {
                0x47 => 0x67, 0x48 => 0x68, 0x49 => 0x69,
                0x4B => 0x64, 0x4C => 0x65, 0x4D => 0x66,
                0x4F => 0x61, 0x50 => 0x62, 0x51 => 0x63,
                0x52 => 0x60, 0x53 => VirtualKeys.Decimal,
                _ => vk,
            };
        }
        return Press(vk, state, capsLock);
    }

    /// <summary>Presses a virtual key with the given modifiers and returns the text produced (may be empty).</summary>
    public string Press(byte vk, ShiftState state, bool capsLock = false)
    {
        var output = Resolve(vk, state, capsLock);
        if (output is null) return "";

        if (PendingDeadKey is not { } dead)
        {
            if (output.IsDead)
            {
                PendingDeadKey = output.Accent;
                return "";
            }
            return output.Text;
        }

        PendingDeadKey = null;
        var definition = layout.FindDeadKey(dead);
        if (!output.IsLigature && definition?.Find(output.Text[0]) is { } entry)
        {
            if (entry.Chained)
            {
                PendingDeadKey = entry.Result;
                return "";
            }
            return entry.Result.ToString();
        }
        // No composition: Windows types the accent followed by the character.
        return dead + output.Text;
    }

    private KeyOutput? Resolve(byte vk, ShiftState state, bool capsLock)
    {
        var key = layout.FindKey(vk);
        if (key is null) return null;
        var canonical = layout.Modifiers.Canonicalize(state);
        if (canonical is null) return null;
        var s = canonical.Value;

        if (capsLock)
        {
            var isAltGr = s.Bits.HasFlag(ModifierBits.AltGr);
            var plainOrShift = (s.Bits & ~ModifierBits.Shift) == ModifierBits.None;
            if (key.CapsOutputs is { } caps && plainOrShift)
                return caps.GetValueOrDefault(s);
            if (plainOrShift && key.Attributes.HasFlag(KeyAttributes.CapsLock)
                || isAltGr && (s.Bits & ~(ModifierBits.AltGr | ModifierBits.Shift)) == 0 && key.Attributes.HasFlag(KeyAttributes.CapsLockAltGr))
            {
                var toggled = layout.Modifiers.Canonicalize(new ShiftState(s.Bits ^ ModifierBits.Shift));
                if (toggled is { } t) s = t;
            }
        }
        return key.Outputs.GetValueOrDefault(s);
    }
}
