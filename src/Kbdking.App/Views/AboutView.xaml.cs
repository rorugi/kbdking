using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kbdking.App.Views;

public sealed partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
        var logo = Path.Combine(AppContext.BaseDirectory, "Assets", "kbdking.png");
        if (File.Exists(logo)) Logo.Source = new BitmapImage(new Uri(logo));
    }
}
