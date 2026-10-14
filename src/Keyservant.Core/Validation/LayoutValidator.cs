using System.Globalization;
using System.Text.RegularExpressions;
using Keyservant.Core.Model;

namespace Keyservant.Core.Validation;

public enum Severity
{
    Info,
    Warning,
    /// <summary>The layout can't be built until this is fixed.</summary>
    Error,
}

/// <summary>One validation finding. <see cref="VirtualKey"/> and <see cref="State"/> point the editor at the key.</summary>
public sealed record ValidationIssue(Severity Severity, string Code, string Message, byte? VirtualKey = null, ShiftState? State = null)
{
    public override string ToString() => $"{Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}

public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = [];

    public bool HasErrors => Issues.Any(i => i.Severity == Severity.Error);

    public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.Severity == Severity.Error);

    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == Severity.Warning);
}

/// <summary>Checks a layout for problems before it is built into a DLL.</summary>
public static partial class LayoutValidator
{
    /// <summary>The longest ligature MSKLC accepts; longer ones work but are unusual.</summary>
    public const int RecommendedMaxLigature = 4;

    /// <summary>Hard limit: VK_TO_WCHARS rows and ligature rows are sized in a byte.</summary>
    public const int MaxLigature = 16;

    public const int MaxColumns = 15;

    [GeneratedRegex("^[A-Za-z0-9_]{1,8}$")]
    private static partial Regex DllNameRegex();

    [GeneratedRegex("^[0-9A-Fa-f]{8}$")]
    private static partial Regex KlidRegex();

