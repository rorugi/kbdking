using Microsoft.UI.Xaml;

namespace Keyservant.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        // A bug in one action shouldn't close the app and lose the user's work: show it and carry on.
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            _window?.ShowError(e.Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
