using System.Globalization;
using System.Text;
using Keyservant.Core.Analysis;
using Keyservant.Core.Model;
using Keyservant.Core.Unicode;

namespace Keyservant.Core.Assist;

public enum PlannedChangeKind
{
    /// <summary>A character placed directly on a key, e.g. AltGr+A → ā.</summary>
    DirectKey,
    /// <summary>A new dead key with its compositions.</summary>
    DeadKey,
    /// <summary>Compositions added to a dead key the layout already has.</summary>
    DeadKeyEntries,
    /// <summary>Two dead keys chained for characters with two marks, e.g. dot below + macron → ṝ.</summary>
    Chain,
}

/// <summary>One proposed change. <see cref="Apply"/> performs it on a layout.</summary>
public sealed class PlannedChange(PlannedChangeKind kind, string description, IReadOnlyList<string> characters, Action<KeyboardLayout> apply)
{
    public PlannedChangeKind Kind { get; } = kind;
    public string Description { get; } = description;
    public IReadOnlyList<string> Characters { get; } = characters;
    public void Apply(KeyboardLayout layout) => apply(layout);
    public override string ToString() => Description;
}

/// <summary>Proposed changes plus the characters that need no change or could not be placed.</summary>
public sealed class PlacementPlan
{
    public List<PlannedChange> Changes { get; } = [];
    public List<string> AlreadyTypeable { get; } = [];
    public List<(string Character, string Reason)> NotPlaced { get; } = [];

    public void ApplyAll(KeyboardLayout layout)
    {
        foreach (var c in Changes) c.Apply(layout);
    }

    public string Summary()
    {
        var sb = new StringBuilder();
        if (AlreadyTypeable.Count > 0) sb.Append("Already typeable: ").Append(string.Join(" ", AlreadyTypeable)).Append('\n');
        foreach (var c in Changes) sb.Append("• ").Append(c.Description).Append('\n');
        foreach (var (ch, reason) in NotPlaced) sb.Append("✗ ").Append(ch).Append(": ").Append(reason).Append('\n');
        if (Changes.Count == 0 && NotPlaced.Count == 0) sb.Append("Nothing to do.\n");
        return sb.ToString();
    }
}

public sealed record PlacementOptions
{
    /// <summary>A mark shared by at least this many characters gets a dead key; rarer ones go on direct keys.</summary>
    public int DeadKeyThreshold { get; init; } = 3;

    /// <summary>Character frequencies (e.g. from a sample text). The most frequent also get a direct key if one is free.</summary>
    public IReadOnlyDictionary<string, int>? Frequencies { get; init; }

    /// <summary>How many of the most frequent characters get a direct key in addition to their dead key.</summary>
    public int ExtraDirectKeys { get; init; } = 6;
}

/// <summary>
/// Proposes where to put characters a layout can't type yet. Characters are split into base letter and
/// marks (Unicode decomposition); marks shared by several characters become dead keys on look-alike keys
/// (dot below on the period key, macron on minus, …) with the base letter as mnemonic; characters with two
/// marks become chained dead keys; the rest go on AltGr / Shift+AltGr of their base letter. Existing
/// characters are never overwritten. Entirely offline and deterministic.
/// </summary>
public static class PlacementPlanner
{
    private sealed record MarkInfo(string Name, char? Spacing, string PreferredKeys);

    private static readonly Dictionary<char, MarkInfo> Marks = new()
    {
        ['̀'] = new("grave", '`', "`'"),
        ['́'] = new("acute", '´', "'´/"),
        ['̂'] = new("circumflex", '^', "^6"),
        ['̃'] = new("tilde", '~', "~#"),
        ['̄'] = new("macron", '¯', "-_="),
        ['̆'] = new("breve", '˘', "(9"),
        ['̇'] = new("dot above", '˙', ";:"),
        ['̈'] = new("diaeresis", '¨', "\":"),
        ['̊'] = new("ring above", '˚', "°0"),
        ['̋'] = new("double acute", '˝', "\""),
        ['̌'] = new("caron", 'ˇ', "<v"),
        ['̣'] = new("dot below", null, ".,"),
        ['̤'] = new("diaeresis below", null, ":"),
        ['̥'] = new("ring below", null, "0"),
        ['̦'] = new("comma below", null, ","),
        ['̧'] = new("cedilla", '¸', ","),
        ['̨'] = new("ogonek", '˛', ","),
        ['̭'] = new("circumflex below", null, "^"),
        ['̱'] = new("macron below", null, "_-"),
        ['̛'] = new("horn", null, "+"),
    };

