using System.Globalization;
using System.Windows.Input;
using Kbdking.Core.Geometry;
using Kbdking.Core.Model;

namespace Kbdking.Presentation;

/// <summary>One key on the on-screen keyboard.</summary>
public sealed class KeyCapViewModel : ObservableObject
{
    public const double UnitSize = 52;

    private readonly EditorViewModel _editor;
    private string _label = "";
    private string _shiftLabel = "";
    private bool _isDead;
    private bool _isSelected;
    private bool _isHighlighted;
    private bool _isCharacterKey;

    internal KeyCapViewModel(EditorViewModel editor, PhysicalKey key)
    {
        _editor = editor;
        Key = key;
        SelectCommand = new RelayCommand(() => _editor.SelectKey(this));
    }

    public PhysicalKey Key { get; }

    public double Width => Key.Width * UnitSize - 4;

    public byte? VirtualKey { get; private set; }

    /// <summary>What the key types in the selected shift state, or its name for keys like Shift.</summary>
    public string Label { get => _label; private set => SetProperty(ref _label, value); }

    /// <summary>What the key types with Shift, shown small in the corner.</summary>
    public string ShiftLabel { get => _shiftLabel; private set => SetProperty(ref _shiftLabel, value); }

    public bool IsDead { get => _isDead; private set => SetProperty(ref _isDead, value); }

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>Marked by a tool, e.g. keys that differ in the Compare tab.</summary>
    public bool IsHighlighted { get => _isHighlighted; private set => SetProperty(ref _isHighlighted, value); }

    /// <summary>True when the key carries characters and can be edited.</summary>
    public bool IsCharacterKey { get => _isCharacterKey; private set => SetProperty(ref _isCharacterKey, value); }

    public string ToolTip => VirtualKey is { } vk
        ? $"Scan code {(Key.Extended ? "E0 " : "")}{Key.ScanCode:X2}, virtual key {VirtualKeys.GetName(vk)}"
        : $"Scan code {(Key.Extended ? "E0 " : "")}{Key.ScanCode:X2}";

    public ICommand SelectCommand { get; }

    internal void Refresh(KeyboardLayout layout, ShiftState state)
    {
        var target = Key.Extended ? layout.ScanCodes.E0.GetValueOrDefault((byte)Key.ScanCode, ScanCodeTarget.None) : layout.ScanCodes[Key.ScanCode];
        VirtualKey = target.IsNone ? null : target.VirtualKey;
        var def = VirtualKey is { } vk ? layout.FindKey(vk) : null;
        IsCharacterKey = VirtualKey is { } v && Key.FixedLabel is null && !VirtualKeys.IsImplicitControlKey(v);

        IsHighlighted = VirtualKey is { } hv && _editor.HighlightedKeys.Contains(hv);
        var layer = VirtualKey is { } lv ? layout.Modifiers.VkToBits.FirstOrDefault(b => b.VirtualKey == lv && (b.Bits & (ModifierBits.LayerA | ModifierBits.LayerB | ModifierBits.LayerC)) != 0) : default;
        if (layer.Bits != ModifierBits.None)
        {
            IsCharacterKey = false;
            Label = new ShiftState(layer.Bits).DisplayName;
            ShiftLabel = "";
            IsDead = false;
            return;
        }
        if (Key.FixedLabel is not null || def is null)
        {
            Label = Key.FixedLabel switch
            {
                "AltGr" when !layout.Flags.HasFlag(LocaleFlags.AltGr) => "Alt",
                { } fixedLabel => fixedLabel,
                null => "",
            };
            ShiftLabel = "";
            IsDead = false;
            return;
        }

        var canonical = layout.Modifiers.Canonicalize(state);
        var output = canonical is { } c ? def.Outputs.GetValueOrDefault(c) : null;
        Label = Display(output);
        IsDead = output?.IsDead ?? false;
        ShiftLabel = state.Bits == ModifierBits.None ? Display(def.Outputs.GetValueOrDefault(ShiftState.Shift)) : "";
        OnPropertyChanged(nameof(ToolTip));
    }

    /// <summary>Formats an output for a key cap: control characters as U+XXXX, combining marks on a dotted circle.</summary>
    public static string Display(KeyOutput? output)
    {
        if (output is null) return "";
        var text = output.Text;
        if (text == " ") return "␣";
        if (text.Length == 1)
        {
            var c = text[0];
            if (char.IsControl(c)) return $"U+{(int)c:X4}";
            if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark)
                return "◌" + c;
        }
        return text;
    }
}
