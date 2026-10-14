using Keyservant.Core.Validation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Keyservant.App;

/// <summary>Functions used from x:Bind expressions.</summary>
public static class Converters
{
    public static Visibility Visible(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Brush KeyBackground(bool isSelected, bool isDead, bool isCharacterKey) =>
        Resource(isSelected ? "SystemFillColorAttentionBackgroundBrush"
            : isDead ? "SystemFillColorCautionBackgroundBrush"
            : isCharacterKey ? "ControlFillColorDefaultBrush"
            : "ControlFillColorTertiaryBrush");

    public static string SeverityGlyph(Severity severity) => severity switch
    {
        Severity.Error => "",   // ErrorBadge
        Severity.Warning => "", // Warning
        _ => "",                // Info
    };

    public static Brush SeverityBrush(Severity severity) => Resource(severity switch
    {
        Severity.Error => "SystemFillColorCriticalBrush",
        Severity.Warning => "SystemFillColorCautionBrush",
        _ => "SystemFillColorAttentionBrush",
    });

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
}
