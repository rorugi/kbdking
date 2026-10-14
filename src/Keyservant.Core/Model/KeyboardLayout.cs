using System.Globalization;

namespace Keyservant.Core.Model;

/// <summary>Layout-wide behaviour flags (kbd.h: KLLF_ALTGR, KLLF_SHIFTLOCK, KLLF_LRM_RLM).</summary>
[Flags]
public enum LocaleFlags : ushort
{
    None = 0,
    /// <summary>Right Alt acts as AltGr (Ctrl+Alt).</summary>
    AltGr = 0x0001,
    /// <summary>Caps Lock is released by pressing Shift (instead of Caps Lock again).</summary>
    ShiftLock = 0x0002,
    /// <summary>Left/Right Shift + Backspace insert LRM/RLM marks.</summary>
    LrmRlm = 0x0004,
}

/// <summary>Descriptive information about a layout; nothing here changes which characters are typed.</summary>
public sealed class LayoutMetadata
{
    public string Name { get; set; } = "New layout";
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Company { get; set; }
    public string? Copyright { get; set; }
    public string Version { get; set; } = "1.0";

    /// <summary>BCP 47 language tag of the input language, e.g. <c>de-AT</c>.</summary>
    public string Locale { get; set; } = "en-US";

    /// <summary>File name of the layout DLL without extension, at most 8 characters, e.g. <c>kbdiast</c>.</summary>
    public string DllName { get; set; } = "kbdks";

    /// <summary>
    /// Keyboard layout ID (KLID), 8 hex digits such as <c>A0000C07</c>. Assigned on first install and kept,
    /// so rebuilding updates the installed layout instead of adding a second one.
    /// </summary>
    public string? LayoutId { get; set; }

    /// <summary>SPDX licence identifier for the layout file, e.g. <c>MIT</c> or <c>CC0-1.0</c>.</summary>
    public string? License { get; set; }

    /// <summary>The Windows language ID (low word of the LCID) for <see cref="Locale"/>.</summary>
    public ushort LanguageId
    {
        get
        {
            try
            {
                var lcid = CultureInfo.GetCultureInfo(Locale).LCID;
                // 0x1000 = LOCALE_CUSTOM_UNSPECIFIED (unknown on this system, or invariant globalization mode).
                if (lcid is not 0x1000 and not 0x007F) return (ushort)(lcid & 0xFFFF);
            }
            catch (CultureNotFoundException)
            {
            }
            return KnownLanguageIds.TryGetValue(Locale, out var id) ? id : (ushort)0x0409;
        }
    }

    // Fallback for environments without ICU data (e.g. Linux build agents in invariant mode).
    private static readonly Dictionary<string, ushort> KnownLanguageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en-US"] = 0x0409, ["en-GB"] = 0x0809, ["de-DE"] = 0x0407, ["de-AT"] = 0x0C07, ["de-CH"] = 0x0807,
        ["fr-FR"] = 0x040C, ["fr-CH"] = 0x100C, ["it-IT"] = 0x0410, ["es-ES"] = 0x0C0A, ["pt-BR"] = 0x0416,
        ["nl-NL"] = 0x0413, ["sv-SE"] = 0x041D, ["pl-PL"] = 0x0415, ["cs-CZ"] = 0x0405, ["hi-IN"] = 0x0439,
        ["sa-IN"] = 0x044F, ["ja-JP"] = 0x0411, ["ru-RU"] = 0x0419,
    };

    public LayoutMetadata Clone() => (LayoutMetadata)MemberwiseClone();
}

/// <summary>
/// A complete Windows keyboard layout: everything a layout DLL (kbd.h: KBDTABLES) can express,
/// plus descriptive metadata. This is the single model every importer, exporter and the editor work with.
/// </summary>
public sealed class KeyboardLayout
{
    public LayoutMetadata Metadata { get; set; } = new();

    public LocaleFlags Flags { get; set; } = LocaleFlags.AltGr;

    /// <summary>kbd.h KBD_VERSION stored in the high word of fLocaleFlags. Version 1 is required for ligatures.</summary>
    public ushort KbdVersion { get; set; } = 1;