    public static ValidationResult Validate(KeyboardLayout layout)
    {
        var r = new ValidationResult();
        void Add(Severity s, string code, string msg, byte? vk = null, ShiftState? st = null) => r.Issues.Add(new(s, code, msg, vk, st));

        // Metadata
        var m = layout.Metadata;
        if (string.IsNullOrWhiteSpace(m.Name)) Add(Severity.Error, "KS001", "The layout needs a name.");
        if (!DllNameRegex().IsMatch(m.DllName ?? ""))
            Add(Severity.Error, "KS002", $"DLL name '{m.DllName}' must be 1–8 letters, digits or underscores (e.g. kbdiast).");
        if (m.LayoutId is not null && !KlidRegex().IsMatch(m.LayoutId))
            Add(Severity.Error, "KS003", $"Layout ID '{m.LayoutId}' must be 8 hex digits, e.g. A0000C07.");
        if (!IsKnownLocale(m.Locale)) Add(Severity.Warning, "KS004", $"Locale '{m.Locale}' is not a known language tag.");
        if (!Version.TryParse(m.Version.Contains('.', StringComparison.Ordinal) ? m.Version : m.Version + ".0", out _))
            Add(Severity.Warning, "KS005", $"Version '{m.Version}' should look like 1.0 or 1.2.3.");

        // Modifiers
        var columns = layout.Modifiers.ColumnCount;
        if (columns == 0) Add(Severity.Error, "KS010", "The layout has no shift states.");
        if (columns > MaxColumns) Add(Severity.Error, "KS011", $"At most {MaxColumns} shift states are possible; this layout has {columns}.");
        for (var c = 0; c < columns; c++)
        {
            if (layout.Modifiers.StatesOf(c).Count == 0) Add(Severity.Error, "KS012", $"Column {c} is not selected by any modifier combination.");
        }

        // Keys
        var seen = new HashSet<byte>();
        var altGrCount = 0;
        foreach (var key in layout.Keys)
        {
            var vk = key.VirtualKey;
            if (vk is 0 or VirtualKeys.ContinuationRow) Add(Severity.Error, "KS020", $"Virtual key 0x{vk:X2} is reserved and can't carry characters.", vk);
            if (!seen.Add(vk)) Add(Severity.Error, "KS021", $"Key {key.Name} is defined twice.", vk);

            foreach (var (state, output) in key.Outputs)
            {
                if (layout.Modifiers.ColumnOf(state) is null)
                    Add(Severity.Error, "KS022", $"Key {key.Name} has a character for {state.DisplayName}, but the layout has no {state.DisplayName} shift state.", vk, state);
                CheckOutput(output, key, state, Add);
                if (state.Bits.HasFlag(ModifierBits.AltGr)) altGrCount++;
            }

            if (key.CapsOutputs is { } caps)
            {
                foreach (var (state, output) in caps)
                {
                    CheckOutput(output, key, state, Add);
                    if (output.IsLigature) Add(Severity.Error, "KS023", $"Key {key.Name}: ligatures are not possible in the Caps Lock row.", vk, state);
                }
                if (key.Outputs.Values.Any(o => o.IsDead))
                    Add(Severity.Error, "KS024", $"Key {key.Name} has separate Caps Lock characters and a dead key; Windows can't combine the two.", vk);
            }

            if (!IsReachable(layout, vk))
                Add(Severity.Warning, "KS025", $"No scan code produces key {key.Name}, so its characters can't be typed.", vk);
        }

        if (altGrCount > 0 && !layout.Flags.HasFlag(LocaleFlags.AltGr))
            Add(Severity.Warning, "KS030", "The layout has AltGr characters but the AltGr flag is off, so Right Alt won't act as AltGr.");
        if (altGrCount > 0)
            Add(Severity.Info, "KS031", $"{altGrCount} AltGr assignments. Windows treats AltGr as Ctrl+Alt, so apps with Ctrl+Alt shortcuts on those keys may stop responding to them.");
        if (layout.Keys.Any(k => k.Outputs.Values.Any(o => o.IsLigature)) && layout.KbdVersion < 1)
            Add(Severity.Error, "KS032", "Ligatures need kbdVersion 1.");

        // Dead keys
        var accents = new HashSet<char>();
        foreach (var d in layout.DeadKeys)
        {
            if (!accents.Add(d.Accent)) Add(Severity.Error, "KS040", $"Dead key U+{(int)d.Accent:X4} is defined twice.");
            if (IsReserved(d.Accent)) Add(Severity.Error, "KS041", $"Dead key U+{(int)d.Accent:X4} uses a reserved code point.");
            var bases = new HashSet<char>();
            foreach (var e in d.Entries)
            {
                if (!bases.Add(e.Base)) Add(Severity.Error, "KS042", $"Dead key {Show(d.Accent)}: base {Show(e.Base)} is listed twice.");
                if (IsReserved(e.Base) || IsReserved(e.Result)) Add(Severity.Error, "KS043", $"Dead key {Show(d.Accent)}: entry {Show(e.Base)} → {Show(e.Result)} uses a reserved code point.");
                if (e.Chained && layout.FindDeadKey(e.Result) is null)
                    Add(Severity.Error, "KS044", $"Dead key {Show(d.Accent)}: {Show(e.Base)} chains to {Show(e.Result)}, which is not a dead key.");
            }
            if (d.Entries.Count == 0)
                Add(Severity.Warning, "KS045", $"Dead key {Show(d.Accent)} has no compositions; it will only produce the accent followed by the next character.");
            if (!layout.Keys.Any(k => k.Outputs.Values.Concat(k.CapsOutputs?.Values ?? Enumerable.Empty<KeyOutput>()).Any(o => o.IsDead && o.Accent == d.Accent))
                && !layout.DeadKeys.Any(x => x.Entries.Any(e => e.Chained && e.Result == d.Accent)))
                Add(Severity.Info, "KS046", $"Dead key {Show(d.Accent)} is defined but no key or chain reaches it.");
        }
        foreach (var cycle in FindChainCycles(layout))
            Add(Severity.Warning, "KS047", $"Dead keys chain in a loop: {string.Join(" → ", cycle.Select(Show))}.");

        foreach (var key in layout.Keys)
        {
            foreach (var (state, o) in key.Outputs)
            {
                if (o.IsDead && layout.FindDeadKey(o.Accent) is null)
                    Add(Severity.Warning, "KS048", $"Key {key.Name} ({state.DisplayName}) is a dead key {Show(o.Accent)} without compositions.", key.VirtualKey, state);
            }
        }

        if (layout.KeyNames.Count == 0)
            Add(Severity.Warning, "KS050", "The layout has no key names; Windows will show empty names for keys like Shift or Enter.");
        return r;
    }

