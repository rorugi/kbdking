using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace Keyservant.App.Views;

public sealed partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
    }
}
