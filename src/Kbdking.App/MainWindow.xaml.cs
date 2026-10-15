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

        var dialogs = _dialogs = new DialogService(this);
        var privileged = new CliPrivilegedOperations();
        var editor = _editor = new EditorViewModel(dialogs, privileged);
        RelayCommand.UnhandledError = ShowError;
        var manager = new LayoutManagerViewModel(new WindowsLayoutSystem(), privileged, dialogs, async path =>
        {
            if (await editor.OpenFileInteractiveAsync(path)) Nav.SelectedItem = EditorItem;
        });
        AppWindow.Closing += OnClosing;

        _aboutView = new AboutView();
        _layoutsView = new LayoutsView(manager);
        _editorView = new EditorView(editor);
        Nav.SelectedItem = LayoutsItem;
        manager.Refresh();
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
