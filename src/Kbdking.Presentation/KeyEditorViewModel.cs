using System.Globalization;
using System.Text;
using Kbdking.Core.Model;

namespace Kbdking.Presentation;

/// <summary>The output of the selected key in one shift state, edited as text plus a dead-key switch.</summary>
public sealed class OutputRowViewModel : ObservableObject, ICharacterTarget
{
    private readonly EditorViewModel _editor;
    private readonly byte _vk;
    private string _text;
    private bool _isDead;
    private string? _error;

    internal OutputRowViewModel(EditorViewModel editor, byte vk, ShiftState state, KeyOutput? output)
    {
        _editor = editor;
        _vk = vk;
        State = state;
        _text = output is null ? "" : Format(output.Text);
        _isDead = output?.IsDead ?? false;
    }

    public ShiftState State { get; }

    public string StateName => State.DisplayName;

    /// <summary>
    /// The characters, typed directly or as codes: <c>U+0101</c> or <c>ā</c>. Empty means the key types nothing.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value)) Apply();
        }
    }

    public bool IsDead
    {
        get => _isDead;
        set
        {
            if (SetProperty(ref _isDead, value)) Apply();
        }
    }

    public string? Error { get => _error; private set => SetProperty(ref _error, value); }

    /// <summary>Code points of the current text, e.g. "U+0101 LATIN SMALL LETTER A WITH MACRON" (name if known).</summary>
    public string CodePoints => Describe(_text);

    public void PickCharacter(string text) => Text = text;

    /// <summary>"U+0101 LATIN SMALL LETTER A WITH MACRON" for one character, codes only for longer text.</summary>
    internal static string Describe(string text)
    {
        if (!TryParse(text, out var s) || s.Length == 0) return "";
        var runes = EnumerateRunes(s).ToList();
        if (runes.Count == 1)
            return $"U+{runes[0].Value:X4} {Kbdking.Core.Unicode.UnicodeCatalog.GetName(runes[0].Value)}".TrimEnd();
        return string.Join(" ", runes.Select(r => $"U+{r.Value:X4}"));
    }

    private void Apply()
    {
        if (!TryParse(_text, out var parsed))
        {
            Error = "Use characters, or codes like U+0101.";
            return;
        }
        if (_isDead && parsed.Length != 1)
        {
            Error = parsed.Length == 0 ? "A dead key needs an accent character." : "A dead key must be a single character.";
            return;
        }
        Error = null;
        KeyOutput? output = parsed.Length == 0 ? null : _isDead ? KeyOutput.Dead(parsed[0]) : KeyOutput.Of(parsed);
        _editor.SetOutput(_vk, State, output);
        OnPropertyChanged(nameof(CodePoints));
    }

    /// <summary>Shows control and invisible characters as U+XXXX so they can be seen and edited.</summary>
    internal static string Format(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            var cat = char.GetUnicodeCategory(c);
            if (char.IsControl(c) || cat is UnicodeCategory.Format or UnicodeCategory.SpaceSeparator && c != ' ')
                sb.Append(CultureInfo.InvariantCulture, $"U+{(int)c:X4}");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Parses text that may contain U+XXXX or \uXXXX codes.</summary>
    internal static bool TryParse(string input, out string result)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < input.Length)
        {
            if (i + 2 < input.Length && (input[i] is 'U' or 'u' && input[i + 1] == '+' || input[i] == '\\' && input[i + 1] is 'u' or 'U'))
            {
                var start = i + 2;
                var end = start;
                while (end < input.Length && end - start < 6 && Uri.IsHexDigit(input[end])) end++;
                if (end - start < 4 || !int.TryParse(input.AsSpan(start, end - start), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp)
                    || cp > 0x10FFFF || cp is >= 0xD800 and <= 0xDFFF)
                {
                    result = "";
                    return false;
                }
                sb.Append(char.ConvertFromUtf32(cp));
                i = end;
                continue;
            }
            sb.Append(input[i]);
            i++;
        }
        result = sb.ToString();
        return true;
    }

    private static IEnumerable<Rune> EnumerateRunes(string s)
    {
        foreach (var r in s.EnumerateRunes()) yield return r;
    }
}

/// <summary>Editing panel for the selected key: one row per shift state, plus Caps Lock behaviour.</summary>
public sealed class KeyEditorViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private readonly KeyDefinition _key;

    internal KeyEditorViewModel(EditorViewModel editor, KeyboardLayout layout, KeyDefinition key, IEnumerable<ShiftState> states)
    {
        _editor = editor;
        _key = key;
        Title = $"{VirtualKeys.GetName(key.VirtualKey)}";
        var scan = layout.ScanCodes.FindScanCode(key.VirtualKey);
        Details = scan is { } sc ? $"Scan code {sc:X2} → virtual key {VirtualKeys.GetName(key.VirtualKey)}" : $"Virtual key {VirtualKeys.GetName(key.VirtualKey)} (no scan code produces it)";
        foreach (var s in states)
        {
            var canonical = layout.Modifiers.Canonicalize(s) ?? s;
            Rows.Add(new OutputRowViewModel(editor, key.VirtualKey, canonical, key.Outputs.GetValueOrDefault(canonical)));
        }
    }

    public string Title { get; }

    public string Details { get; }

    public List<OutputRowViewModel> Rows { get; } = [];

    /// <summary>Caps Lock acts like Shift for this key (letters).</summary>
    public bool CapsLock
    {
        get => _key.Attributes.HasFlag(KeyAttributes.CapsLock);
        set
        {
            if (value == CapsLock) return;
            _key.Attributes = value ? _key.Attributes | KeyAttributes.CapsLock : _key.Attributes & ~KeyAttributes.CapsLock;
            _editor.MarkChanged();
            _editor.RefreshKeyboard();
            OnPropertyChanged();
        }
    }

    /// <summary>Caps Lock also acts like Shift in the AltGr states.</summary>
    public bool CapsLockAltGr
    {
        get => _key.Attributes.HasFlag(KeyAttributes.CapsLockAltGr);
        set
        {
            if (value == CapsLockAltGr) return;
            _key.Attributes = value ? _key.Attributes | KeyAttributes.CapsLockAltGr : _key.Attributes & ~KeyAttributes.CapsLockAltGr;
            _editor.MarkChanged();
            _editor.RefreshKeyboard();
            OnPropertyChanged();
        }
    }
}
