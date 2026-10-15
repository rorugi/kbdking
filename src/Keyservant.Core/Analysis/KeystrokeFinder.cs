using Keyservant.Core.Model;

namespace Keyservant.Core.Analysis;

/// <summary>A way to type some text: the strokes and their estimated effort.</summary>
public sealed record TypingRecipe(IReadOnlyList<KeyStroke> Strokes, double Effort);

/// <summary>
/// Finds the cheapest keystroke sequence that types a given text with a layout, including dead keys and
/// chained dead keys (breadth-first over dead-key states, at most six strokes). Only keys and modifier
/// combinations a physical key can actually produce are used.
/// </summary>
public sealed class KeystrokeFinder
{
    private const int MaxStrokes = 6;

    private readonly KeyboardLayout _layout;
    private readonly Dictionary<string, TypingRecipe> _best = new(StringComparer.Ordinal);

    public KeystrokeFinder(KeyboardLayout effectiveLayout, EffortModel? effort = null)
    {
        _layout = effectiveLayout;
        Effort = effort ?? EffortModel.Default;
        Explore();
    }

    public EffortModel Effort { get; }

    /// <summary>Every text the layout can type in at most six strokes.</summary>
    public IReadOnlyDictionary<string, TypingRecipe> All => _best;

    public TypingRecipe? Find(string text) => _best.GetValueOrDefault(text);

    public bool CanType(string text) => _best.ContainsKey(text);

    private void Explore()
    {
        // All (stroke, output) pairs, cheapest strokes first.
        var moves = new List<(KeyStroke Stroke, KeyOutput Output, double Cost)>();
        var sent = _layout.ScanCodes.Normal.Select(t => t.VirtualKey).Concat(_layout.ScanCodes.E0.Values.Select(t => t.VirtualKey)).ToHashSet();
        var reachableBits = ModifierBits.Shift | ModifierBits.Ctrl | ModifierBits.Alt;
        foreach (var b in _layout.Modifiers.VkToBits)
            if (sent.Contains(b.VirtualKey)) reachableBits |= b.Bits;
        foreach (var key in _layout.Keys)
        {
            // Numpad digits and the decimal key come from numpad scan codes through Num Lock.
            if (!sent.Contains(key.VirtualKey) && !VirtualKeys.IsNumpadDigit(key.VirtualKey) && key.VirtualKey != VirtualKeys.Decimal) continue;
            foreach (var (state, output) in key.Outputs)
            {
                if ((state.Bits & ~reachableBits) != 0) continue;
                if (!output.IsDead && output.Text != "\t" && output.Text.Any(char.IsControl)) continue;
                var stroke = new KeyStroke(key.VirtualKey, state);
                moves.Add((stroke, output, Effort.StrokeCost(_layout, stroke)));
            }
        }
        moves.Sort((a, b) => a.Cost.CompareTo(b.Cost));

        // Breadth-first over pending dead-key states.
        var frontier = new List<(char? Pending, List<KeyStroke> Strokes, double Cost)> { (null, [], 0) };
        var seenStates = new Dictionary<char, double>();
        for (var depth = 0; depth < MaxStrokes && frontier.Count > 0; depth++)
        {
            var next = new List<(char?, List<KeyStroke>, double)>();
            foreach (var (pending, strokes, cost) in frontier)
            {
                foreach (var (stroke, output, strokeCost) in moves)
                {
                    var total = cost + strokeCost;
                    List<KeyStroke> Path() => [.. strokes, stroke];
                    if (pending is null)
                    {
                        if (output.IsDead) Enqueue(output.Accent, Path(), total);
                        else Record(output.Text, Path(), total);
                        continue;
                    }
                    if (output.IsLigature) continue;
                    var entry = _layout.FindDeadKey(pending.Value)?.Find(output.Text[0]);
                    if (entry is null) continue;
                    if (entry.Value.Chained) Enqueue(entry.Value.Result, Path(), total);
                    else Record(entry.Value.Result.ToString(), Path(), total);
                }
            }
            frontier = next;

            void Enqueue(char accent, List<KeyStroke> path, double total)
            {
                if (seenStates.TryGetValue(accent, out var c) && c <= total) return;
                seenStates[accent] = total;
                next.Add((accent, path, total));
            }
        }

        void Record(string text, List<KeyStroke> path, double total)
        {
            if (!_best.TryGetValue(text, out var existing) || existing.Effort > total)
                _best[text] = new TypingRecipe(path, total);
        }
    }
}

/// <summary>
/// A simple, explainable typing-effort model: a cost per physical key (row and finger) plus a cost per
/// modifier combination. Lower is easier.
/// </summary>
public sealed class EffortModel
{
    public static EffortModel Default { get; } = new();

    public double ShiftCost { get; init; } = 0.6;
    public double AltGrCost { get; init; } = 1.0;
    public double CtrlCost { get; init; } = 1.0;
    public double OtherModifierCost { get; init; } = 1.5;

    /// <summary>Cost of pressing a key by scan code, ignoring modifiers.</summary>
    public double KeyCost(int scanCode) => scanCode switch
    {
        0x39 => 0.8,                                   // space (thumb)
        0x21 or 0x24 => 0.9,                           // F, J (index finger home)
        >= 0x1F and <= 0x26 => 1.0,                    // home row
        0x1E or 0x27 or 0x28 or 0x2B => 1.4,           // home row, pinky
        0x10 or 0x19 or 0x1A or 0x1B => 1.8,           // top row, pinky
        >= 0x11 and <= 0x18 => 1.4,                    // top row
        0x2C or 0x35 or 0x56 or 0x73 => 2.0,           // bottom row, pinky
        >= 0x2D and <= 0x34 => 1.6,                    // bottom row
        0x29 or 0x0C or 0x0D or 0x7D => 2.6,           // number row edges
        >= 0x02 and <= 0x0B => 2.0,                    // number row
        _ => 2.5,
    };

    public double ModifierCost(ShiftState state)
    {
        var b = state.Bits;
        var cost = 0.0;
        if (b.HasFlag(ModifierBits.Shift)) cost += ShiftCost;
        if (b.HasFlag(ModifierBits.AltGr)) cost += AltGrCost;
        else if (b.HasFlag(ModifierBits.Ctrl)) cost += CtrlCost;
        else if (b.HasFlag(ModifierBits.Alt)) cost += OtherModifierCost;
        if ((b & ~(ModifierBits.Shift | ModifierBits.AltGr)) != 0) cost += OtherModifierCost;
        return cost;
    }

    public double StrokeCost(KeyboardLayout layout, KeyStroke stroke)
    {
        var sc = PhysicalScanCode(layout, stroke.VirtualKey);
        return KeyCost(sc ?? 0xFF) + ModifierCost(stroke.State);
    }

    /// <summary>The unprefixed scan code of the key producing a virtual key (E0 keys get a generic high cost).</summary>
    public static int? PhysicalScanCode(KeyboardLayout layout, byte vk) => layout.ScanCodes.FindScanCode(vk);
}
