using System.Globalization;

namespace Keyservant.Core.Model;

/// <summary>
/// Windows virtual-key codes and their names as used in .keyservant and .klc files
/// (the <c>VK_</c> prefix is dropped: <c>VK_OEM_1</c> becomes <c>OEM_1</c>, <c>'A'</c> becomes <c>A</c>).
/// </summary>
public static class VirtualKeys
{
    public const byte Back = 0x08;
    public const byte Tab = 0x09;
    public const byte Return = 0x0D;
    public const byte Shift = 0x10;
    public const byte Control = 0x11;
    public const byte Menu = 0x12;
    public const byte Cancel = 0x03;
    public const byte Kana = 0x15;
    public const byte Escape = 0x1B;
    public const byte Space = 0x20;
    public const byte Numpad0 = 0x60;
    public const byte Numpad9 = 0x69;
    public const byte Multiply = 0x6A;
    public const byte Add = 0x6B;
    public const byte Subtract = 0x6D;
    public const byte Decimal = 0x6E;
    public const byte Divide = 0x6F;

    /// <summary>Marks a continuation row in a VK_TO_WCHARS table (dead characters or SGCAPS row).</summary>
    public const byte ContinuationRow = 0xFF;

    private static readonly Dictionary<byte, string> s_names = BuildNames();
    private static readonly Dictionary<string, byte> s_codes = s_names.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<byte, string> BuildNames()
    {
        var n = new Dictionary<byte, string>
        {
            [0x01] = "LBUTTON", [0x02] = "RBUTTON", [0x03] = "CANCEL", [0x04] = "MBUTTON",
            [0x05] = "XBUTTON1", [0x06] = "XBUTTON2",
            [0x08] = "BACK", [0x09] = "TAB", [0x0C] = "CLEAR", [0x0D] = "RETURN",
            [0x10] = "SHIFT", [0x11] = "CONTROL", [0x12] = "MENU", [0x13] = "PAUSE", [0x14] = "CAPITAL",
            [0x15] = "KANA", [0x16] = "IME_ON", [0x17] = "JUNJA", [0x18] = "FINAL", [0x19] = "KANJI", [0x1A] = "IME_OFF",
            [0x1B] = "ESCAPE", [0x1C] = "CONVERT", [0x1D] = "NONCONVERT", [0x1E] = "ACCEPT", [0x1F] = "MODECHANGE",
            [0x20] = "SPACE", [0x21] = "PRIOR", [0x22] = "NEXT", [0x23] = "END", [0x24] = "HOME",
            [0x25] = "LEFT", [0x26] = "UP", [0x27] = "RIGHT", [0x28] = "DOWN", [0x29] = "SELECT",
            [0x2A] = "PRINT", [0x2B] = "EXECUTE", [0x2C] = "SNAPSHOT", [0x2D] = "INSERT", [0x2E] = "DELETE", [0x2F] = "HELP",
            [0x5B] = "LWIN", [0x5C] = "RWIN", [0x5D] = "APPS", [0x5F] = "SLEEP",
            [0x6A] = "MULTIPLY", [0x6B] = "ADD", [0x6C] = "SEPARATOR", [0x6D] = "SUBTRACT", [0x6E] = "DECIMAL", [0x6F] = "DIVIDE",
            [0x90] = "NUMLOCK", [0x91] = "SCROLL", [0x92] = "OEM_FJ_JISHO", [0x93] = "OEM_FJ_MASSHOU", [0x94] = "OEM_FJ_TOUROKU",
            [0x95] = "OEM_FJ_LOYA", [0x96] = "OEM_FJ_ROYA",
            [0xA0] = "LSHIFT", [0xA1] = "RSHIFT", [0xA2] = "LCONTROL", [0xA3] = "RCONTROL", [0xA4] = "LMENU", [0xA5] = "RMENU",
            [0xA6] = "BROWSER_BACK", [0xA7] = "BROWSER_FORWARD", [0xA8] = "BROWSER_REFRESH", [0xA9] = "BROWSER_STOP",
            [0xAA] = "BROWSER_SEARCH", [0xAB] = "BROWSER_FAVORITES", [0xAC] = "BROWSER_HOME",
            [0xAD] = "VOLUME_MUTE", [0xAE] = "VOLUME_DOWN", [0xAF] = "VOLUME_UP",
            [0xB0] = "MEDIA_NEXT_TRACK", [0xB1] = "MEDIA_PREV_TRACK", [0xB2] = "MEDIA_STOP", [0xB3] = "MEDIA_PLAY_PAUSE",
            [0xB4] = "LAUNCH_MAIL", [0xB5] = "LAUNCH_MEDIA_SELECT", [0xB6] = "LAUNCH_APP1", [0xB7] = "LAUNCH_APP2",
            [0xBA] = "OEM_1", [0xBB] = "OEM_PLUS", [0xBC] = "OEM_COMMA", [0xBD] = "OEM_MINUS", [0xBE] = "OEM_PERIOD",
            [0xBF] = "OEM_2", [0xC0] = "OEM_3", [0xC1] = "ABNT_C1", [0xC2] = "ABNT_C2",
            [0xDB] = "OEM_4", [0xDC] = "OEM_5", [0xDD] = "OEM_6", [0xDE] = "OEM_7", [0xDF] = "OEM_8",
            [0xE1] = "OEM_AX", [0xE2] = "OEM_102", [0xE3] = "ICO_HELP", [0xE4] = "ICO_00", [0xE5] = "PROCESSKEY",
            [0xE6] = "ICO_CLEAR", [0xE7] = "PACKET", [0xE9] = "OEM_RESET", [0xEA] = "OEM_JUMP", [0xEB] = "OEM_PA1",
            [0xEC] = "OEM_PA2", [0xED] = "OEM_PA3", [0xEE] = "OEM_WSCTRL", [0xEF] = "OEM_CUSEL", [0xF0] = "OEM_ATTN",
            [0xF1] = "OEM_FINISH", [0xF2] = "OEM_COPY", [0xF3] = "OEM_AUTO", [0xF4] = "OEM_ENLW", [0xF5] = "OEM_BACKTAB",
            [0xF6] = "ATTN", [0xF7] = "CRSEL", [0xF8] = "EXSEL", [0xF9] = "EREOF", [0xFA] = "PLAY", [0xFB] = "ZOOM",
            [0xFC] = "NONAME", [0xFD] = "PA1", [0xFE] = "OEM_CLEAR",
        };
        for (var c = '0'; c <= '9'; c++) n[(byte)c] = c.ToString();
        for (var c = 'A'; c <= 'Z'; c++) n[(byte)c] = c.ToString();
        for (var i = 0; i <= 9; i++) n[(byte)(0x60 + i)] = "NUMPAD" + i;
        for (var i = 1; i <= 24; i++) n[(byte)(0x6F + i)] = "F" + i;
        return n;
    }

