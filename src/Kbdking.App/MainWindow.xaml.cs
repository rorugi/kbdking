using Kbdking.App.Services;
using Kbdking.App.Views;
using Kbdking.Platform;
using Kbdking.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kbdking.App;

public sealed partial class MainWindow : Window
{
    private readonly LayoutsView _layoutsView;
    private readonly EditorView _editorView;
    private readonly AboutView _aboutView;
    private readonly DialogService _dialogs;
    private readonly EditorViewModel _editor;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 860));
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "kbdking.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        var dialogs = _dialogs = new DialogService(this);
        var privileged = new CliPrivilegedOperations();
        var editor = _editor = new EditorViewModel(dialogs, privileged);
        RelayCommand.UnhandledError = ShowError;
        var manager = new LayoutManagerViewModel(new WindowsLayoutSystem(), privileged, dialogs, async path =>
        {
            if (await editor.OpenFileInteractiveAsync(path)) Nav.SelectedItem = EditorItem;
        });
        AppWindow.Closing += OnClosing;
        Activated += (_, e) =>
        {
            // Key-up events go to other windows while this one is in the background.
            if (e.WindowActivationState == WindowActivationState.Deactivated) _editorView?.ResetHeldKeys();
        };

        _aboutView = new AboutView();
        _layoutsView = new LayoutsView(manager);
        _editorView = new EditorView(editor);
        Nav.SelectedItem = LayoutsItem;
        // Entries of layouts that no longer exist can't be selected in the switcher; remove them.
        try { UserSwitcher.RemoveStale(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { App.Log(ex); }
        manager.Refresh();
        Nav.Loaded += OnFirstLoaded;
    }

    /// <summary>
    /// A layout file given on the command line ("Open with") opens in the editor. KBDKING_TAB=&lt;topic id&gt;
    /// (e.g. tab-assistant, or "about") opens that page; the build uses it to take screenshots.
    /// </summary>
    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Nav.Loaded -= OnFirstLoaded;
        try
        {
            if (Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists) is { } file && await _editor.OpenFileInteractiveAsync(file))
                Nav.SelectedItem = EditorItem;
            switch (Environment.GetEnvironmentVariable("KBDKING_TAB"))
            {
                case null or "":
                    break;
                case "about":
                    Nav.SelectedItem = AboutItem;
                    break;
                case "layouts":
                    Nav.SelectedItem = LayoutsItem;
                    break;
                case var tab:
                    Nav.SelectedItem = EditorItem;
                    _editorView.SelectTab(tab);
                    break;
            }
            // KBDKING_DEMO=1 fills the tools with an example (for the screenshots in the README).
            if (Environment.GetEnvironmentVariable("KBDKING_DEMO") == "1") _editor.ShowDemo();
            // KBDKING_SHIFT_STATE=altgr shows the keyboard in that shift state (also for screenshots).
            if (Kbdking.Core.Model.ShiftState.TryParse(Environment.GetEnvironmentVariable("KBDKING_SHIFT_STATE"), out var state)
                && _editor.ShiftStates.FirstOrDefault(o => o.State == state) is { } option)
                _editor.SelectedState = option;
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Shows an unexpected error instead of crashing.</summary>
    internal void ShowError(Exception ex)
    {
        App.Log(ex);
        _ = _dialogs.ShowMessageAsync("Something went wrong", ex.Message);
    }

    /// <summary>Asks before closing with unsaved changes.</summary>
    private async void OnClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !_editor.IsDirty) return;
        args.Cancel = true;
        if (!await _editor.ConfirmDiscardAsync()) return;
        _closeConfirmed = true;
        Close();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Host.Content = ((args.SelectedItem as NavigationViewItem)?.Tag as string) switch
        {
            "editor" => (UserControl)_editorView,
            "about" => _aboutView,
            _ => _layoutsView,
        };
    }
}
