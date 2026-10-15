using Kbdking.Core.Model;

namespace Kbdking.Core.Templates;

/// <summary>A built-in starting point for new layouts.</summary>
public sealed record LayoutTemplate(string Id, string DisplayName, Func<KeyboardLayout> Create);

/// <summary>
/// Built-in layouts used as starting points and as the base for .klc imports. They follow the standard
/// Windows layouts closely but are written from scratch; to start from the exact Windows layout,
/// import its DLL from System32 instead.
/// </summary>
public static class LayoutTemplates
{
    private sealed record DeadMarker(char Accent);

    private static DeadMarker Dead(char accent) => new(accent);

    public static IReadOnlyList<LayoutTemplate> All { get; } =
    [
        new("en-US", "English (United States)", UnitedStates),
        new("de-DE", "German (Germany)", () => German("de-DE")),
        new("de-AT", "German (Austria)", () => German("de-AT")),
    ];

    public static KeyboardLayout Create(string id) =>
        All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Create()
        ?? throw new ArgumentException($"Unknown template '{id}'. Available: {string.Join(", ", All.Select(t => t.Id))}.", nameof(id));

    /// <summary>US English (QWERTY), the equivalent of KBDUS.DLL.</summary>
    public static KeyboardLayout UnitedStates()
    {
        var l = new KeyboardLayout
        {
            Flags = LocaleFlags.None,
            Modifiers = ModifierTable.CreateStandard(withAltGr: false, withShiftCtrl: true),
            ScanCodes = Standard101ScanCodes(),
        };
        l.Metadata.Name = "US";
        l.Metadata.Locale = "en-US";
        l.Metadata.DllName = "kbdks";
        ShiftState[] cols = [ShiftState.Base, ShiftState.Shift, ShiftState.Ctrl, ShiftState.ShiftCtrl];

        void Row(byte vk, KeyAttributes a, params object?[] outs) => AddRow(l, cols, vk, a, outs);

        Row((byte)'1', 0, "1", "!");
        Row((byte)'2', 0, "2", "@", null, "\0");
        Row((byte)'3', 0, "3", "#");
        Row((byte)'4', 0, "4", "$");
        Row((byte)'5', 0, "5", "%");
        Row((byte)'6', 0, "6", "^", null, "\u001e");
        Row((byte)'7', 0, "7", "&");
        Row((byte)'8', 0, "8", "*");
        Row((byte)'9', 0, "9", "(");
        Row((byte)'0', 0, "0", ")");
        Row(0xBD /*OEM_MINUS*/, 0, "-", "_", null, "\u001f");
        Row(0xBB /*OEM_PLUS*/, 0, "=", "+");
        AddLetters(l, cols, "QWERTYUIOP");
        Row(0xDB /*OEM_4*/, 0, "[", "{", "\u001b");
        Row(0xDD /*OEM_6*/, 0, "]", "}", "\u001d");
        AddLetters(l, cols, "ASDFGHJKL");
        Row(0xBA /*OEM_1*/, 0, ";", ":");
        Row(0xDE /*OEM_7*/, 0, "'", "\"");
        Row(0xC0 /*OEM_3*/, 0, "`", "~");
        Row(0xDC /*OEM_5*/, 0, "\\", "|", "\u001c");
        AddLetters(l, cols, "ZXCVBNM");
        Row(0xBC /*OEM_COMMA*/, 0, ",", "<");
        Row(0xBE /*OEM_PERIOD*/, 0, ".", ">");
        Row(0xBF /*OEM_2*/, 0, "/", "?");
        Row(0xE2 /*OEM_102*/, 0, "\\", "|", "\u001c");
        Row(VirtualKeys.Space, 0, " ", " ", " ");
        Row(VirtualKeys.Decimal, 0, ".", ".");
        AddControlAndNumpad(l, cols);
        AddEnglishKeyNames(l);
        return l;
    }