    private static void CheckOutput(KeyOutput output, KeyDefinition key, ShiftState state, Action<Severity, string, string, byte?, ShiftState?> add)
    {
        var vk = key.VirtualKey;
        foreach (var c in output.Text)
        {
            if (IsReserved(c))
                add(Severity.Error, "KS060", $"Key {key.Name} ({state.DisplayName}) uses reserved code point U+{(int)c:X4}.", vk, state);
        }
        if (!output.IsDead)
        {
            for (var i = 0; i < output.Text.Length; i++)
            {
                var c = output.Text[i];
                if (char.IsHighSurrogate(c) && (i + 1 >= output.Text.Length || !char.IsLowSurrogate(output.Text[i + 1])) ||
                    char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(output.Text[i - 1])))
                    add(Severity.Error, "KS061", $"Key {key.Name} ({state.DisplayName}) contains a broken surrogate pair.", vk, state);
            }
            if (output.Text.Length > MaxLigature)
                add(Severity.Error, "KS062", $"Key {key.Name} ({state.DisplayName}) produces {output.Text.Length} UTF-16 units; the maximum is {MaxLigature}.", vk, state);
            else if (output.Text.Length > RecommendedMaxLigature)
                add(Severity.Warning, "KS063", $"Key {key.Name} ({state.DisplayName}) produces {output.Text.Length} UTF-16 units; some apps only handle up to {RecommendedMaxLigature}.", vk, state);
        }
    }

    /// <summary>Code points Windows uses as markers in the character tables (WCH_NONE, WCH_DEAD, WCH_LGTR).</summary>
    private static bool IsReserved(char c) => c is '' or '' or '';

    private static bool IsReachable(KeyboardLayout layout, byte vk)
    {
        if (VirtualKeys.IsNumpadDigit(vk) || vk == VirtualKeys.Decimal || vk == VirtualKeys.Cancel) return true;
        if (layout.ScanCodes.Normal.Any(t => t.VirtualKey == vk)) return true;
        return layout.ScanCodes.E0.Values.Any(t => t.VirtualKey == vk) || layout.ScanCodes.E1.Values.Any(t => t.VirtualKey == vk);
    }

    private static IEnumerable<List<char>> FindChainCycles(KeyboardLayout layout)
    {
        var graph = layout.DeadKeys.ToDictionary(d => d.Accent, d => d.Entries.Where(e => e.Chained).Select(e => e.Result).Distinct().ToList());
        var reported = new HashSet<string>();
        foreach (var start in graph.Keys)
        {
            var stack = new List<char> { start };
            foreach (var cycle in Walk(start, stack))
            {
                var keyText = new string([.. cycle.OrderBy(c => c)]);
                if (reported.Add(keyText)) yield return cycle;
            }
        }

        IEnumerable<List<char>> Walk(char node, List<char> path)
        {
            if (!graph.TryGetValue(node, out var next)) yield break;
            foreach (var n in next)
            {
                var idx = path.IndexOf(n);
                if (idx >= 0)
                {
                    yield return [.. path.Skip(idx), n];
                    continue;
                }
                path.Add(n);
                foreach (var c in Walk(n, path)) yield return c;
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    private static bool IsKnownLocale(string locale)
    {
        try
        {
            var c = CultureInfo.GetCultureInfo(locale, predefinedOnly: true);
            return !string.IsNullOrEmpty(c.Name);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static string Show(char c) =>
        char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpaceSeparator
            ? $"U+{(int)c:X4}" : $"'{c}' (U+{(int)c:X4})";
}
