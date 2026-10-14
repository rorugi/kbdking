using System.Globalization;
using System.Text;
using Keyservant.Core.Model;

namespace Keyservant.Core.Diff;

/// <summary>One difference between two layouts.</summary>
public sealed record LayoutChange(string Area, string Item, string? Before, string? After)
{
    /// <summary>The key concerned, for highlighting on the keyboard.</summary>
    public byte? VirtualKey { get; init; }

    public ShiftState? State { get; init; }

    public string Kind => Before is null ? "added" : After is null ? "removed" : "changed";

    public override string ToString() => Kind switch
    {
        "added" => $"+ {Area}: {Item} = {After}",
        "removed" => $"- {Area}: {Item} (was {Before})",
        _ => $"~ {Area}: {Item}: {Before} → {After}",
    };
}

/// <summary>Compares two layouts and lists what changed, area by area.</summary>
public static class LayoutDiff
{
    public static IReadOnlyList<LayoutChange> Compare(KeyboardLayout before, KeyboardLayout after)
    {
        var changes = new List<LayoutChange>();
        void Add(string area, string item, string? b, string? a, byte? vk = null, ShiftState? st = null)
        {
            if (b != a) changes.Add(new LayoutChange(area, item, b, a) { VirtualKey = vk, State = st });
        }

        var mb = before.Metadata;
        var ma = after.Metadata;
        Add("Metadata", "name", mb.Name, ma.Name);
        Add("Metadata", "DLL name", mb.DllName, ma.DllName);
        Add("Metadata", "locale", mb.Locale, ma.Locale);
        Add("Metadata", "version", mb.Version, ma.Version);
        Add("Metadata", "author", mb.Author, ma.Author);
        Add("Metadata", "description", mb.Description, ma.Description);
        Add("Flags", "flags", before.Flags.ToString(), after.Flags.ToString());
        Add("Shift states", "columns",
            string.Join(", ", before.Modifiers.Columns.Select(c => c.Name)),
            string.Join(", ", after.Modifiers.Columns.Select(c => c.Name)));

        // Scan codes
        var maxScan = Math.Max(before.ScanCodes.Normal.Count, after.ScanCodes.Normal.Count);
        for (var sc = 0; sc < maxScan; sc++)
        {
            var b = before.ScanCodes[sc];
            var a = after.ScanCodes[sc];
            if (b != a) Add("Scan codes", sc.ToString("X2", CultureInfo.InvariantCulture), b.IsNone ? null : b.ToString(), a.IsNone ? null : a.ToString());
        }
        foreach (var sc in before.ScanCodes.E0.Keys.Union(after.ScanCodes.E0.Keys).Order())
        {
            Add("Scan codes", "E0 " + sc.ToString("X2", CultureInfo.InvariantCulture),
                before.ScanCodes.E0.TryGetValue(sc, out var b) ? b.ToString() : null,
                after.ScanCodes.E0.TryGetValue(sc, out var a) ? a.ToString() : null);
        }

        // Keys
        var vks = before.Keys.Select(k => k.VirtualKey).Union(after.Keys.Select(k => k.VirtualKey));
        foreach (var vk in vks)
        {
            var kb = before.FindKey(vk);
            var ka = after.FindKey(vk);
            var name = VirtualKeys.GetName(vk);
            var states = (kb?.Outputs.Keys ?? Enumerable.Empty<ShiftState>()).Union(ka?.Outputs.Keys ?? Enumerable.Empty<ShiftState>()).Order();
            foreach (var st in states)
            {
                Add("Keys", $"{name} {st.DisplayName}", Show(kb?.Outputs.GetValueOrDefault(st)), Show(ka?.Outputs.GetValueOrDefault(st)), vk, st);
            }
            if (kb is not null && ka is not null)
            {
                Add("Keys", $"{name} attributes", kb.Attributes.ToString(), ka.Attributes.ToString(), vk);
                var capsB = kb.CapsOutputs is null ? null : string.Join(" ", kb.CapsOutputs.Select(p => $"{p.Key.Name}={Show(p.Value)}"));
                var capsA = ka.CapsOutputs is null ? null : string.Join(" ", ka.CapsOutputs.Select(p => $"{p.Key.Name}={Show(p.Value)}"));
                Add("Keys", $"{name} Caps Lock row", capsB, capsA, vk);
            }
        }

        // Dead keys
        foreach (var accent in before.DeadKeys.Select(d => d.Accent).Union(after.DeadKeys.Select(d => d.Accent)))
        {
            var db = before.FindDeadKey(accent);
            var da = after.FindDeadKey(accent);
            var label = Char(accent);
            if (db is null || da is null)
            {
                Add("Dead keys", label, db is null ? null : $"{db.Entries.Count} compositions", da is null ? null : $"{da.Entries.Count} compositions");
                continue;
            }
            Add("Dead keys", $"{label} name", db.Name, da.Name);
            foreach (var bc in db.Entries.Select(e => e.Base).Union(da.Entries.Select(e => e.Base)))
            {
                Add("Dead keys", $"{label} + {Char(bc)}", Entry(db.Find(bc)), Entry(da.Find(bc)));
            }
        }

        // Key names
        foreach (var sc in before.KeyNames.Keys.Union(after.KeyNames.Keys).Order())
            Add("Key names", sc.ToString("X2", CultureInfo.InvariantCulture), before.KeyNames.GetValueOrDefault(sc), after.KeyNames.GetValueOrDefault(sc));
        foreach (var sc in before.KeyNamesExt.Keys.Union(after.KeyNamesExt.Keys).Order())
            Add("Key names", "E0 " + sc.ToString("X2", CultureInfo.InvariantCulture), before.KeyNamesExt.GetValueOrDefault(sc), after.KeyNamesExt.GetValueOrDefault(sc));

        // Compose
        Add("Compose", "key", before.Compose?.KeyText, after.Compose?.KeyText);
        var seqB = before.Compose?.Sequences.GroupBy(s => s.Keys).ToDictionary(g => g.Key, g => g.First().Result) ?? [];
        var seqA = after.Compose?.Sequences.GroupBy(s => s.Keys).ToDictionary(g => g.Key, g => g.First().Result) ?? [];
        foreach (var k in seqB.Keys.Union(seqA.Keys).Order(StringComparer.Ordinal))
            Add("Compose", k, seqB.GetValueOrDefault(k), seqA.GetValueOrDefault(k));

        // Tests
        var tb = before.Tests.GroupBy(t => t.Keys).ToDictionary(g => g.Key, g => g.First().Expect);
        var ta = after.Tests.GroupBy(t => t.Keys).ToDictionary(g => g.Key, g => g.First().Expect);
        foreach (var k in tb.Keys.Union(ta.Keys).Order(StringComparer.Ordinal))
            Add("Tests", k, tb.GetValueOrDefault(k), ta.GetValueOrDefault(k));

        return changes;
    }

    public static string Format(IReadOnlyList<LayoutChange> changes)
    {
        if (changes.Count == 0) return "The layouts are identical.";
        var sb = new StringBuilder();
        foreach (var group in changes.GroupBy(c => c.Area))
        {
            sb.Append(group.Key).Append(" (").Append(group.Count()).Append(")\n");
            foreach (var c in group) sb.Append("  ").Append(c).Append('\n');
        }
        return sb.ToString();
    }

    private static string? Show(KeyOutput? o) =>
        o is null ? null : o.IsDead ? $"dead {Char(o.Accent)}" : string.Concat(o.Text.Select(c => char.IsControl(c) ? $"U+{(int)c:X4}" : c.ToString()));

    private static string? Entry(DeadKeyEntry? e) => e is { } x ? Char(x.Result) + (x.Chained ? " (chained)" : "") : null;

    private static string Char(char c) =>
        char.IsControl(c) || char.IsWhiteSpace(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format or UnicodeCategory.PrivateUse
            ? $"U+{(int)c:X4}" : c.ToString();
}