    /// <summary>German (QWERTZ), the equivalent of KBDGR.DLL. Germany and Austria use the same layout.</summary>
    public static KeyboardLayout German(string locale = "de-DE")
    {
        var l = new KeyboardLayout
        {
            Flags = LocaleFlags.AltGr,
            Modifiers = ModifierTable.CreateStandard(withAltGr: false, withShiftCtrl: true),
            ScanCodes = Standard101ScanCodes(),
        };
        l.Modifiers.EnsureColumn(ShiftState.AltGr);
        l.Metadata.Name = locale.Equals("de-AT", StringComparison.OrdinalIgnoreCase) ? "German (Austria)" : "German";
        l.Metadata.Locale = locale;
        l.Metadata.DllName = "kbdks";

        // QWERTZ positions of the keys that differ from US.
        var sc = l.ScanCodes;
        sc.Set(0x0C, new(0xDB, 0)); // ß    OEM_4
        sc.Set(0x0D, new(0xDD, 0)); // ´    OEM_6
        sc.Set(0x15, new((byte)'Z', 0));
        sc.Set(0x1A, new(0xBA, 0)); // ü    OEM_1
        sc.Set(0x1B, new(0xBB, 0)); // +    OEM_PLUS
        sc.Set(0x27, new(0xC0, 0)); // ö    OEM_3
        sc.Set(0x28, new(0xDE, 0)); // ä    OEM_7
        sc.Set(0x29, new(0xDC, 0)); // ^    OEM_5
        sc.Set(0x2B, new(0xBF, 0)); // #    OEM_2
        sc.Set(0x2C, new((byte)'Y', 0));
        sc.Set(0x35, new(0xBD, 0)); // -    OEM_MINUS

        ShiftState[] cols = [ShiftState.Base, ShiftState.Shift, ShiftState.Ctrl, ShiftState.ShiftCtrl, ShiftState.AltGr];

        void Row(byte vk, KeyAttributes a, params object?[] outs) => AddRow(l, cols, vk, a, outs);

        Row((byte)'1', 0, "1", "!");
        Row((byte)'2', 0, "2", "\"", null, "\0", "²");
        Row((byte)'3', 0, "3", "§", null, null, "³");
        Row((byte)'4', 0, "4", "$");
        Row((byte)'5', 0, "5", "%");
        Row((byte)'6', 0, "6", "&", null, "\u001e");
        Row((byte)'7', 0, "7", "/", null, null, "{");
        Row((byte)'8', 0, "8", "(", null, null, "[");
        Row((byte)'9', 0, "9", ")", null, null, "]");
        Row((byte)'0', 0, "0", "=", null, null, "}");
        Row(0xDB /*OEM_4 ß*/, 0, "ß", "?", null, null, "\\");
        Row(0xDD /*OEM_6 ´*/, 0, Dead('´'), Dead('`'));
        Row((byte)'Q', KeyAttributes.CapsLock, "q", "Q", null, null, "@");
        Row((byte)'W', KeyAttributes.CapsLock, "w", "W");
        Row((byte)'E', KeyAttributes.CapsLock, "e", "E", null, null, "€");
        AddLetters(l, cols, "RTZUIOP");
        Row(0xBA /*OEM_1 ü*/, KeyAttributes.CapsLock, "ü", "Ü", "\u001b");
        Row(0xBB /*OEM_PLUS +*/, 0, "+", "*", "\u001d", null, "~");
        AddLetters(l, cols, "ASDFGHJKL");
        Row(0xC0 /*OEM_3 ö*/, KeyAttributes.CapsLock, "ö", "Ö");
        Row(0xDE /*OEM_7 ä*/, KeyAttributes.CapsLock, "ä", "Ä");
        Row(0xDC /*OEM_5 ^*/, 0, Dead('^'), "°", "\u001c");
        Row(0xBF /*OEM_2 #*/, 0, "#", "'", "\u001c");
        Row(0xE2 /*OEM_102 <*/, 0, "<", ">", "\u001c", null, "|");
        AddLetters(l, cols, "YXCVBN");
        Row((byte)'M', KeyAttributes.CapsLock, "m", "M", null, null, "µ");
        Row(0xBC /*OEM_COMMA*/, 0, ",", ";");
        Row(0xBE /*OEM_PERIOD*/, 0, ".", ":");
        Row(0xBD /*OEM_MINUS*/, 0, "-", "_", null, "\u001f");
        Row(VirtualKeys.Space, 0, " ", " ", " ");
        Row(VirtualKeys.Decimal, 0, ",", ",");
        AddControlAndNumpad(l, cols);

        var circumflex = l.GetOrAddDeadKey('^');
        circumflex.Name = "ZIRKUMFLEX";
        AddCompositions(circumflex, "aâAÂeêEÊiîIÎoôOÔuûUÛ ^");
        var acute = l.GetOrAddDeadKey('´');
        acute.Name = "AKUT";
        AddCompositions(acute, "aáAÁeéEÉiíIÍoóOÓuúUÚyýYÝ ´");
        var grave = l.GetOrAddDeadKey('`');
        grave.Name = "GRAVIS";
        AddCompositions(grave, "aàAÀeèEÈiìIÌoòOÒuùUÙ `");

        AddGermanKeyNames(l);
        return l;
    }

