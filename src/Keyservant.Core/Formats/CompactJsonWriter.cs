using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Keyservant.Core.Formats;

/// <summary>
/// Writes JSON for people and for Git: objects and arrays that fit on one line are kept on one line,
/// everything else is indented. Characters that are hard to see in a diff (control characters,
/// combining marks, format and private-use characters) are written as \u escapes; everything else
/// is written literally.
/// </summary>
internal static class CompactJsonWriter
{
    private const int MaxLineLength = 110;

    public static string Write(JsonNode node)
    {
        var sb = new StringBuilder();
        WriteNode(sb, node, 0);
        sb.Append('\n');
        return sb.ToString();
    }

    private static void WriteNode(StringBuilder sb, JsonNode? node, int indent)
    {
        if (node is JsonObject or JsonArray)
        {
            var compact = Compact(node);
            if (indent > 0 && compact.Length + indent * 2 <= MaxLineLength && !compact.Contains('\n'))
            {
                sb.Append(compact);
                return;
            }
        }

        switch (node)
        {
            case JsonObject obj:
                if (obj.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                var i = 0;
                foreach (var (name, value) in obj)
                {
                    Indent(sb, indent + 1);
                    WriteString(sb, name);
                    sb.Append(": ");
                    WriteNode(sb, value, indent + 1);
                    if (++i < obj.Count) sb.Append(',');
                    sb.Append('\n');
                }
                Indent(sb, indent);
                sb.Append('}');
                break;
            case JsonArray arr:
                if (arr.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (var j = 0; j < arr.Count; j++)
                {
                    Indent(sb, indent + 1);
                    WriteNode(sb, arr[j], indent + 1);
                    if (j < arr.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                Indent(sb, indent);
                sb.Append(']');
                break;
            default:
                WriteScalar(sb, node);
                break;
        }
    }

    private static string Compact(JsonNode node)
    {
        var sb = new StringBuilder();
        WriteCompact(sb, node);
        return sb.ToString();
    }

    private static void WriteCompact(StringBuilder sb, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                sb.Append('{');
                var i = 0;
                foreach (var (name, value) in obj)
                {
                    if (i++ > 0) sb.Append(", ");
                    WriteString(sb, name);
                    sb.Append(": ");
                    WriteCompact(sb, value);
                }
                sb.Append('}');
                break;
            case JsonArray arr:
                sb.Append('[');
                for (var j = 0; j < arr.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    WriteCompact(sb, arr[j]);
                }
                sb.Append(']');
                break;
            default:
                WriteScalar(sb, node);
                break;
        }
    }

    private static void WriteScalar(StringBuilder sb, JsonNode? node)
    {
        if (node is null) { sb.Append("null"); return; }
        var value = node.AsValue();
        if (value.TryGetValue<string>(out var s)) WriteString(sb, s);
        else if (value.TryGetValue<bool>(out var b)) sb.Append(b ? "true" : "false");
        else if (value.TryGetValue<int>(out var n)) sb.Append(n.ToString(CultureInfo.InvariantCulture));
        else if (value.TryGetValue<long>(out var l)) sb.Append(l.ToString(CultureInfo.InvariantCulture));
        else if (value.TryGetValue<uint>(out var u)) sb.Append(u.ToString(CultureInfo.InvariantCulture));
        else if (value.TryGetValue<double>(out var d)) sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        else sb.Append(node.ToJsonString());
    }

    internal static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (NeedsEscape(c)) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private static bool NeedsEscape(char c) =>
        char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format
            or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
        || c == ' ' || c == '­';

    private static void Indent(StringBuilder sb, int level) => sb.Append(' ', level * 2);
}
