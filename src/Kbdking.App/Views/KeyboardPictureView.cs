using Kbdking.Core.Rendering;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kbdking.App.Views;

/// <summary>
/// Draws a <see cref="KeyboardPicture"/> (heatmap, comparison) with native controls. WinUI's SVG images don't
/// render text, so the SVG version is only used for saving.
/// </summary>
internal static class KeyboardPictureView
{
    private const double Unit = 56;

    public static UIElement? Create(KeyboardPicture? picture)
    {
        if (picture is null) return null;
        var canvas = new Canvas { Width = picture.Width * Unit, Height = picture.Rows * Unit, Background = new SolidColorBrush(Colors.White) };
        foreach (var key in picture.Keys)
        {
            var grid = new Grid { Padding = new Thickness(5, 3, 5, 3) };
            if (key.FixedLabel is not null)
            {
                grid.Children.Add(Label(key.FixedLabel, false, 12, HorizontalAlignment.Center, VerticalAlignment.Center, Color.FromArgb(255, 0x44, 0x44, 0x44)));
            }
            else
            {
                Add(grid, key.Base, 18, HorizontalAlignment.Left, VerticalAlignment.Bottom);
                Add(grid, key.Shift, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
                Add(grid, key.AltGr, 12, HorizontalAlignment.Right, VerticalAlignment.Bottom);
                Add(grid, key.ShiftAltGr, 12, HorizontalAlignment.Right, VerticalAlignment.Top);
                if (key.Count > 0)
                    grid.Children.Add(Label(key.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), false, 10,
                        HorizontalAlignment.Center, VerticalAlignment.Center, Color.FromArgb(0x99, 0, 0, 0)));
            }
            var border = new Border
            {
                Width = key.Width * Unit - 3,
                Height = Unit - 3,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0x88, 0x88, 0x88)),
                Background = new SolidColorBrush(Parse(key.Fill)),
                Child = grid,
            };
            Canvas.SetLeft(border, key.X * Unit + 1.5);
            Canvas.SetTop(border, key.Row * Unit + 1.5);
            canvas.Children.Add(border);
        }
        return canvas;
    }

    private static void Add(Grid grid, PictureLabel? label, double size, HorizontalAlignment h, VerticalAlignment v)
    {
        if (label is null) return;
        grid.Children.Add(Label(label.Text, label.IsDead, size, h, v,
            label.IsDead ? Color.FromArgb(255, 0xD9, 0x48, 0x0F) : Color.FromArgb(255, 0x11, 0x11, 0x11)));
    }

    private static TextBlock Label(string text, bool bold, double size, HorizontalAlignment h, VerticalAlignment v, Color color) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        HorizontalAlignment = h,
        VerticalAlignment = v,
        Foreground = new SolidColorBrush(color),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>"#rrggbb" → colour.</summary>
    private static Color Parse(string hex)
    {
        var value = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
