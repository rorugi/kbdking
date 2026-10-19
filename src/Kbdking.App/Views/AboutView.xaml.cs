using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kbdking.App.Views;

public sealed partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        // 0.1.0 for a release, 0.1.0 build 42 for a build between releases.
        VersionText.Text = version is null ? "" : version.Revision > 0 ? $"Version {version.ToString(3)} build {version.Revision}" : $"Version {version.ToString(3)}";
        var logo = Path.Combine(AppContext.BaseDirectory, "Assets", "kbdking.png");
        if (File.Exists(logo)) Logo.Source = new BitmapImage(new Uri(logo));
    }
}
