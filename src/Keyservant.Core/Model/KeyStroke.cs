using System.Text;

namespace Keyservant.Core.Model;

/// <summary>One keystroke: a virtual key with modifiers, optionally with Caps Lock on.</summary>
public readonly record struct KeyStroke(byte VirtualKey, ShiftState State, bool CapsLock = false);

/// <summary>
/// Parses and formats keystroke sequences such as <c>AltGr+. t</c> or <c>Shift+AltGr+A</c>.
/// </summary>
/// <remarks>
/// Strokes are separated by spaces. Each stroke is optional modifiers (<c>Shift</c>, <c>Ctrl</c>, <c>Alt</c>,
/// <c>AltGr</c>, <c>Caps</c>) joined with <c>+</c>, then a key. A key is either a single character, meaning the
/// key that types it without modifiers in this layout, or a virtual key name such as <c>OEM_PERIOD</c> or
/// <c>SPACE</c>. Use <c>PLUS</c> for the + key in the middle of a stroke.
/// </remarks>
public static class KeySequence
{
    public static IReadOnlyList<KeyStroke> Parse(string text, KeyboardLayout layout)
    {
        var strokes = new List<KeyStroke>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            strokes.Add(ParseStroke(token, layout));
        }
        if (strokes.Count == 0) throw new FormatException("The key sequence is empty.");
        return strokes;
    }

    private static KeyStroke ParseStroke(string token, KeyboardLayout layout)
    {
        // A lone "+" or a trailing "++" means the + key itself.
        string keyPart;
        string[] modifiers;
        if (token == "+") { keyPart = "+"; modifiers = []; }
        else if (token.EndsWith("++", StringComparison.Ordinal)) { keyPart = "+"; modifiers = token[..^2].Split('+', StringSplitOptions.RemoveEmptyEntries); }
        else
        {
            var parts = token.Split('+');
            keyPart = parts[^1];
            modifiers = parts[..^1];
            if (keyPart.Length == 0) throw new FormatException($"'{token}' has no key.");
        }

        var bits = ModifierBits.None;
        var caps = false;
        foreach (var m in modifiers)
        {
            switch (m.ToLowerInvariant())
            {
                case "shift": bits |= ModifierBits.Shift; break;
                case "ctrl" or "control": bits |= ModifierBits.Ctrl; break;
                case "alt": bits |= ModifierBits.Alt; break;
                case "altgr": bits |= ModifierBits.AltGr; break;
                case "kana": bits |= ModifierBits.Kana; break;
                case "caps" or "capslock": caps = true; break;
                default: throw new FormatException($"Unknown modifier '{m}' in '{token}'.");
            }
        }

        var vk = ResolveKey(keyPart, layout, ref bits, token);
        return new KeyStroke(vk, new ShiftState(bits), caps);
    }

    private static byte ResolveKey(string keyPart, KeyboardLayout layout, ref ModifierBits bits, string token)
    {
        if (keyPart.Equals("PLUS", StringComparison.OrdinalIgnoreCase)) keyPart = "+";
        if (keyPart.Equals("Compose", StringComparison.OrdinalIgnoreCase) && layout.Compose is { } compose)
        {
            foreach (var key in layout.Keys)
            {
                if (key.Outputs.TryGetValue(ShiftState.Base, out var o) && o.IsDead && o.Accent == compose.Accent) return key.VirtualKey;
            }
            throw new FormatException("The compose key is not on the keyboard; check the compose settings.");
        }
        var isSingle = keyPart.EnumerateRunes().Count() == 1;
        if (isSingle)
        {
            foreach (var key in layout.Keys)
            {
                if (key.Outputs.TryGetValue(ShiftState.Base, out var o) && !o.IsDead && o.Text == keyPart) return key.VirtualKey;
            }
            foreach (var key in layout.Keys)
            {
                if (key.Outputs.TryGetValue(ShiftState.Base, out var o) && o.IsDead && o.Text == keyPart) return key.VirtualKey;
            }
            // A character that is only on Shift implies Shift ("A" means Shift+A).
            foreach (var key in layout.Keys)
            {
                if (key.Outputs.TryGetValue(ShiftState.Shift, out var o) && o.Text == keyPart)
                {
                    bits |= ModifierBits.Shift;
                    return key.VirtualKey;
                }
            }
        }
        if (VirtualKeys.TryParse(keyPart, out var vk)) return vk;
        throw new FormatException($"No key types '{keyPart}' in this layout (in '{token}'). Use a virtual key name such as OEM_PERIOD.");
    }

    /// <summary>Formats strokes in the same notation <see cref="Parse"/> accepts.</summary>
    public static string Format(IEnumerable<KeyStroke> strokes, KeyboardLayout layout)
    {
        var sb = new StringBuilder();
        foreach (var s in strokes)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (s.CapsLock) sb.Append("Caps+");
            if (s.State.Bits != ModifierBits.None) sb.Append(s.State.DisplayName.Replace(" + ", "+", StringComparison.Ordinal)).Append('+');
            var key = layout.FindKey(s.VirtualKey);
            var label = key?.Outputs.GetValueOrDefault(ShiftState.Base) is { } o && o.Text.Length == 1 && !char.IsWhiteSpace(o.Text[0]) && !char.IsControl(o.Text[0]) && o.Text != "+"
                ? o.Text
                : VirtualKeys.GetName(s.VirtualKey);
            sb.Append(label);
        }
        return sb.ToString();
    }
}

/// <summary>
/// An assertion stored with a layout: typing <see cref="Keys"/> must produce <see cref="Expect"/>.
/// Checked by the validator, the CLI (<c>keyservant test</c>) and the editor.
/// </summary>
public sealed record LayoutTestCase(string Keys, string Expect, string? Name = null);

/// <summary>Result of one <see cref="LayoutTestCase"/>.</summary>
public sealed record LayoutTestResult(LayoutTestCase Test, bool Passed, string? Actual, string? Error)
{
    public string Message => Error ?? (Passed ? "passed" : $"typed \"{Actual}\", expected \"{Test.Expect}\"");
}

public static class LayoutTestRunner
{
    /// <summary>Runs every test of the layout with the typing simulator (compose sequences included).</summary>
    public static IReadOnlyList<LayoutTestResult> Run(KeyboardLayout layout)
    {
        if (layout.Tests.Count == 0) return [];
        var effective = Compose.ComposeCompiler.Compile(layout).Layout;
        return [.. layout.Tests.Select(t => Run(effective, t))];
    }

    public static LayoutTestResult Run(KeyboardLayout effectiveLayout, LayoutTestCase test)
    {
        try
        {
            var strokes = KeySequence.Parse(test.Keys, effectiveLayout);
            var sim = new TypingSimulator(effectiveLayout);
            var sb = new StringBuilder();
            foreach (var s in strokes) sb.Append(sim.Press(s.VirtualKey, s.State, s.CapsLock));
            var actual = sb.ToString();
            return new LayoutTestResult(test, actual == test.Expect, actual, null);
        }
        catch (FormatException ex)
        {
            return new LayoutTestResult(test, false, null, ex.Message);
        }
    }
}