    private static readonly ShiftState[] SymbolSlots = [ShiftState.AltGr, ShiftState.ShiftAltGr];

    public static PlacementPlan Plan(KeyboardLayout layout, string characters, PlacementOptions? options = null)
    {
        options ??= new PlacementOptions();
        var plan = new PlacementPlan();
        var work = layout.Clone();
        var finder = new KeystrokeFinder(Compose.ComposeCompiler.Compile(work).Layout);

        // Distinct characters, in order of appearance (or frequency).
        var wanted = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(characters.Normalize(NormalizationForm.FormC));
        while (e.MoveNext())
        {
            var t = (string)e.Current;
            if (t.All(char.IsWhiteSpace) || t.Any(char.IsControl) || wanted.Contains(t)) continue;
            wanted.Add(t);
        }
        if (options.Frequencies is { } freq) wanted = [.. wanted.OrderByDescending(w => freq.GetValueOrDefault(w))];

        var singles = new Dictionary<char, List<(string Char, char Base)>>();
        var chains = new Dictionary<(char, char), List<(string Char, char Base)>>();
        var direct = new List<string>();
        foreach (var c in wanted)
        {
            if (finder.CanType(c)) { plan.AlreadyTypeable.Add(c); continue; }
            var d = c.Normalize(NormalizationForm.FormD);
            var marks = d.Skip(1).ToArray();
            var baseOk = d.Length > 0 && !char.IsSurrogate(d[0]) && FindBaseKey(work, d[0]) is not null;
            if (c.Length != 1 || marks.Length == 0 || !baseOk || marks.Any(m => !Marks.ContainsKey(m)) || marks.Length > 2)
            {
                direct.Add(c);
                continue;
            }
            if (marks.Length == 1) Get(singles, marks[0]).Add((c, d[0]));
            else Get(chains, (marks[0], marks[1])).Add((c, d[0]));
        }

        // Which marks get a dead key.
        var deadMarks = new HashSet<char>();
        foreach (var (mark, list) in singles)
        {
            if (list.Count >= options.DeadKeyThreshold || ExistingDeadKey(work, mark) is not null) deadMarks.Add(mark);
        }
        foreach (var (m1, m2) in chains.Keys) { deadMarks.Add(m1); deadMarks.Add(m2); }

        // Characters with a mark that stays without dead key go on direct keys.
        foreach (var (mark, list) in singles)
        {
            if (!deadMarks.Contains(mark)) direct.AddRange(list.Select(x => x.Char));
        }

        // Dead keys.
        var deadAccents = new Dictionary<char, char>();
        foreach (var mark in deadMarks.OrderByDescending(m => singles.GetValueOrDefault(m)?.Count ?? 0))
        {
            var entries = singles.GetValueOrDefault(mark) ?? [];
            var info = Marks[mark];
            if (ExistingDeadKey(work, mark) is { } existing)
            {
                deadAccents[mark] = existing;
                if (entries.Count == 0) continue;
                var accent = existing;
                var list = entries.ToList();
                plan.Changes.Add(Record(work, new PlannedChange(PlannedChangeKind.DeadKeyEntries,
                    $"Add to the existing {info.Name} dead key: {string.Join(" ", list.Select(x => $"{x.Base}→{x.Char}"))}",
                    [.. list.Select(x => x.Char)],
                    l => { var dk = l.GetOrAddDeadKey(accent); foreach (var (ch, b) in list) if (dk.Find(b) is null) dk.Set(b, ch[0]); })));
                continue;
            }

            var slot = FindSlotForSymbols(work, info.PreferredKeys) ?? FindAnyFreeSlot(work, preferPunctuation: true);
            if (slot is null)
            {
                foreach (var (ch, _) in entries) plan.NotPlaced.Add((ch, $"no free AltGr position left for a {info.Name} dead key"));
                continue;
            }
            var newAccent = info.Spacing ?? mark;
            deadAccents[mark] = newAccent;
            var (vk, state) = slot.Value;
            var name = (UnicodeCatalog.GetName(mark) ?? info.Name).Replace("COMBINING ", "", StringComparison.Ordinal);
            var spaceResult = info.Spacing ?? mark;
            var items = entries.ToList();
            var keyLabel = KeySequence.Format([new KeyStroke(vk, state)], work);
            plan.Changes.Add(Record(work, new PlannedChange(PlannedChangeKind.DeadKey,
                $"Dead key {info.Name} on {keyLabel}" + (items.Count > 0 ? $": {string.Join(" ", items.Select(x => $"{x.Base}→{x.Char}"))}" : " (for chained characters)"),
                [.. items.Select(x => x.Char)],
                l =>
                {
                    l.SetOutput(vk, state, KeyOutput.Dead(newAccent));
                    var dk = l.GetOrAddDeadKey(newAccent);
                    dk.Name ??= name;
                    if (dk.Find(' ') is null) dk.Set(' ', spaceResult);
                    foreach (var (ch, b) in items) if (dk.Find(b) is null) dk.Set(b, ch[0]);
                })));
        }

        // Chains for characters with two marks.
        foreach (var ((m1, m2), list) in chains)
        {
            if (!deadAccents.TryGetValue(m1, out var a1) || !deadAccents.TryGetValue(m2, out var a2))
            {
                foreach (var (ch, _) in list) plan.NotPlaced.Add((ch, "its dead keys could not be placed"));
                continue;
            }
            var intermediate = list[0].Char[0];
            var items = list.ToList();
            plan.Changes.Add(Record(work, new PlannedChange(PlannedChangeKind.Chain,
                $"Chain {Marks[m1].Name} + {Marks[m2].Name} (either order), then {string.Join(" ", items.Select(x => $"{x.Base}→{x.Char}"))}",
                [.. items.Select(x => x.Char)],
                l =>
                {
                    l.GetOrAddDeadKey(a1).Set(a2, intermediate, chained: true);
                    l.GetOrAddDeadKey(a2).Set(a1, intermediate, chained: true);
                    var state = l.GetOrAddDeadKey(intermediate);
                    state.Name ??= $"{Marks[m1].Name} and {Marks[m2].Name}".ToUpperInvariant();
                    if (state.Find(' ') is null) state.Set(' ', intermediate);
                    foreach (var (ch, b) in items) if (state.Find(b) is null) state.Set(b, ch[0]);
                })));
        }

        // Frequent characters also get a direct key.
        if (options.Frequencies is not null)
        {
            var extra = singles.Values.SelectMany(v => v).Concat(chains.Values.SelectMany(v => v)).Select(x => x.Char)
                .OrderByDescending(c => options.Frequencies.GetValueOrDefault(c)).Take(options.ExtraDirectKeys);
            direct.AddRange(extra.Where(c => !direct.Contains(c)));
        }

        // Direct keys: AltGr / Shift+AltGr on the base letter, else any free slot.
        foreach (var c in direct)
        {
            var slot = DirectSlot(work, c);
            if (slot is null)
            {
                if (!plan.Changes.Any(ch => ch.Characters.Contains(c))) plan.NotPlaced.Add((c, "no free AltGr position"));
                continue;
            }
            var (vk, state) = slot.Value;
            var label = KeySequence.Format([new KeyStroke(vk, state)], work);
            plan.Changes.Add(Record(work, new PlannedChange(PlannedChangeKind.DirectKey, $"{label} → {c}", [c],
                l => { if (l.FindKey(vk)?[state] is null) l.SetOutput(vk, state, KeyOutput.Of(c)); })));
        }
        return plan;
    }