    private static void AddCompositions(DeadKeyDefinition d, string pairs)
    {
        for (var i = 0; i + 1 < pairs.Length; i += 2) d.Set(pairs[i], pairs[i + 1]);
    }

    private static void AddRow(KeyboardLayout l, ShiftState[] cols, byte vk, KeyAttributes a, object?[] outs)
    {
        var key = l.GetOrAddKey(vk);
        key.Attributes = a;
        for (var i = 0; i < outs.Length && i < cols.Length; i++)
        {
            KeyOutput? o = outs[i] switch
            {
                null => null,
                string s => KeyOutput.Of(s),
                DeadMarker d => KeyOutput.Dead(d.Accent),
                _ => throw new ArgumentException("Unsupported output."),
            };
            if (o is not null) l.SetOutput(vk, cols[i], o);
        }
    }

    private static void AddLetters(KeyboardLayout l, ShiftState[] cols, string letters)
    {
        foreach (var c in letters)
        {
            if (l.FindKey((byte)c) is not null) continue;
            AddRow(l, cols, (byte)c, KeyAttributes.CapsLock, [char.ToLowerInvariant(c).ToString(), c.ToString()]);
        }
    }

    private static void AddControlAndNumpad(KeyboardLayout l, ShiftState[] cols)
    {
        AddRow(l, cols, VirtualKeys.Tab, 0, ["\t", "\t"]);
        AddRow(l, cols, VirtualKeys.Add, 0, ["+", "+"]);
        AddRow(l, cols, VirtualKeys.Divide, 0, ["/", "/"]);
        AddRow(l, cols, VirtualKeys.Multiply, 0, ["*", "*"]);
        AddRow(l, cols, VirtualKeys.Subtract, 0, ["-", "-"]);
        AddRow(l, cols, VirtualKeys.Back, 0, ["\b", "\b", "\u007f"]);
        AddRow(l, cols, VirtualKeys.Escape, 0, ["\u001b", "\u001b", "\u001b"]);
        AddRow(l, cols, VirtualKeys.Return, 0, ["\r", "\r", "\n"]);
        AddRow(l, cols, VirtualKeys.Cancel, 0, ["\u0003", "\u0003", "\u0003"]);
        for (var i = 0; i <= 9; i++)
        {
            AddRow(l, cols, (byte)(VirtualKeys.Numpad0 + i), 0, [((char)('0' + i)).ToString()]);
        }
    }

