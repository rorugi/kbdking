using System.Globalization;
using System.Text;
using Keyservant.Core.Model;
using Keyservant.Core.Templates;

namespace Keyservant.Core.Formats;

/// <summary>
/// Microsoft Keyboard Layout Creator (MSKLC 1.4) source files. UTF-16 text with sections such as
/// SHIFTSTATE, LAYOUT, LIGATURE, DEADKEY and KEYNAME.
/// </summary>
/// <remarks>
/// .klc files only list character keys. Control keys (Backspace, Enter, ...), the numpad and the
/// scan codes of non-character keys come from the US template, which is what MSKLC does too.
/// </remarks>
public sealed class KlcFormat : ILayoutFormat
{
    public string Id => "klc";
    public string DisplayName => "MSKLC source";
    public IReadOnlyList<string> Extensions { get; } = [".klc"];
    public bool CanRead => true;
    public bool CanWrite => true;

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "KBD", "COPYRIGHT", "COMPANY", "LOCALENAME", "LOCALEID", "VERSION", "SHIFTSTATE", "LAYOUT", "LIGATURE",
        "DEADKEY", "KEYNAME", "KEYNAME_EXT", "KEYNAME_DEAD", "DESCRIPTIONS", "LANGUAGENAMES", "ATTRIBUTES", "ENDKBD",
    };

    // ---------------------------------------------------------------- reading

    public KeyboardLayout Read(Stream input, ConversionReport report)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        return Parse(Decode(ms.ToArray()), report);
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        // UTF-16LE without BOM: every second byte of ASCII text is zero.
        if (bytes.Length >= 4 && bytes[1] == 0 && bytes[3] == 0) return Encoding.Unicode.GetString(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    public static KeyboardLayout Parse(string text, ConversionReport report)
    {
        var layout = LayoutTemplates.UnitedStates();
        layout.Metadata = new LayoutMetadata { Name = "Imported layout" };
        layout.Flags = LocaleFlags.None;
        layout.DeadKeys.Clear();

        // MSKLC rebuilds character keys from the file; keep only the implicit control and numpad keys.
        var template = layout.Clone();
        layout.Keys.RemoveAll(k => !VirtualKeys.IsImplicitControlKey(k.VirtualKey));

        var columns = new List<ShiftState>();
        var ligatures = new List<(byte Vk, int Column, string Text, int Line)>();
        var namesCleared = new HashSet<string>();
        string section = "";
        DeadKeyDefinition? currentDead = null;
        KeyDefinition? lastKey = null;
        var lineNo = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            lineNo++;
            var tokens = Tokenize(StripComment(rawLine));
            if (tokens.Count == 0) continue;
            var head = tokens[0];

            if (Keywords.Contains(head))
            {
                section = head.ToUpperInvariant();
                switch (section)
                {
                    case "KBD":
                        if (tokens.Count > 1) layout.Metadata.DllName = tokens[1];
                        if (tokens.Count > 2) layout.Metadata.Name = tokens[2];
                        break;
                    case "COPYRIGHT": layout.Metadata.Copyright = Arg(tokens); break;
                    case "COMPANY": layout.Metadata.Company = Arg(tokens); break;
                    case "LOCALENAME": layout.Metadata.Locale = Arg(tokens) ?? layout.Metadata.Locale; break;
                    case "VERSION": layout.Metadata.Version = Arg(tokens) ?? "1.0"; break;
                    case "DEADKEY":
                        if (tokens.Count < 2) throw Error(lineNo, "DEADKEY needs the accent as a hex code.");
                        currentDead = layout.GetOrAddDeadKey(ParseHexChar(tokens[1], lineNo));
                        break;
                    case "KEYNAME" or "KEYNAME_EXT" or "KEYNAME_DEAD":
                        if (namesCleared.Add(section))
                        {
                            if (section == "KEYNAME") layout.KeyNames.Clear();
                            if (section == "KEYNAME_EXT") layout.KeyNamesExt.Clear();
                        }
                        break;
                }
                if (section == "ENDKBD") break;
                continue;
            }

            switch (section)
            {
                case "SHIFTSTATE":
                    if (!int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits) || bits is < 0 or > 0xFF)
                        throw Error(lineNo, $"Invalid shift state '{head}'.");
                    columns.Add(new ShiftState((ModifierBits)bits));
                    break;

                case "LAYOUT":
                    if (columns.Count == 0) throw Error(lineNo, "LAYOUT comes before SHIFTSTATE.");
                    if (head == "-1")
                    {
                        // SGCAPS continuation: "-1 -1 0 <caps chars>"
                        if (lastKey is null) throw Error(lineNo, "SGCap continuation line without a key.");
                        lastKey.CapsOutputs = [];
                        for (var c = 0; c < columns.Count && 3 + c < tokens.Count; c++)
                        {
                            var o = ParseOutput(tokens[3 + c], lineNo, out var lig);
                            if (lig) report.Warn($"Line {lineNo}: ligatures in Caps Lock rows are not supported; ignored.");
                            else if (o is not null) lastKey.CapsOutputs[columns[c]] = o;
                        }
                        lastKey.Attributes |= KeyAttributes.SgCaps;
                        break;
                    }
                    if (tokens.Count < 3) throw Error(lineNo, "A LAYOUT line needs: scan code, virtual key, Caps Lock flag, characters.");
                    lastKey = ParseLayoutLine(layout, template, tokens, columns, lineNo);
                    break;

                case "LIGATURE":
                    if (tokens.Count < 3) throw Error(lineNo, "A LIGATURE line needs: virtual key, column, characters.");
                    var lvk = ParseVk(tokens[0], lineNo);
                    if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var col))
                        throw Error(lineNo, $"Invalid ligature column '{tokens[1]}'.");
                    var sb = new StringBuilder();
                    foreach (var t in tokens.Skip(2)) sb.Append(ParseHexString(t, lineNo));
                    ligatures.Add((lvk, col, sb.ToString(), lineNo));
                    break;

                case "DEADKEY":
                    if (currentDead is null || tokens.Count < 2) throw Error(lineNo, "Invalid DEADKEY entry.");
                    var chained = tokens[1].EndsWith('@');
                    currentDead.Set(ParseHexChar(tokens[0], lineNo), ParseHexChar(tokens[1].TrimEnd('@'), lineNo), chained);
                    break;

                case "KEYNAME":
                    layout.KeyNames[(byte)ParseScan(head, lineNo)] = string.Join(' ', tokens.Skip(1));
                    break;
                case "KEYNAME_EXT":
                    layout.KeyNamesExt[(byte)ParseScan(head, lineNo)] = string.Join(' ', tokens.Skip(1));
                    break;
                case "KEYNAME_DEAD":
                    layout.GetOrAddDeadKey(ParseHexChar(head, lineNo)).Name = string.Join(' ', tokens.Skip(1));
                    break;

                case "ATTRIBUTES":
                    layout.Flags |= head.ToUpperInvariant() switch
                    {
                        "ALTGR" => LocaleFlags.AltGr,
                        "SHIFTLOCK" => LocaleFlags.ShiftLock,
                        "LRM_RLM" => LocaleFlags.LrmRlm,
                        _ => LocaleFlags.None,
                    };
                    break;

                case "DESCRIPTIONS" or "LANGUAGENAMES" or "LOCALEID":
                    break;

                default:
                    report.Warn($"Line {lineNo}: ignored text outside a known section.");
                    break;
            }
        }

        if (columns.Count == 0) throw new LayoutFormatException("The file has no SHIFTSTATE section; is it really a .klc file?");

        // Build the modifier table from SHIFTSTATE; control keys need base, shift and ctrl.
        var modifiers = new ModifierTable();
        modifiers.VkToBits.Add(new(VirtualKeys.Shift, ModifierBits.Shift));
        modifiers.VkToBits.Add(new(VirtualKeys.Control, ModifierBits.Ctrl));
        modifiers.VkToBits.Add(new(VirtualKeys.Menu, ModifierBits.Alt));
        foreach (var c in columns) modifiers.EnsureColumn(c);
        foreach (var c in new[] { ShiftState.Base, ShiftState.Shift, ShiftState.Ctrl }) modifiers.EnsureColumn(c);
        layout.Modifiers = modifiers;
        if (columns.Any(c => c.Bits.HasFlag(ModifierBits.AltGr))) layout.Flags |= LocaleFlags.AltGr;

        foreach (var (vk, column, value, line) in ligatures)
        {
            if (column < 0 || column >= columns.Count) throw Error(line, $"Ligature column {column} does not exist.");
            var key = layout.FindKey(vk) ?? throw Error(line, $"Ligature for {VirtualKeys.GetName(vk)}, which is not in LAYOUT.");
            key.Outputs[columns[column]] = KeyOutput.Of(value);
        }
        foreach (var key in layout.Keys)
        {
            foreach (var (state, output) in key.Outputs)
            {
                if (output.Text == LigaturePlaceholder) throw new LayoutFormatException($"Key {key.Name} has %% in state {state} but no LIGATURE entry.");
            }
        }
        layout.NormalizeShiftStates();
        return layout;
    }

    private const string LigaturePlaceholder = "";

    private static KeyDefinition ParseLayoutLine(KeyboardLayout layout, KeyboardLayout template, List<string> tokens,
        List<ShiftState> columns, int lineNo)
    {
        var scToken = tokens[0];
        var vk = ParseVk(tokens[1], lineNo);
        var capToken = tokens[2];

        if (scToken.Length == 4 && scToken.StartsWith("e0", StringComparison.OrdinalIgnoreCase))
        {
            var sc = (byte)ParseScan(scToken[2..], lineNo);
            var existing = layout.ScanCodes.E0.GetValueOrDefault(sc, new ScanCodeTarget(vk, ScanCodeFlags.Ext));
            layout.ScanCodes.E0[sc] = existing with { VirtualKey = vk };
        }
        else
        {
            var sc = ParseScan(scToken, lineNo);
            var existing = template.ScanCodes[sc];
            // Numpad scan codes keep their navigation VK (Num Lock translates them); e.g. "53 DECIMAL".
            if (!existing.Flags.HasFlag(ScanCodeFlags.Numpad))
            {
                var flags = existing.IsNone ? ScanCodeFlags.None : existing.Flags;
                layout.ScanCodes.Set(sc, new ScanCodeTarget(vk, flags));
            }
        }

        var key = layout.GetOrAddKey(vk);
        key.Outputs.Clear();
        key.CapsOutputs = null;
        key.Attributes = capToken.ToUpperInvariant() switch
        {
            "SGCAP" => KeyAttributes.None,
            _ when int.TryParse(capToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap) =>
                (KeyAttributes)(cap & (int)(KeyAttributes.CapsLock | KeyAttributes.CapsLockAltGr)),
            _ => throw Error(lineNo, $"Invalid Caps Lock flag '{capToken}'."),
        };

        for (var c = 0; c < columns.Count && 3 + c < tokens.Count; c++)
        {
            var output = ParseOutput(tokens[3 + c], lineNo, out var isLigature);
            if (isLigature) key.Outputs[columns[c]] = KeyOutput.Of(LigaturePlaceholder);
            else if (output is not null)
            {
                key.Outputs[columns[c]] = output;
                if (output.IsDead) layout.GetOrAddDeadKey(output.Accent);
            }
        }
        return key;
    }

    private static KeyOutput? ParseOutput(string token, int lineNo, out bool isLigature)
    {
        isLigature = false;
        if (token == "-1") return null;
        if (token == "%%") { isLigature = true; return null; }
        var dead = token.Length > 1 && token.EndsWith('@');
        if (dead) token = token[..^1];
        var text = token.Length == 1 ? token : ParseHexString(token, lineNo);
        if (dead)
        {
            if (text.Length != 1) throw Error(lineNo, "A dead key must be a single UTF-16 character.");
            return KeyOutput.Dead(text[0]);
        }
        return KeyOutput.Of(text);
    }

    private static string ParseHexString(string token, int lineNo)
    {
        if (!int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp) || cp is < 0 or > 0x10FFFF)
            throw Error(lineNo, $"Invalid character code '{token}'.");
        return char.ConvertFromUtf32(cp is >= 0xD800 and <= 0xDFFF ? throw Error(lineNo, $"Lone surrogate '{token}'.") : cp);
    }

    private static char ParseHexChar(string token, int lineNo)
    {
        var s = token.Length == 1 ? token : ParseHexString(token, lineNo);
        return s.Length == 1 ? s[0] : throw Error(lineNo, $"'{token}' must be a single UTF-16 character.");
    }

    private static int ParseScan(string token, int lineNo) =>
        int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var sc) && sc is >= 0 and <= 0xFF
            ? sc : throw Error(lineNo, $"Invalid scan code '{token}'.");

    private static byte ParseVk(string token, int lineNo) =>
        VirtualKeys.TryParse(token, out var vk) ? vk : throw Error(lineNo, $"Unknown virtual key '{token}'.");

    private static string? Arg(List<string> tokens) => tokens.Count > 1 ? tokens[1] : null;

    private static string StripComment(string line)
    {
        var inQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') inQuote = !inQuote;
            else if (!inQuote && c == '/' && i + 1 < line.Length && line[i + 1] == '/') return line[..i];
            else if (!inQuote && c == ';' && (i == 0 || char.IsWhiteSpace(line[i - 1])) && IsKeywordLine(line))
                return line[..i];
        }
        return line;
    }

    private static bool IsKeywordLine(string line)
    {
        var first = line.TrimStart().Split([' ', '\t'], 2)[0];
        return Keywords.Contains(first);
    }

    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var i = 0;
        line = line.TrimEnd('\r');
        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length) break;
            if (line[i] == '"')
            {
                var end = line.IndexOf('"', i + 1);
                if (end < 0) end = line.Length;
                tokens.Add(line[(i + 1)..end]);
                i = end + 1;
            }
            else
            {
                var start = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
                tokens.Add(line[start..i]);
            }
        }
        return tokens;
    }

    private static LayoutFormatException Error(int line, string message) => new($"Line {line}: {message}");

    // ---------------------------------------------------------------- writing

    public void Write(KeyboardLayout layout, Stream output, ConversionReport report)
    {
        var text = Serialize(layout, report);
        output.Write([0xFF, 0xFE]);
        output.Write(Encoding.Unicode.GetBytes(text));
    }

    public static string Serialize(KeyboardLayout source, ConversionReport report)
    {
        if (source.Compose is { Sequences.Count: > 0 }) report.Warn("Compose sequences are exported as chained dead keys and a remapped key.");
        if (source.Tests.Count > 0) report.Warn("Layout tests are not stored in .klc files.");
        var layout = Compose.ComposeCompiler.Compile(source).Layout;
        var m = layout.Metadata;
        var sb = new StringBuilder();
        void Line(string s = "") => sb.Append(s).Append("\r\n");
        static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "'", StringComparison.Ordinal) + "\"";

        Line($"KBD\t{m.DllName}\t{Q(m.Name)}");
        Line();
        Line($"COPYRIGHT\t{Q(m.Copyright)}");
        Line();
        Line($"COMPANY\t{Q(m.Company)}");
        Line();
        Line($"LOCALENAME\t{Q(m.Locale)}");
        Line();
        Line($"LOCALEID\t{Q(m.LanguageId.ToString("x8", CultureInfo.InvariantCulture))}");
        Line();
        Line($"VERSION\t{m.Version}");
        Line();

        var columns = layout.Modifiers.Columns;
        for (var c = 0; c < columns.Count; c++)
        {
            if (layout.Modifiers.StatesOf(c).Count > 1)
                report.Warn($"Column {c} is shared by several modifier combinations; .klc keeps only {columns[c].DisplayName}.");
        }
        Line("SHIFTSTATE");
        Line();
        for (var c = 0; c < columns.Count; c++)
        {
            Line($"{columns[c].Value}\t//Column {c + 4}{(columns[c].Bits == 0 ? "" : " : " + columns[c].DisplayName)}");
        }
        Line();

        if (layout.Flags.HasFlag(LocaleFlags.ShiftLock) || layout.Flags.HasFlag(LocaleFlags.LrmRlm))
        {
            Line("ATTRIBUTES");
            if (layout.Flags.HasFlag(LocaleFlags.ShiftLock)) Line("SHIFTLOCK");
            if (layout.Flags.HasFlag(LocaleFlags.LrmRlm)) Line("LRM_RLM");
            Line();
        }

        Line("LAYOUT\t\t;an extra '@' at the end is a dead key");
        Line();
        Line("//SC\tVK_\t\tCap\t" + string.Join('\t', columns.Select(s => s.Value)));
        Line("//--\t----\t\t----\t" + string.Join('\t', columns.Select(_ => "----")));
        Line();

        var ligatures = new List<(byte Vk, int Column, string Text)>();
        var exported = new HashSet<byte>();
        for (var sc = 0; sc < layout.ScanCodes.Normal.Count; sc++)
        {
            var target = layout.ScanCodes.Normal[sc];
            var vk = target.VirtualKey;
            if (target.Flags.HasFlag(ScanCodeFlags.Numpad))
            {
                if (sc != 0x53) continue;
                vk = VirtualKeys.Decimal;
            }
            var key = layout.FindKey(vk);
            if (key is null || VirtualKeys.IsImplicitControlKey(vk) || !exported.Add(vk)) continue;
            WriteKeyLine(sb, layout, key, ScanCodeMap.FormatScan(sc).ToLowerInvariant(), columns, ligatures, report);
        }
        foreach (var (sc, target) in layout.ScanCodes.E0)
        {
            var key = layout.FindKey(target.VirtualKey);
            if (key is null || VirtualKeys.IsImplicitControlKey(target.VirtualKey) || !exported.Add(target.VirtualKey)) continue;
            WriteKeyLine(sb, layout, key, "e0" + ScanCodeMap.FormatScan(sc).ToLowerInvariant(), columns, ligatures, report);
        }
        foreach (var key in layout.Keys)
        {
            if (!exported.Contains(key.VirtualKey) && !VirtualKeys.IsImplicitControlKey(key.VirtualKey))
                report.Warn($"Key {key.Name} has no scan code, so .klc can't contain it.");
            if (VirtualKeys.IsImplicitControlKey(key.VirtualKey) && !IsStandardControlKey(key))
                report.Warn($"Changes to control key {key.Name} are not stored in .klc; MSKLC uses the standard characters.");
        }
        Line();

        if (ligatures.Count > 0)
        {
            Line("LIGATURE");
            Line();
            Line("//VK_\tMod#\tChar0\tChar1\tChar2\tChar3");
            Line("//----\t\t----\t----\t----\t----\t----");
            Line();
            foreach (var (vk, col, text) in ligatures)
            {
                var hex = string.Join('\t', text.Select(c => ((int)c).ToString("x4", CultureInfo.InvariantCulture)));
                Line($"{VirtualKeys.GetName(vk)}\t{col}\t{hex}");
            }
            Line();
        }

        foreach (var d in layout.DeadKeys)
        {
            Line($"DEADKEY\t{Hex(d.Accent)}");
            Line();
            foreach (var e in d.Entries) Line($"{Hex(e.Base)}\t{Hex(e.Result)}{(e.Chained ? "@" : "")}\t// {Printable(e.Base)} -> {Printable(e.Result)}");
            Line();
        }

        static string Name(string s) => s.Any(char.IsWhiteSpace) ? "\"" + s + "\"" : s;
        Line("KEYNAME");
        Line();
        foreach (var (sc, name) in layout.KeyNames) Line($"{sc:x2}\t{Name(name)}");
        Line();
        Line("KEYNAME_EXT");
        Line();
        foreach (var (sc, name) in layout.KeyNamesExt) Line($"{sc:x2}\t{Name(name)}");
        Line();
        if (layout.DeadKeys.Any(d => !string.IsNullOrEmpty(d.Name)))
        {
            Line("KEYNAME_DEAD");
            Line();
            foreach (var d in layout.DeadKeys.Where(d => !string.IsNullOrEmpty(d.Name))) Line($"{Hex(d.Accent)}\t\"{d.Name}\"");
            Line();
        }

        Line("DESCRIPTIONS");
        Line();
        Line($"0409\t{m.Name}");
        Line();
        Line("LANGUAGENAMES");
        Line();
        Line($"0409\t{LanguageDisplayName(m.Locale)}");
        Line();
        Line("ENDKBD");
        return sb.ToString();
    }

    private static void WriteKeyLine(StringBuilder sb, KeyboardLayout layout, KeyDefinition key, string sc,
        IReadOnlyList<ShiftState> columns, List<(byte, int, string)> ligatures, ConversionReport report)
    {
        string cap;
        if (key.CapsOutputs is not null) cap = "SGCap";
        else cap = ((int)(key.Attributes & (KeyAttributes.CapsLock | KeyAttributes.CapsLockAltGr))).ToString(CultureInfo.InvariantCulture);
        if (key.Attributes.HasFlag(KeyAttributes.KanaLock) || key.Attributes.HasFlag(KeyAttributes.GrpSelTap))
            report.Warn($"Key {key.Name}: Kana lock and group-select attributes are not stored in .klc.");

        var cells = new List<string>();
        var comments = new List<string>();
        for (var c = 0; c < columns.Count; c++)
        {
            var o = key.Outputs.GetValueOrDefault(columns[c]);
            cells.Add(Token(o, key.VirtualKey, c, ligatures));
            comments.Add(o is null ? "<none>" : o.IsDead ? $"{Printable(o.Accent)} (dead)" : string.Concat(o.Text.Select(Printable)));
        }
        foreach (var state in key.Outputs.Keys.Where(s => layout.Modifiers.ColumnOf(s) is null))
            report.Warn($"Key {key.Name}: shift state {state} has no column and is not exported.");

        sb.Append(sc).Append('\t').Append(VirtualKeys.GetName(key.VirtualKey)).Append("\t\t").Append(cap).Append('\t')
          .Append(string.Join('\t', cells)).Append("\t// ").Append(string.Join(", ", comments)).Append("\r\n");

        if (key.CapsOutputs is { } caps)
        {
            var capsCells = new List<string>();
            for (var c = 0; c < columns.Count; c++)
            {
                var o = caps.GetValueOrDefault(columns[c]);
                if (o is { IsLigature: true })
                {
                    report.Warn($"Key {key.Name}: ligatures in the Caps Lock row can't be stored in .klc.");
                    o = null;
                }
                capsCells.Add(Token(o, key.VirtualKey, c, null));
            }
            sb.Append("-1\t-1\t\t0\t").Append(string.Join('\t', capsCells)).Append("\r\n");
        }
    }

    private static string Token(KeyOutput? o, byte vk, int column, List<(byte, int, string)>? ligatures)
    {
        if (o is null) return "-1";
        if (o.IsDead) return Hex(o.Accent) + "@";
        if (o.IsLigature)
        {
            ligatures?.Add((vk, column, o.Text));
            return "%%";
        }
        var ch = o.Text[0];
        return char.IsAsciiLetterOrDigit(ch) ? ch.ToString() : Hex(ch);
    }

    private static bool IsStandardControlKey(KeyDefinition key)
    {
        var standard = LayoutTemplates.UnitedStates().FindKey(key.VirtualKey);
        if (standard is null) return true;
        return standard.Outputs.Count == key.Outputs.Count
            && standard.Outputs.All(p => key.Outputs.TryGetValue(p.Key, out var o) && o == p.Value);
    }

    private static string Hex(char c) => ((int)c).ToString("x4", CultureInfo.InvariantCulture);

    private static string Printable(char c) =>
        char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format
            ? $"U+{(int)c:X4}" : c.ToString();

    private static string LanguageDisplayName(string locale)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(locale).EnglishName;
            if (!string.IsNullOrEmpty(name) && !name.StartsWith("Unknown", StringComparison.Ordinal)) return name;
        }
        catch (CultureNotFoundException)
        {
        }
        return locale;
    }
}