    private static PlannedChange Record(KeyboardLayout work, PlannedChange change)
    {
        change.Apply(work); // keep the working copy up to date so later proposals see what is taken
        return change;
    }

    private static List<T> Get<TKey, T>(Dictionary<TKey, List<T>> d, TKey key) where TKey : notnull
    {
        if (!d.TryGetValue(key, out var list)) d[key] = list = [];
        return list;
    }

    /// <summary>The key that types <paramref name="c"/> (or its lowercase form) without modifiers.</summary>
    private static KeyDefinition? FindBaseKey(KeyboardLayout l, char c)
    {
        var lower = char.ToLowerInvariant(c).ToString();
        return l.Keys.FirstOrDefault(k => k.Outputs.GetValueOrDefault(ShiftState.Base) is { IsDead: false } o && o.Text == lower);
    }

    private static char? ExistingDeadKey(KeyboardLayout l, char mark)
    {
        var spacing = Marks.TryGetValue(mark, out var info) ? info.Spacing : null;
        foreach (var k in l.Keys)
        {
            foreach (var o in k.Outputs.Values)
            {
                if (o.IsDead && (o.Accent == mark || o.Accent == spacing)) return o.Accent;
            }
        }
        return null;
    }

    private static bool IsFree(KeyboardLayout l, byte vk, ShiftState state) => l.FindKey(vk)?[state] is null;

