using System.Globalization;
using System.Security;
using System.Text;
using Keyservant.Core.Geometry;
using Keyservant.Core.Model;

namespace Keyservant.Core.Rendering;

public enum RenderMode
{
    /// <summary>The whole keyboard at real size, for printing or a picture of the layout.</summary>
    Keyboard,
    /// <summary>A grid of small labels to print on sticker paper and put on the keys.</summary>
    Stickers,
}

public sealed record RenderOptions
{
    public RenderMode Mode { get; init; } = RenderMode.Keyboard;
    public KeyboardShape Shape { get; init; } = KeyboardShape.Iso;
    public string? Title { get; init; }

    /// <summary>Key presses per virtual key; colours keys from white to red (heatmap).</summary>
    public IReadOnlyDictionary<byte, int>? Heat { get; init; }

    /// <summary>Keys to highlight (e.g. changed keys in a comparison).</summary>
    public IReadOnlySet<byte>? Highlight { get; init; }

    /// <summary>Sticker edge length in millimetres.</summary>
    public double StickerSize { get; init; } = 12;
}

/// <summary>
/// Draws a layout as SVG in millimetres (1 key unit = 19.05 mm), so it prints at real size: the full
/// keyboard, a heatmap, a comparison, or a sheet of key stickers. Each key shows up to four characters:
/// base (bottom left), Shift (top left), AltGr (bottom right) and Shift+AltGr (top right); dead keys in orange.
/// </summary>
public static class KeyboardSvgRenderer
{
    private const double Unit = 19.05;
    private const double Gap = 1.0;
    private const string Font = "Segoe UI, 'Noto Sans', 'Arial Unicode MS', sans-serif";

    public static string Render(KeyboardLayout layout, RenderOptions? options = null)
    {
        options ??= new RenderOptions();
        var effective = Compose.ComposeCompiler.Compile(layout).Layout;
        return options.Mode == RenderMode.Stickers ? Stickers(effective, options) : Keyboard(effective, options);
    }