    /// <summary>Returns the name of a virtual key, or <c>0xNN</c> for codes without a name.</summary>
    public static string GetName(byte vk) =>
        s_names.TryGetValue(vk, out var name) ? name : "0x" + vk.ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>Parses a virtual-key name (with or without <c>VK_</c>) or a hex code such as <c>0xBA</c>.</summary>
    public static byte Parse(string text)
    {
        if (TryParse(text, out var vk)) return vk;
        throw new FormatException($"Unknown virtual key '{text}'.");
    }

    public static bool TryParse(string? text, out byte vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.StartsWith("VK_", StringComparison.OrdinalIgnoreCase)) t = t[3..];
        if (s_codes.TryGetValue(t, out vk)) return true;
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && byte.TryParse(t.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk))
            return true;
        return false;
    }

    public static bool IsNumpadDigit(byte vk) => vk is >= Numpad0 and <= Numpad9;

    /// <summary>
    /// Keys whose characters Windows layouts always define the same way (control characters and the numpad).
    /// MSKLC does not list them in .klc files; the template supplies them.
    /// </summary>
    public static bool IsImplicitControlKey(byte vk) =>
        vk is Back or Tab or Return or Cancel or Escape || IsNumpadDigit(vk)
        || vk is Multiply or Add or Subtract or Divide;
}