    /// <summary>Scan code → virtual key mapping of a standard 101/102-key keyboard (kbd.h keyboard type 4).</summary>
    public static ScanCodeMap Standard101ScanCodes()
    {
        const ScanCodeFlags Ext = ScanCodeFlags.Ext, Multi = ScanCodeFlags.MultiVk,
            NumSpecial = ScanCodeFlags.Numpad | ScanCodeFlags.Special;
        var m = new ScanCodeMap();
        void S(int sc, string vk, ScanCodeFlags f = 0) => m.Set(sc, new ScanCodeTarget(VirtualKeys.Parse(vk), f));

        for (var i = 0; i < 0x80; i++) m.Set(i, ScanCodeTarget.None);
        S(0x01, "ESCAPE");
        for (var i = 0; i < 9; i++) S(0x02 + i, ((char)('1' + i)).ToString());
        S(0x0B, "0"); S(0x0C, "OEM_MINUS"); S(0x0D, "OEM_PLUS"); S(0x0E, "BACK"); S(0x0F, "TAB");
        var row1 = "QWERTYUIOP";
        for (var i = 0; i < row1.Length; i++) S(0x10 + i, row1[i].ToString());
        S(0x1A, "OEM_4"); S(0x1B, "OEM_6"); S(0x1C, "RETURN"); S(0x1D, "LCONTROL");
        var row2 = "ASDFGHJKL";
        for (var i = 0; i < row2.Length; i++) S(0x1E + i, row2[i].ToString());
        S(0x27, "OEM_1"); S(0x28, "OEM_7"); S(0x29, "OEM_3"); S(0x2A, "LSHIFT"); S(0x2B, "OEM_5");
        var row3 = "ZXCVBNM";
        for (var i = 0; i < row3.Length; i++) S(0x2C + i, row3[i].ToString());
        S(0x33, "OEM_COMMA"); S(0x34, "OEM_PERIOD"); S(0x35, "OEM_2"); S(0x36, "RSHIFT", Ext);
        S(0x37, "MULTIPLY", Multi); S(0x38, "LMENU"); S(0x39, "SPACE"); S(0x3A, "CAPITAL");
        for (var i = 0; i < 10; i++) S(0x3B + i, "F" + (i + 1));
        S(0x45, "NUMLOCK", Ext | Multi); S(0x46, "SCROLL", Multi);
        S(0x47, "HOME", NumSpecial); S(0x48, "UP", NumSpecial); S(0x49, "PRIOR", NumSpecial); S(0x4A, "SUBTRACT");
        S(0x4B, "LEFT", NumSpecial); S(0x4C, "CLEAR", NumSpecial); S(0x4D, "RIGHT", NumSpecial); S(0x4E, "ADD");
        S(0x4F, "END", NumSpecial); S(0x50, "DOWN", NumSpecial); S(0x51, "NEXT", NumSpecial);
        S(0x52, "INSERT", NumSpecial); S(0x53, "DELETE", NumSpecial);
        S(0x54, "SNAPSHOT"); S(0x56, "OEM_102"); S(0x57, "F11"); S(0x58, "F12"); S(0x59, "CLEAR");
        S(0x5A, "OEM_WSCTRL"); S(0x5B, "OEM_FINISH"); S(0x5C, "OEM_JUMP"); S(0x5D, "EREOF");
        S(0x5E, "OEM_BACKTAB"); S(0x5F, "OEM_AUTO"); S(0x62, "ZOOM"); S(0x63, "HELP");
        for (var i = 0; i < 11; i++) S(0x64 + i, "F" + (13 + i));
        S(0x6F, "OEM_PA3"); S(0x71, "OEM_RESET"); S(0x73, "ABNT_C1"); S(0x76, "F24"); S(0x7B, "OEM_PA1");
        S(0x7C, "TAB"); S(0x7E, "ABNT_C2"); S(0x7F, "OEM_PA2");

        void E0(byte sc, string vk) => m.E0[sc] = new ScanCodeTarget(VirtualKeys.Parse(vk), Ext);
        E0(0x10, "MEDIA_PREV_TRACK"); E0(0x19, "MEDIA_NEXT_TRACK"); E0(0x1C, "RETURN"); E0(0x1D, "RCONTROL");
        E0(0x20, "VOLUME_MUTE"); E0(0x21, "LAUNCH_APP2"); E0(0x22, "MEDIA_PLAY_PAUSE"); E0(0x24, "MEDIA_STOP");
        E0(0x2E, "VOLUME_DOWN"); E0(0x30, "VOLUME_UP"); E0(0x32, "BROWSER_HOME"); E0(0x35, "DIVIDE");
        E0(0x37, "SNAPSHOT"); E0(0x38, "RMENU"); E0(0x46, "CANCEL"); E0(0x47, "HOME"); E0(0x48, "UP");
        E0(0x49, "PRIOR"); E0(0x4B, "LEFT"); E0(0x4D, "RIGHT"); E0(0x4F, "END"); E0(0x50, "DOWN");
        E0(0x51, "NEXT"); E0(0x52, "INSERT"); E0(0x53, "DELETE"); E0(0x5B, "LWIN"); E0(0x5C, "RWIN");
        E0(0x5D, "APPS"); E0(0x5F, "SLEEP"); E0(0x65, "BROWSER_SEARCH"); E0(0x66, "BROWSER_FAVORITES");
        E0(0x67, "BROWSER_REFRESH"); E0(0x68, "BROWSER_STOP"); E0(0x69, "BROWSER_FORWARD");
        E0(0x6A, "BROWSER_BACK"); E0(0x6B, "LAUNCH_APP1"); E0(0x6C, "LAUNCH_MAIL"); E0(0x6D, "LAUNCH_MEDIA_SELECT");
        m.E1[0x1D] = new ScanCodeTarget(VirtualKeys.Parse("PAUSE"), 0);
        return m;
    }

