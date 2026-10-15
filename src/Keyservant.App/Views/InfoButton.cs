using Keyservant.Presentation;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Keyservant.App.Views;

/// <summary>
/// A small ⓘ button that shows a detailed explanation when clicked. The text comes from
/// <see cref="HelpTopics"/>: <c>&lt;views:InfoButton Topic="dll-name" /&gt;</c>.
/// </summary>
public sealed partial class InfoButton : Button
{
    public static readonly DependencyProperty TopicProperty =
        DependencyProperty.Register(nameof(Topic), typeof(string), typeof(InfoButton), new PropertyMetadata(null, (d, _) => ((InfoButton)d).Update()));

    private readonly TextBlock _title = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

    public InfoButton()
    {
        Content = new FontIcon { Glyph = "", FontSize = 14 };
        Padding = new Thickness(6);
        MinWidth = 0;
        MinHeight = 0;
        Background = new SolidColorBrush(Colors.Transparent);
        BorderThickness = new Thickness(0);
        VerticalAlignment = VerticalAlignment.Center;
        Flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            Content = new StackPanel { Spacing = 8, MaxWidth = 420, Children = { _title, _text } },
        };
        ToolTipService.SetToolTip(this, "What is this?");
        Update();
    }

    /// <summary>The id of the explanation in <see cref="HelpTopics.All"/>.</summary>
    public string? Topic
    {
        get => (string?)GetValue(TopicProperty);
        set => SetValue(TopicProperty, value);
    }

    private void Update()
    {
        var topic = HelpTopics.Get(Topic);
        _title.Text = topic.Title;
        _text.Text = topic.Text;
        AutomationProperties.SetName(this, "Explain: " + topic.Title);
    }
}
