using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Kbdking.App;

public partial class App : Application
{
    private MainWindow? _window;

    /// <summary>Where startup and other unexpected errors are written: %LOCALAPPDATA%\kbdking\crash.log.</summary>
    internal static string CrashLogPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kbdking", "crash.log");

    static App()
    {
        // Errors before the window exists (XAML loading, startup) would otherwise close the app without a trace.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);
    }

    public App()
    {
        InitializeComponent();
        // A bug in one action shouldn't close the app and lose the user's work: show it and carry on.
        UnhandledException += (_, e) =>
        {
            Log(e.Exception);
            if (_window is null) return; // startup failure: let it surface through ReportFatal
            e.Handled = true;
            _window.ShowError(e.Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
            Exit();
        }
    }

    internal static void Log(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ReportFatal(Exception? ex)
    {
        Log(ex);
        Console.Error.WriteLine(ex);
        if (Environment.GetEnvironmentVariable("KBDKING_NO_DIALOGS") == "1") return;
        _ = MessageBox(0, $"kbdking could not start:{Environment.NewLine}{Environment.NewLine}{ex?.Message}{Environment.NewLine}{Environment.NewLine}Details: {CrashLogPath}",
            "kbdking", 0x10 /* MB_ICONERROR */);
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint hwnd, string text, string caption, uint type);
}
