using Keyservant.App.Services;
using Keyservant.App.Views;
using Keyservant.Platform;
using Keyservant.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Keyservant.App;

public sealed partial class MainWindow : Window
{
    private readonly LayoutsView _layoutsView;
    private readonly EditorView _editorView;
    private readonly AboutView _aboutView = new();

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 860));

        var dialogs = new DialogService(this);
        var privileged = new CliPrivilegedOperations();
        var editor = new EditorViewModel(dialogs, privileged);
        var manager = new LayoutManagerViewModel(new WindowsLayoutSystem(), privileged, dialogs, path =>
        {
            editor.OpenFile(path);
            Nav.SelectedItem = EditorItem;
        });

        _layoutsView = new LayoutsView(manager);
        _editorView = new EditorView(editor);
        Nav.SelectedItem = LayoutsItem;
        manager.Refresh();
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
