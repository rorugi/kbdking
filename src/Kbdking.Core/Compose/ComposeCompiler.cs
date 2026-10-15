using Kbdking.Core.Model;

namespace Kbdking.Core.Compose;

/// <summary>Result of compiling compose sequences: the effective layout and what could not be expressed.</summary>
public sealed record ComposeCompilation(KeyboardLayout Layout, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
{
    public int CompiledSequences { get; init; }
}

/// <summary>
/// Turns compose sequences into native Windows layout tables.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The compose key's scan code is remapped to an unused virtual key whose only output is a dead key
/// (the compose accent).</item>
/// <item>All sequences form a tree. Each branching point becomes a chained dead-key state with its own
/// accent character from the Private Use Area; each leaf becomes a dead-key composition.</item>
/// </list>
/// Windows limits what this can express: a sequence produces exactly one BMP character, a sequence can't
/// also be the start of a longer one, and abandoning a sequence types the current state's accent.
/// </remarks>
public static class ComposeCompiler
{
    private static readonly byte[] CandidateVks = [0xDF, 0xE1, 0xE3, 0xE4, 0xE6, 0xE9, 0xEA, 0xEB, 0xEC, 0xED, 0xEE, 0xEF, 0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0x92, 0x93, 0x94, 0x95, 0x96];

    private const char FirstStateAccent = '';
    private const char LastStateAccent = '';

    private sealed class Node
    {
        public SortedDictionary<char, Node> Children { get; } = new();
        public char? Result { get; set; }
        public string Path { get; init; } = "";
        public char Accent { get; set; }
    }

    /// <summary>
    /// Returns the layout with compose sequences compiled in. Layouts without compose sequences are
    /// returned unchanged (not copied). The compose definition stays attached so it can still be shown.
    /// </summary>
    public static ComposeCompilation Compile(KeyboardLayout layout)
    {
        if (layout.Compose is not { } compose || compose.Sequences.Count == 0) return new ComposeCompilation(layout, [], []);
        if (IsCompiled(layout, compose)) return new ComposeCompilation(layout, [], []);

        var warnings = new List<string>();
        var errors = new List<string>();
        var result = layout.Clone();

        // 0. The compose key must not take over a key the layout still needs.
        var previous = compose.Extended ? layout.ScanCodes.E0.GetValueOrDefault((byte)compose.ScanCode, ScanCodeTarget.None) : layout.ScanCodes[compose.ScanCode];
        if (!previous.IsNone)
        {
            if (layout.Modifiers.VkToBits.Any(b => b.VirtualKey == previous.VirtualKey))
                errors.Add($"The compose key ({compose.KeyText}) is also a layer or modifier key of this layout. Choose another compose key.");
            else if (compose.Extended && compose.ScanCode == 0x38 && layout.Flags.HasFlag(LocaleFlags.AltGr)
                     && layout.Keys.Any(k => k.Outputs.Keys.Any(s => s.Bits.HasFlag(ModifierBits.AltGr))))
                errors.Add("Right Alt is the AltGr key of this layout, so it can't be the compose key. Choose another key.");
            else if (layout.FindKey(previous.VirtualKey) is { IsEmpty: false } typed && !VirtualKeys.IsImplicitControlKey(previous.VirtualKey))
                warnings.Add($"The compose key ({compose.KeyText}) typed characters before (virtual key {typed.Name}); they can no longer be typed.");
        }
        if (layout.FindDeadKey(compose.Accent) is not null || layout.Keys.Any(k => k.Outputs.Values.Any(o => o.Text.Contains(compose.Accent))))
            errors.Add($"The compose accent '{compose.Accent}' (U+{(int)compose.Accent:X4}) is already used by the layout. Pick a character the layout doesn't type.");
        if (errors.Count > 0) return new ComposeCompilation(layout, warnings, errors);

        // 1. A free virtual key for the compose key.
        var used = new HashSet<byte>(result.Keys.Select(k => k.VirtualKey));
        foreach (var b in result.Modifiers.VkToBits) used.Add(b.VirtualKey);
        foreach (var t in result.ScanCodes.Normal) used.Add(t.VirtualKey);
        foreach (var t in result.ScanCodes.E0.Values) used.Add(t.VirtualKey);
        var vk = CandidateVks.FirstOrDefault(v => !used.Contains(v));
        if (vk == 0)
        {
            errors.Add("No unused virtual key is left for the compose key.");
            return new ComposeCompilation(layout, warnings, errors);
        }
        if (compose.Extended) result.ScanCodes.E0[(byte)compose.ScanCode] = new ScanCodeTarget(vk, ScanCodeFlags.Ext);
        else result.ScanCodes.Set(compose.ScanCode, new ScanCodeTarget(vk, ScanCodeFlags.None));
        var key = result.GetOrAddKey(vk);
        key.Outputs.Clear();
        key.Outputs[ShiftState.Base] = KeyOutput.Dead(compose.Accent);
        result.Modifiers.EnsureColumn(ShiftState.Shift);
        key.Outputs[ShiftState.Shift] = KeyOutput.Dead(compose.Accent);

        // 2. The sequence tree.
        var root = new Node { Accent = compose.Accent };
        var compiled = 0;
        foreach (var seq in compose.Sequences)
        {
            if (seq.Keys.Length == 0) { warnings.Add($"Skipped a sequence for \"{seq.Result}\" without keys."); continue; }
            if (seq.Result.Length != 1 || char.IsSurrogate(seq.Result[0]))
            {
                warnings.Add($"Skipped \"{seq.Keys}\" → \"{seq.Result}\": Windows dead keys produce exactly one character from the Basic Multilingual Plane.");
                continue;
            }
            if (seq.Keys.Any(char.IsSurrogate))
            {
                warnings.Add($"Skipped \"{seq.Keys}\": keys typed in a sequence must be characters from the Basic Multilingual Plane.");
                continue;
            }
            var node = root;
            var conflict = false;
            for (var i = 0; i < seq.Keys.Length; i++)
            {
                if (node.Result is not null && node != root)
                {
                    warnings.Add($"Skipped \"{seq.Keys}\": \"{node.Path}\" already produces \"{node.Result}\", so it can't start a longer sequence.");
                    conflict = true;
                    break;
                }
                var c = seq.Keys[i];
                if (!node.Children.TryGetValue(c, out var child))
                {
                    child = new Node { Path = node.Path + c };
                    node.Children[c] = child;
                }
                node = child;
            }
            if (conflict) continue;
            if (node.Children.Count > 0)
            {
                warnings.Add($"Skipped \"{seq.Keys}\" → \"{seq.Result}\": it is the start of longer sequences.");
                continue;
            }
            if (node.Result is { } existing)
            {
                if (existing != seq.Result[0]) warnings.Add($"\"{seq.Keys}\" is defined twice (\"{existing}\" and \"{seq.Result}\"); keeping the first.");
                continue;
            }
            node.Result = seq.Result[0];
            compiled++;
        }

        // 3. Accents for intermediate states, avoiding characters the layout already uses.
        var taken = new HashSet<char>(result.DeadKeys.Select(d => d.Accent)) { compose.Accent };
        foreach (var k in result.Keys)
        {
            foreach (var o in k.Outputs.Values) foreach (var ch in o.Text) taken.Add(ch);
        }
        var next = FirstStateAccent;
        var ok = true;
        void Assign(Node n)
        {
            foreach (var child in n.Children.Values)
            {
                if (child.Children.Count == 0) continue;
                while (next <= LastStateAccent && taken.Contains(next)) next++;
                if (next > LastStateAccent) { ok = false; return; }
                child.Accent = next++;
                Assign(child);
            }
        }
        Assign(root);
        if (!ok)
        {
            errors.Add("Too many compose states (more than 4096).");
            return new ComposeCompilation(layout, warnings, errors);
        }

        // 4. Dead-key tables.
        void Emit(Node n)
        {
            if (n.Children.Count == 0) return;
            var dead = result.GetOrAddDeadKey(n.Accent);
            dead.Name ??= n == root ? "COMPOSE" : "COMPOSE " + n.Path;
            foreach (var (c, child) in n.Children)
            {
                if (dead.Find(c) is not null) continue;
                if (child.Children.Count > 0) dead.Set(c, child.Accent, chained: true);
                else if (child.Result is { } r) dead.Set(c, r);
                Emit(child);
            }
        }
        Emit(root);

        // 5. Sequences whose keys the layout can't type at all.
        var typeable = TypeableCharacters(result);
        foreach (var seq in compose.Sequences)
        {
            var missing = seq.Keys.Where(c => !typeable.Contains(c)).Distinct().ToList();
            if (missing.Count > 0) warnings.Add($"\"{seq.Keys}\" uses {string.Join(", ", missing.Select(c => $"'{c}'"))}, which no key types directly.");
        }

        return new ComposeCompilation(result, warnings, errors) { CompiledSequences = compiled };
    }

    /// <summary>True for a layout that already went through <see cref="Compile"/> (its compose key types the compose accent).</summary>
    private static bool IsCompiled(KeyboardLayout layout, ComposeDefinition compose)
    {
        var target = compose.Extended ? layout.ScanCodes.E0.GetValueOrDefault((byte)compose.ScanCode, ScanCodeTarget.None) : layout.ScanCodes[compose.ScanCode];
        return !target.IsNone && layout.FindKey(target.VirtualKey)?.Outputs.GetValueOrDefault(ShiftState.Base) is { IsDead: true } o
            && o.Accent == compose.Accent && layout.FindDeadKey(compose.Accent) is not null;
    }

    private static HashSet<char> TypeableCharacters(KeyboardLayout layout)
    {
        var set = new HashSet<char>();
        foreach (var k in layout.Keys)
        {
            foreach (var o in k.Outputs.Values)
            {
                if (o.Text.Length == 1) set.Add(o.Text[0]);
            }
        }
        return set;
    }
}