    /// <summary>Keyboard type (4 = generic 101/102-key keyboard).</summary>
    public uint KeyboardType { get; set; } = 4;

    public uint KeyboardSubType { get; set; }

    public ModifierTable Modifiers { get; set; } = ModifierTable.CreateStandard();

    public ScanCodeMap ScanCodes { get; set; } = new();

    /// <summary>Character keys in table order. The order matters for Windows' VkKeyScan lookups.</summary>
    public List<KeyDefinition> Keys { get; } = [];

    public List<DeadKeyDefinition> DeadKeys { get; } = [];

    /// <summary>Names of unprefixed scan codes (GetKeyNameText).</summary>
    public SortedDictionary<byte, string> KeyNames { get; } = new();

    /// <summary>Names of E0-prefixed scan codes.</summary>
    public SortedDictionary<byte, string> KeyNamesExt { get; } = new();

    public KeyDefinition? FindKey(byte vk) => Keys.Find(k => k.VirtualKey == vk);

    public KeyDefinition GetOrAddKey(byte vk)
    {
        var key = FindKey(vk);
        if (key is null)
        {
            key = new KeyDefinition(vk);
            // Keep numpad digits last: Windows' VkKeyScan prefers keys from earlier tables.
            var numpadIndex = Keys.FindIndex(k => VirtualKeys.IsNumpadDigit(k.VirtualKey));
            if (numpadIndex >= 0 && !VirtualKeys.IsNumpadDigit(vk)) Keys.Insert(numpadIndex, key);
            else Keys.Add(key);
        }
        return key;
    }

    public DeadKeyDefinition? FindDeadKey(char accent) => DeadKeys.Find(d => d.Accent == accent);

    public DeadKeyDefinition GetOrAddDeadKey(char accent)
    {
        var d = FindDeadKey(accent);
        if (d is null)
        {
            d = new DeadKeyDefinition(accent);
            DeadKeys.Add(d);
        }
        return d;
    }

    /// <summary>Sets the output of a key, adding a column for the shift state if the layout doesn't have one yet.</summary>
    public void SetOutput(byte vk, ShiftState state, KeyOutput? output)
    {
        var key = GetOrAddKey(vk);
        if (output is null)
        {
            key.Outputs.Remove(state);
            return;
        }
        state = Modifiers.CanonicalState(Modifiers.EnsureColumn(state));
        if (state.Bits.HasFlag(ModifierBits.AltGr)) Flags |= LocaleFlags.AltGr;
        key.Outputs[state] = output;
        if (output.IsDead) GetOrAddDeadKey(output.Accent);
    }

    /// <summary>
    /// Stores every output under the canonical state of its column (e.g. a layout where Ctrl+Alt and
    /// Shift+Ctrl+Alt share a column keeps outputs under "altgr"). Outputs for states without a column are kept.
    /// </summary>
    public void NormalizeShiftStates()
    {
        foreach (var key in Keys)
        {
            Normalize(key.Outputs);
            if (key.CapsOutputs is not null) Normalize(key.CapsOutputs);
        }

        void Normalize(SortedDictionary<ShiftState, KeyOutput> outputs)
        {
            foreach (var state in outputs.Keys.ToList())
            {
                if (Modifiers.Canonicalize(state) is { } canonical && canonical != state && !outputs.ContainsKey(canonical))
                {
                    outputs[canonical] = outputs[state];
                    outputs.Remove(state);
                }
            }
        }
    }

    public KeyboardLayout Clone()
    {
        var l = new KeyboardLayout
        {
            Metadata = Metadata.Clone(),
            Flags = Flags,
            KbdVersion = KbdVersion,
            KeyboardType = KeyboardType,
            KeyboardSubType = KeyboardSubType,
            Modifiers = Modifiers.Clone(),
            ScanCodes = ScanCodes.Clone(),
        };
        l.Keys.AddRange(Keys.Select(k => k.Clone()));
        l.DeadKeys.AddRange(DeadKeys.Select(d => d.Clone()));
        foreach (var (k, v) in KeyNames) l.KeyNames[k] = v;
        foreach (var (k, v) in KeyNamesExt) l.KeyNamesExt[k] = v;
        return l;
    }
}
