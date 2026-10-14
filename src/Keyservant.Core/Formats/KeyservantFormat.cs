using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Keyservant.Core.Model;

namespace Keyservant.Core.Formats;

/// <summary>
/// The canonical .keyservant format: UTF-8 JSON, schema-versioned, laid out so each key and each
/// dead-key composition sits on its own line for readable Git diffs. See docs/file-format.md.
/// </summary>
public sealed class KeyservantFormat : ILayoutFormat
{
    public const string FormatName = "keyservant-layout";
    public const int CurrentSchemaVersion = 1;

    public string Id => "keyservant";
    public string DisplayName => "Keyservant layout";
    public IReadOnlyList<string> Extensions { get; } = [".keyservant"];
    public bool CanRead => true;
    public bool CanWrite => true;

    public KeyboardLayout Read(Stream input, ConversionReport report)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd(), report);
    }

    public void Write(KeyboardLayout layout, Stream output, ConversionReport report)
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Serialize(layout));
        output.Write(bytes);
    }

    // ---------------------------------------------------------------- writing

    public static string Serialize(KeyboardLayout layout)
    {
        var root = new JsonObject
        {
            ["format"] = FormatName,
            ["schemaVersion"] = CurrentSchemaVersion,
            ["metadata"] = WriteMetadata(layout.Metadata),
            ["flags"] = WriteFlags(layout.Flags),
            ["kbdVersion"] = (int)layout.KbdVersion,
            ["keyboardType"] = (long)layout.KeyboardType,
            ["keyboardSubType"] = (long)layout.KeyboardSubType,
            ["modifiers"] = WriteModifiers(layout.Modifiers),
            ["scanCodes"] = WriteScanCodes(layout.ScanCodes),
            ["keys"] = WriteKeys(layout),
            ["deadKeys"] = WriteDeadKeys(layout.DeadKeys),
            ["keyNames"] = WriteNames(layout.KeyNames),
            ["keyNamesExt"] = WriteNames(layout.KeyNamesExt),
        };
        if (layout.Compose is { } compose) root["compose"] = WriteCompose(compose);
        if (layout.Tests.Count > 0) root["tests"] = WriteTests(layout.Tests);
        return CompactJsonWriter.Write(root);
    }

    private static JsonObject WriteMetadata(LayoutMetadata m)
    {
        var o = new JsonObject { ["name"] = m.Name };
        void Opt(string key, string? value) { if (!string.IsNullOrEmpty(value)) o[key] = value; }
        Opt("description", m.Description);
        Opt("author", m.Author);
        Opt("company", m.Company);
        Opt("copyright", m.Copyright);
        o["version"] = m.Version;
        o["locale"] = m.Locale;
        o["dllName"] = m.DllName;
        Opt("layoutId", m.LayoutId);
        Opt("license", m.License);
        return o;
    }

    private static JsonArray WriteFlags(LocaleFlags flags)
    {
        var a = new JsonArray();
        if (flags.HasFlag(LocaleFlags.AltGr)) a.Append("altgr");
        if (flags.HasFlag(LocaleFlags.ShiftLock)) a.Append("shiftlock");
        if (flags.HasFlag(LocaleFlags.LrmRlm)) a.Append("lrm-rlm");
        return a;
    }

    private static JsonObject WriteModifiers(ModifierTable t)
    {
        var bits = new JsonObject();
        foreach (var v in t.VkToBits) bits[VirtualKeys.GetName(v.VirtualKey)] = new ShiftState(v.Bits).Name;
        var columns = new JsonArray();
        for (var c = 0; c < t.ColumnCount; c++)
        {
            var states = t.StatesOf(c);
            if (states.Count == 1) columns.Append(states[0].Name);
            else columns.Append(new JsonArray([.. states.Select(s => (JsonNode)s.Name)]));
        }
        return new JsonObject
        {
            ["vkToBits"] = bits,
            ["columns"] = columns,
            ["maxModBits"] = t.ModNumber.Count - 1,
        };
    }

    private static JsonObject WriteScanCodes(ScanCodeMap m)
    {
        var normal = new JsonObject();
        for (var i = 0; i < m.Normal.Count; i++)
        {
            if (!m.Normal[i].IsNone) normal[ScanCodeMap.FormatScan(i)] = m.Normal[i].ToString();
        }
        static JsonObject Prefixed(SortedDictionary<byte, ScanCodeTarget> d)
        {
            var o = new JsonObject();
            foreach (var (sc, t) in d) o[ScanCodeMap.FormatScan(sc)] = t.ToString();
            return o;
        }
        return new JsonObject
        {
            ["count"] = m.Normal.Count,
            ["normal"] = normal,
            ["e0"] = Prefixed(m.E0),
            ["e1"] = Prefixed(m.E1),
        };
    }

    private static JsonArray WriteKeys(KeyboardLayout layout)
    {
        var keys = new JsonArray();
        foreach (var k in layout.Keys)
        {
            var o = new JsonObject { ["vk"] = k.Name };
            var attrs = WriteAttributes(k.Attributes & ~KeyAttributes.SgCaps);
            if (attrs.Count > 0) o["attrs"] = attrs;
            foreach (var (state, output) in OrderedOutputs(layout, k.Outputs)) o[state.Name] = WriteOutput(output);
            if (k.CapsOutputs is { } caps)
            {
                var c = new JsonObject();
                foreach (var (state, output) in OrderedOutputs(layout, caps)) c[state.Name] = WriteOutput(output);
                o["caps"] = c;
            }
            keys.Append(o);
        }
        return keys;
    }

    private static IEnumerable<KeyValuePair<ShiftState, KeyOutput>> OrderedOutputs(
        KeyboardLayout layout, SortedDictionary<ShiftState, KeyOutput> outputs) =>
        outputs.OrderBy(p => layout.Modifiers.ColumnOf(p.Key) ?? 1000 + p.Key.Value);

    private static JsonArray WriteAttributes(KeyAttributes a)
    {
        var arr = new JsonArray();
        if (a.HasFlag(KeyAttributes.CapsLock)) arr.Append("capslock");
        if (a.HasFlag(KeyAttributes.CapsLockAltGr)) arr.Append("capslock-altgr");
        if (a.HasFlag(KeyAttributes.KanaLock)) arr.Append("kanalock");
        if (a.HasFlag(KeyAttributes.GrpSelTap)) arr.Append("grpseltap");
        return arr;
    }

    private static JsonNode WriteOutput(KeyOutput o) =>
        o.IsDead ? new JsonObject { ["dead"] = o.Text } : JsonValue.Create(o.Text);

    private static JsonArray WriteDeadKeys(List<DeadKeyDefinition> deadKeys)
    {
        var arr = new JsonArray();
        foreach (var d in deadKeys)
        {
            var o = new JsonObject { ["accent"] = d.Accent.ToString() };
            if (!string.IsNullOrEmpty(d.Name)) o["name"] = d.Name;
            var entries = new JsonArray();
            foreach (var e in d.Entries)
            {
                var entry = new JsonArray(e.Base.ToString(), e.Result.ToString());
                if (e.Chained) entry.Append("chained");
                entries.Append(entry);
            }
            o["entries"] = entries;
            arr.Append(o);
        }
        return arr;
    }

    private static JsonObject WriteCompose(Keyservant.Core.Compose.ComposeDefinition c)
    {
        var sequences = new JsonArray();
        foreach (var seq in c.Sequences) sequences.Append(new JsonArray(seq.Keys, seq.Result));
        return new JsonObject
        {
            ["key"] = c.KeyText,
            ["accent"] = c.Accent.ToString(),
            ["sequences"] = sequences,
        };
    }

    private static JsonArray WriteTests(List<LayoutTestCase> tests)
    {
        var arr = new JsonArray();
        foreach (var t in tests)
        {
            var o = new JsonObject();
            if (!string.IsNullOrEmpty(t.Name)) o["name"] = t.Name;
            o["keys"] = t.Keys;
            o["expect"] = t.Expect;
            arr.Append(o);
        }
        return arr;
    }

    private static JsonObject WriteNames(SortedDictionary<byte, string> names)
    {
        var o = new JsonObject();
        foreach (var (sc, name) in names) o[ScanCodeMap.FormatScan(sc)] = name;
        return o;
    }

    // ---------------------------------------------------------------- reading

    public static KeyboardLayout Parse(string json, ConversionReport? report = null)
    {
        report ??= new ConversionReport();
        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new LayoutFormatException($"Invalid JSON: {ex.Message}", ex);
        }
        if (rootNode is not JsonObject root) throw new LayoutFormatException("The file must contain a JSON object.");

        if (Str(root, "format") != FormatName)
            throw new LayoutFormatException($"Not a Keyservant layout (expected \"format\": \"{FormatName}\").");
        var version = Int(root, "schemaVersion") ?? throw new LayoutFormatException("Missing \"schemaVersion\".");
        if (version > CurrentSchemaVersion)
            throw new LayoutFormatException($"Schema version {version} is newer than this version of Keyservant supports ({CurrentSchemaVersion}). Please update Keyservant.");

        var layout = new KeyboardLayout
        {
            Metadata = ReadMetadata(Obj(root, "metadata")),
            Flags = ReadFlags(root["flags"] as JsonArray),
            KbdVersion = (ushort)(Int(root, "kbdVersion") ?? 1),
            KeyboardType = (uint)(Long(root, "keyboardType") ?? 4),
            KeyboardSubType = (uint)(Long(root, "keyboardSubType") ?? 0),
            Modifiers = ReadModifiers(Obj(root, "modifiers")),
            ScanCodes = ReadScanCodes(Obj(root, "scanCodes")),
        };

        if (root["keys"] is JsonArray keys)
        {
            var i = 0;
            foreach (var node in keys)
            {
                layout.Keys.Add(ReadKey(node as JsonObject ?? throw Error($"keys[{i}] must be an object."), i));
                i++;
            }
        }
        if (root["deadKeys"] is JsonArray dead)
        {
            var i = 0;
            foreach (var node in dead) layout.DeadKeys.Add(ReadDeadKey(node as JsonObject ?? throw Error($"deadKeys[{i}] must be an object."), i++));
        }
        ReadNames(root["keyNames"] as JsonObject, layout.KeyNames, "keyNames");
        ReadNames(root["keyNamesExt"] as JsonObject, layout.KeyNamesExt, "keyNamesExt");

        if (root["compose"] is JsonObject compose) layout.Compose = ReadCompose(compose);
        if (root["tests"] is JsonArray tests)
        {
            var i = 0;
            foreach (var node in tests)
            {
                if (node is not JsonObject t || Str(t, "keys") is not { } testKeys || Str(t, "expect") is not { } expect)
                    throw Error($"tests[{i}] needs \"keys\" and \"expect\".");
                layout.Tests.Add(new LayoutTestCase(testKeys, expect, Str(t, "name")));
                i++;
            }
        }

        layout.NormalizeShiftStates();

        string[] known = ["format", "schemaVersion", "metadata", "flags", "kbdVersion", "keyboardType", "keyboardSubType",
            "modifiers", "scanCodes", "keys", "deadKeys", "keyNames", "keyNamesExt", "compose", "tests"];
        foreach (var (name, _) in root)
        {
            if (!known.Contains(name)) report.Warn($"Ignored unknown property \"{name}\".");
        }
        return layout;
    }

    private static LayoutMetadata ReadMetadata(JsonObject? o)
    {
        var m = new LayoutMetadata();
        if (o is null) return m;
        m.Name = Str(o, "name") ?? m.Name;
        m.Description = Str(o, "description");
        m.Author = Str(o, "author");
        m.Company = Str(o, "company");
        m.Copyright = Str(o, "copyright");
        m.Version = Str(o, "version") ?? m.Version;
        m.Locale = Str(o, "locale") ?? m.Locale;
        m.DllName = Str(o, "dllName") ?? m.DllName;
        m.LayoutId = Str(o, "layoutId");
        m.License = Str(o, "license");
        return m;
    }

    private static LocaleFlags ReadFlags(JsonArray? a)
    {
        var f = LocaleFlags.None;
        if (a is null) return f;
        foreach (var n in a)
        {
            f |= n?.GetValue<string>() switch
            {
                "altgr" => LocaleFlags.AltGr,
                "shiftlock" => LocaleFlags.ShiftLock,
                "lrm-rlm" => LocaleFlags.LrmRlm,
                var other => throw Error($"Unknown flag \"{other}\"."),
            };
        }
        return f;
    }

    private static ModifierTable ReadModifiers(JsonObject? o)
    {
        if (o is null) return ModifierTable.CreateStandard();
        var t = new ModifierTable();
        if (o["vkToBits"] is JsonObject bits)
        {
            foreach (var (vk, value) in bits)
            {
                var state = ParseState(value?.GetValue<string>() ?? "", "modifiers.vkToBits");
                t.VkToBits.Add(new VkModifierBit(ParseVk(vk), state.Bits));
            }
        }
        var max = Int(o, "maxModBits");
        if (o["columns"] is JsonArray columns)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                IEnumerable<string> names = columns[c] switch
                {
                    JsonArray arr => arr.Select(n => n!.GetValue<string>()),
                    JsonValue v => [v.GetValue<string>()],
                    _ => throw Error($"modifiers.columns[{c}] must be a string or an array of strings."),
                };
                foreach (var name in names)
                {
                    var state = ParseState(name, "modifiers.columns");
                    while (t.ModNumber.Count <= state.Value) t.ModNumber.Add(ModifierTable.Invalid);
                    if (t.ModNumber[state.Value] != ModifierTable.Invalid) throw Error($"Shift state \"{name}\" is listed twice in modifiers.columns.");
                    t.ModNumber[state.Value] = (byte)c;
                }
            }
        }
        if (max is { } m)
        {
            if (m + 1 < t.ModNumber.Count) throw Error("modifiers.maxModBits is smaller than the highest shift state used.");
            while (t.ModNumber.Count <= m) t.ModNumber.Add(ModifierTable.Invalid);
        }
        return t;
    }

    private static ScanCodeMap ReadScanCodes(JsonObject? o)
    {
        var m = new ScanCodeMap();
        if (o is null) return m;
        var count = Int(o, "count") ?? 0;
        for (var i = 0; i < count; i++) m.Normal.Add(ScanCodeTarget.None);
        if (o["normal"] is JsonObject normal)
        {
            foreach (var (sc, value) in normal) m.Set(ParseScan(sc), ParseTarget(value, sc));
        }
        void Prefixed(string name, SortedDictionary<byte, ScanCodeTarget> target)
        {
            if (o[name] is not JsonObject p) return;
            foreach (var (sc, value) in p) target[(byte)ParseScan(sc)] = ParseTarget(value, sc);
        }
        Prefixed("e0", m.E0);
        Prefixed("e1", m.E1);
        return m;
    }

    private static ScanCodeTarget ParseTarget(JsonNode? value, string sc)
    {
        try
        {
            return ScanCodeTarget.Parse(value?.GetValue<string>() ?? "");
        }
        catch (FormatException ex)
        {
            throw Error($"Scan code {sc}: {ex.Message}");
        }
    }

    private static KeyDefinition ReadKey(JsonObject o, int index)
    {
        var vkName = Str(o, "vk") ?? throw Error($"keys[{index}] has no \"vk\".");
        var key = new KeyDefinition(ParseVk(vkName));
        foreach (var (name, value) in o)
        {
            switch (name)
            {
                case "vk":
                    break;
                case "attrs":
                    foreach (var a in value as JsonArray ?? throw Error($"keys[{index}].attrs must be an array."))
                    {
                        key.Attributes |= a?.GetValue<string>() switch
                        {
                            "capslock" => KeyAttributes.CapsLock,
                            "capslock-altgr" => KeyAttributes.CapsLockAltGr,
                            "kanalock" => KeyAttributes.KanaLock,
                            "grpseltap" => KeyAttributes.GrpSelTap,
                            var other => throw Error($"Key {vkName}: unknown attribute \"{other}\"."),
                        };
                    }
                    break;
                case "caps":
                    key.CapsOutputs = [];
                    foreach (var (state, output) in value as JsonObject ?? throw Error($"Key {vkName}: \"caps\" must be an object."))
                        key.CapsOutputs[ParseState(state, $"key {vkName}")] = ReadOutput(output, vkName);
                    break;
                default:
                    key.Outputs[ParseState(name, $"key {vkName}")] = ReadOutput(value, vkName);
                    break;
            }
        }
        if (key.CapsOutputs is not null) key.Attributes |= KeyAttributes.SgCaps;
        return key;
    }

    private static KeyOutput ReadOutput(JsonNode? node, string vkName)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var s) && s.Length > 0:
                return KeyOutput.Of(s);
            case JsonObject o when Str(o, "dead") is { Length: 1 } accent:
                return KeyOutput.Dead(accent[0]);
            default:
                throw Error($"Key {vkName}: an output must be a non-empty string or {{\"dead\": \"<one character>\"}}.");
        }
    }

    private static DeadKeyDefinition ReadDeadKey(JsonObject o, int index)
    {
        var accent = Str(o, "accent");
        if (accent is not { Length: 1 }) throw Error($"deadKeys[{index}].accent must be exactly one UTF-16 character.");
        var d = new DeadKeyDefinition(accent[0]) { Name = Str(o, "name") };
        if (o["entries"] is JsonArray entries)
        {
            foreach (var e in entries)
            {
                if (e is not JsonArray pair || pair.Count is < 2 or > 3)
                    throw Error($"Dead key U+{(int)d.Accent:X4}: each entry must be [\"base\", \"result\"] or [\"base\", \"result\", \"chained\"].");
                var b = pair[0]!.GetValue<string>();
                var r = pair[1]!.GetValue<string>();
                if (b.Length != 1 || r.Length != 1)
                    throw Error($"Dead key U+{(int)d.Accent:X4}: base and result must each be one UTF-16 character (got \"{b}\" → \"{r}\").");
                var chained = pair.Count == 3 && pair[2]!.GetValue<string>() == "chained";
                d.Entries.Add(new DeadKeyEntry(b[0], r[0], chained));
            }
        }
        return d;
    }

    private static Keyservant.Core.Compose.ComposeDefinition ReadCompose(JsonObject o)
    {
        var c = new Keyservant.Core.Compose.ComposeDefinition();
        if (Str(o, "key") is { } key)
        {
            try { (c.ScanCode, c.Extended) = Keyservant.Core.Compose.ComposeDefinition.ParseKey(key); }
            catch (FormatException ex) { throw Error("compose.key: " + ex.Message); }
        }
        if (Str(o, "accent") is { Length: 1 } accent) c.Accent = accent[0];
        if (o["sequences"] is JsonArray seqs)
        {
            foreach (var node in seqs)
            {
                if (node is not JsonArray pair || pair.Count != 2) throw Error("Each compose sequence must be [\"keys\", \"result\"].");
                c.Sequences.Add(new Keyservant.Core.Compose.ComposeSequence(pair[0]!.GetValue<string>(), pair[1]!.GetValue<string>()));
            }
        }
        return c;
    }

    private static void ReadNames(JsonObject? o, SortedDictionary<byte, string> target, string what)
    {
        if (o is null) return;
        foreach (var (sc, value) in o) target[(byte)ParseScan(sc)] = value?.GetValue<string>() ?? throw Error($"{what}.{sc} must be a string.");
    }

    private static byte ParseVk(string name) =>
        VirtualKeys.TryParse(name, out var vk) ? vk : throw Error($"Unknown virtual key \"{name}\".");

    private static ShiftState ParseState(string name, string where) =>
        ShiftState.TryParse(name, out var s) ? s : throw Error($"Unknown shift state \"{name}\" in {where}.");

    private static int ParseScan(string text) =>
        int.TryParse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text,
            NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var sc) && sc is >= 0 and <= 0xFF
            ? sc : throw Error($"Invalid scan code \"{text}\" (expected two hex digits).");

    private static JsonObject? Obj(JsonObject o, string name) => o[name] as JsonObject;

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    private static long? Long(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<long>(out var i) ? i : null;

    private static LayoutFormatException Error(string message) => new(message);
}

/// <summary>A layout file is malformed; the message says where and why.</summary>
public sealed class LayoutFormatException : Exception
{
    public LayoutFormatException(string message) : base(message) { }
    public LayoutFormatException(string message, Exception inner) : base(message, inner) { }
}

internal static class JsonArrayExtensions
{
    /// <summary>Adds a node without going through the reflection-based <c>JsonArray.Add&lt;T&gt;</c>.</summary>
    public static void Append(this JsonArray array, JsonNode? node) => array.Add(node);

    public static void Append(this JsonArray array, string value) => array.Add((JsonNode?)JsonValue.Create(value));
}