    private static void AddEnglishKeyNames(KeyboardLayout l)
    {
        AddNames(l.KeyNames,
            (0x01, "Esc"), (0x0E, "Backspace"), (0x0F, "Tab"), (0x1C, "Enter"), (0x1D, "Ctrl"), (0x2A, "Shift"),
            (0x36, "Right Shift"), (0x37, "Num *"), (0x38, "Alt"), (0x39, "Space"), (0x3A, "Caps Lock"),
            (0x45, "Pause"), (0x46, "Scroll Lock"), (0x47, "Num 7"), (0x48, "Num 8"), (0x49, "Num 9"),
            (0x4A, "Num -"), (0x4B, "Num 4"), (0x4C, "Num 5"), (0x4D, "Num 6"), (0x4E, "Num +"), (0x4F, "Num 1"),
            (0x50, "Num 2"), (0x51, "Num 3"), (0x52, "Num 0"), (0x53, "Num Del"), (0x54, "Sys Req"),
            (0x57, "F11"), (0x58, "F12"));
        AddFunctionKeys(l);
        AddNames(l.KeyNamesExt,
            (0x1C, "Num Enter"), (0x1D, "Right Control"), (0x35, "Num /"), (0x37, "Prnt Scrn"), (0x38, "Right Alt"),
            (0x45, "Num Lock"), (0x46, "Break"), (0x47, "Home"), (0x48, "Up"), (0x49, "Page Up"), (0x4B, "Left"),
            (0x4D, "Right"), (0x4F, "End"), (0x50, "Down"), (0x51, "Page Down"), (0x52, "Insert"), (0x53, "Delete"),
            (0x5B, "Left Windows"), (0x5C, "Right Windows"), (0x5D, "Application"));
    }

    private static void AddGermanKeyNames(KeyboardLayout l)
    {
        AddNames(l.KeyNames,
            (0x01, "Esc"), (0x0E, "Rücktaste"), (0x0F, "Tabulator"), (0x1C, "Eingabe"), (0x1D, "Strg"),
            (0x2A, "Umschalt"), (0x36, "Umschalt rechts"), (0x37, "* (Zehnertastatur)"), (0x38, "Alt"),
            (0x39, "Leer"), (0x3A, "Feststell"), (0x45, "Pause"), (0x46, "Rollen-Feststell"),
            (0x47, "7 (Zehnertastatur)"), (0x48, "8 (Zehnertastatur)"), (0x49, "9 (Zehnertastatur)"),
            (0x4A, "- (Zehnertastatur)"), (0x4B, "4 (Zehnertastatur)"), (0x4C, "5 (Zehnertastatur)"),
            (0x4D, "6 (Zehnertastatur)"), (0x4E, "+ (Zehnertastatur)"), (0x4F, "1 (Zehnertastatur)"),
            (0x50, "2 (Zehnertastatur)"), (0x51, "3 (Zehnertastatur)"), (0x52, "0 (Zehnertastatur)"),
            (0x53, "Komma (Zehnertastatur)"), (0x54, "S-Abf"), (0x57, "F11"), (0x58, "F12"));
        AddFunctionKeys(l);
        AddNames(l.KeyNamesExt,
            (0x1C, "Eingabe (Zehnertastatur)"), (0x1D, "Strg rechts"), (0x35, "/ (Zehnertastatur)"),
            (0x37, "Druck"), (0x38, "Alt Gr"), (0x45, "Num-Feststell"), (0x46, "Untbr"), (0x47, "Pos1"),
            (0x48, "Nach-oben"), (0x49, "Bild-nach-oben"), (0x4B, "Nach-links"), (0x4D, "Nach-rechts"),
            (0x4F, "Ende"), (0x50, "Nach-unten"), (0x51, "Bild-nach-unten"), (0x52, "Einfg"), (0x53, "Entf"),
            (0x5B, "Linke Windows"), (0x5C, "Rechte Windows"), (0x5D, "Anwendung"));
    }

    private static void AddFunctionKeys(KeyboardLayout l)
    {
        for (var i = 0; i < 10; i++) l.KeyNames[(byte)(0x3B + i)] = "F" + (i + 1);
    }

    private static void AddNames(SortedDictionary<byte, string> target, params (int Scan, string Name)[] names)
    {
        foreach (var (scan, name) in names) target[(byte)scan] = name;
    }
}