    private static (byte, ShiftState)? FindSlotForSymbols(KeyboardLayout l, string symbols)
    {
        foreach (var symbol in symbols)
        {
            var s = symbol.ToString();
            foreach (var key in l.Keys.Where(k => k.Outputs.Values.Any(o => o.Text == s && (o == k[ShiftState.Base] || o == k[ShiftState.Shift]))))
            {
                foreach (var slot in SymbolSlots)
                {
                    if (IsFree(l, key.VirtualKey, slot)) return (key.VirtualKey, slot);
                }
            }
        }
        return null;
    }

    private static (byte, ShiftState)? FindAnyFreeSlot(KeyboardLayout l, bool preferPunctuation)
    {
        var keys = l.Keys.Where(k => IsReachable(l, k.VirtualKey) && !VirtualKeys.IsImplicitControlKey(k.VirtualKey) && k.VirtualKey != VirtualKeys.Space && k.VirtualKey != VirtualKeys.Decimal);
        var ordered = preferPunctuation ? keys.OrderBy(k => char.IsAsciiLetter((char)k.VirtualKey) ? 1 : 0) : keys.OrderBy(_ => 0);
        foreach (var slot in SymbolSlots)
        {
            foreach (var key in ordered)
            {
                if (IsFree(l, key.VirtualKey, slot)) return (key.VirtualKey, slot);
            }
        }
        return null;
    }

    private static bool IsReachable(KeyboardLayout l, byte vk) => l.ScanCodes.FindScanCode(vk) is not null;

    private static (byte, ShiftState)? DirectSlot(KeyboardLayout l, string c)
    {
        var d = c.Normalize(NormalizationForm.FormD);
        if (c.Length == 1 && d.Length > 0 && FindBaseKey(l, d[0]) is { } baseKey)
        {
            var upper = char.IsUpper(c[0]);
            var preferred = upper ? ShiftState.ShiftAltGr : ShiftState.AltGr;
            if (IsFree(l, baseKey.VirtualKey, preferred)) return (baseKey.VirtualKey, preferred);
            // The base letter's slot is taken: try a key that looks like the mark (~ for a tilde, …).
            foreach (var mark in d.Skip(1))
            {
                if (Marks.TryGetValue(mark, out var info) && FindSlotForSymbols(l, info.PreferredKeys) is { } symbolSlot) return symbolSlot;
            }
        }
        return FindAnyFreeSlot(l, preferPunctuation: true);
    }
}