    private static string Keyboard(KeyboardLayout layout, RenderOptions o)
    {
        var rows = KeyboardGeometry.Rows(o.Shape);
        var width = rows.Max(r => r.Sum(k => k.Width)) * Unit;
        var titleHeight = o.Title is null ? 0 : 10;
        var height = rows.Count * Unit + titleHeight;
        var maxHeat = o.Heat is { Count: > 0 } h ? h.Values.Max() : 0;

        var sb = Header(width + 4, height + 4);
        if (o.Title is not null) Text(sb, 2, 7, 5, o.Title, "start", "#222", bold: true);
        foreach (var row in rows)
        {
            foreach (var key in row)
            {
                var x = 2 + key.X * Unit;
                var y = 2 + titleHeight + key.Row * Unit;
                var vk = VirtualKeyOf(layout, key);
                var fill = "#fafafa";
                if (key.FixedLabel is not null) fill = "#e8e8e8";
                if (key.FixedLabel is null && vk is { } v && o.Heat is not null && maxHeat > 0) fill = HeatColor(o.Heat.GetValueOrDefault(v) / (double)maxHeat);
                if (vk is { } hv && o.Highlight?.Contains(hv) == true) fill = "#cfe3ff";
                sb.Append(Inv($"<rect x=\"{x + Gap / 2:0.##}\" y=\"{y + Gap / 2:0.##}\" width=\"{key.Width * Unit - Gap:0.##}\" height=\"{Unit - Gap:0.##}\" rx=\"1.6\" fill=\"{fill}\" stroke=\"#888\" stroke-width=\"0.3\"/>\n"));
                DrawLabels(sb, layout, key, vk, x, y, key.Width * Unit, Unit, small: false);
                if (vk is { } cv && o.Heat is not null && o.Heat.GetValueOrDefault(cv) is > 0 and var count)
                    Text(sb, x + key.Width * Unit / 2, y + Unit / 2 + 1.2, 2.4, count.ToString(CultureInfo.InvariantCulture), "middle", "#00000080");
            }
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static string Stickers(KeyboardLayout layout, RenderOptions o)
    {
        var keys = KeyboardGeometry.Rows(o.Shape).SelectMany(r => r)
            .Where(k => k.FixedLabel is null && VirtualKeyOf(layout, k) is { } vk && layout.FindKey(vk) is not null && k.ScanCode != 0x39)
            .ToList();
        var size = Math.Clamp(o.StickerSize, 5, 60);
        const double spacing = 3, margin = 10;
        var perRow = (int)Math.Floor((210 - 2 * margin + spacing) / (size + spacing));
        var rowsNeeded = (keys.Count + perRow - 1) / perRow;
        var width = 210.0;
        var height = margin * 2 + 10 + rowsNeeded * (size + spacing);

        var sb = Header(width, height);
        Text(sb, margin, margin + 4, 4.5, (o.Title ?? layout.Metadata.Name) + " — key stickers", "start", "#222", bold: true);
        Text(sb, margin, margin + 8.5, 2.6, "Bottom left: base · top left: Shift · bottom right: AltGr · top right: Shift+AltGr · orange: dead key", "start", "#555");
        for (var i = 0; i < keys.Count; i++)
        {
            var x = margin + i % perRow * (size + spacing);
            var y = margin + 12 + i / perRow * (size + spacing);
            sb.Append(Inv($"<rect x=\"{x:0.##}\" y=\"{y:0.##}\" width=\"{size:0.##}\" height=\"{size:0.##}\" rx=\"1\" fill=\"white\" stroke=\"#aaa\" stroke-width=\"0.2\" stroke-dasharray=\"1,0.6\"/>\n"));
            DrawLabels(sb, layout, keys[i], VirtualKeyOf(layout, keys[i]), x, y, size, size, small: true);
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static void DrawLabels(StringBuilder sb, KeyboardLayout layout, PhysicalKey key, byte? vk, double x, double y, double w, double h, bool small)
    {
        if (key.FixedLabel is not null)
        {
            var label = key.FixedLabel == "AltGr" && !layout.Flags.HasFlag(LocaleFlags.AltGr) ? "Alt" : key.FixedLabel;
            Text(sb, x + w / 2, y + h / 2 + 1.2, 3, label, "middle", "#444");
            return;
        }
        var def = vk is { } v ? layout.FindKey(v) : null;
        if (def is null) return;
        var big = small ? h * 0.36 : 6.0;
        var little = small ? h * 0.26 : 4.2;
        var pad = small ? h * 0.1 : 2.2;
        void Corner(ShiftState state, double cx, double cy, double fs, string anchor)
        {
            var output = layout.Modifiers.Canonicalize(state) is { } c ? def.Outputs.GetValueOrDefault(c) : null;
            if (output is null) return;
            Text(sb, cx, cy, fs, Display(output), anchor, output.IsDead ? "#d9480f" : "#111");
        }
        Corner(ShiftState.Base, x + pad, y + h - pad, big, "start");
        Corner(ShiftState.Shift, x + pad, y + pad + little, little, "start");
        Corner(ShiftState.AltGr, x + w - pad, y + h - pad, little, "end");
        Corner(ShiftState.ShiftAltGr, x + w - pad, y + pad + little, little, "end");
    }

    private static byte? VirtualKeyOf(KeyboardLayout layout, PhysicalKey key)
    {
        var t = key.Extended ? layout.ScanCodes.E0.GetValueOrDefault((byte)key.ScanCode, ScanCodeTarget.None) : layout.ScanCodes[key.ScanCode];
        return t.IsNone ? null : t.VirtualKey;
    }

    /// <summary>Text for a key cap: control characters as code, combining marks on a dotted circle, space as ␣.</summary>
    public static string Display(KeyOutput output)
    {
        var t = output.Text;
        if (t == " ") return "␣";
        if (t.Length == 1)
        {
            var c = t[0];
            if (char.IsControl(c)) return "";
            if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark) return "◌" + c;
        }
        return t;
    }

    private static string HeatColor(double t)
    {
        t = Math.Clamp(t, 0, 1);
        // white → yellow → orange → red
        var r = 255;
        var g = (int)(255 - 175 * t);
        var b = (int)(255 - 235 * Math.Min(1, t * 2));
        return $"#{r:X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";
    }

    private static StringBuilder Header(double w, double h)
    {
        var sb = new StringBuilder();
        sb.Append(Inv($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w:0.##}mm\" height=\"{h:0.##}mm\" viewBox=\"0 0 {w:0.##} {h:0.##}\" font-family=\"{Font}\">\n"));
        sb.Append(Inv($"<rect width=\"{w:0.##}\" height=\"{h:0.##}\" fill=\"white\"/>\n"));
        return sb;
    }

    private static void Text(StringBuilder sb, double x, double y, double size, string text, string anchor, string color, bool bold = false)
    {
        sb.Append(Inv($"<text x=\"{x:0.##}\" y=\"{y:0.##}\" font-size=\"{size:0.##}\" text-anchor=\"{anchor}\" fill=\"{color}\"{(bold ? " font-weight=\"600\"" : "")}>"))
          .Append(SecurityElement.Escape(XmlSafe(text))).Append("</text>\n");
    }

    /// <summary>Replaces characters XML 1.0 can't contain (most control characters, unpaired surrogates) with U+FFFD.</summary>
    private static string XmlSafe(string text)
    {
        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var ok = c is '\t' or '\n' or '\r' || c >= ' ' && c is not ('\uFFFE' or '\uFFFF') && !char.IsSurrogate(c);
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb?.Append(c).Append(text[i + 1]);
                i++;
                continue;
            }
            if (!ok) sb ??= new StringBuilder(text, 0, i, text.Length);
            sb?.Append(ok ? c : '\uFFFD');
        }
        return sb?.ToString() ?? text;
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
