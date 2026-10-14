using Keyservant.Presentation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Keyservant.App.Views;

public sealed partial class EditorView : UserControl
{
    public EditorView(EditorViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        foreach (var template in viewModel.Templates)
        {
            NewMenu.Items.Add(new MenuFlyoutItem
            {
                Text = $"From {template.DisplayName}",
                Command = viewModel.NewCommand,
                CommandParameter = template.Id,
            });
        }
    }

    public EditorViewModel ViewModel { get; }

    /// <summary>
    /// Feeds physical keystrokes (scan codes) to the layout being edited, so the preview behaves like the
    /// new layout regardless of which layout Windows currently has active.
    /// </summary>
    private void Preview_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        if (e.Key is VirtualKey.Tab && !Down(VirtualKey.Control)) return; // keep Tab for focus navigation

        var status = e.KeyStatus;
        var capsLock = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.CapitalLock).HasFlag(CoreVirtualKeyStates.Locked);
        // AltGr arrives as Ctrl+Alt, which is exactly how layouts define it.
        e.Handled = ViewModel.HandlePreviewKey((int)status.ScanCode, status.IsExtendedKey,
            Down(VirtualKey.Shift), Down(VirtualKey.Control), Down(VirtualKey.Menu), capsLock);
        if (e.Handled && sender is TextBox box) box.SelectionStart = box.Text.Length;
    }
}
