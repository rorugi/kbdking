using System.ComponentModel;
using System.Text;
using Keyservant.Core.Unicode;
using Keyservant.Presentation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;

namespace Keyservant.App.Views;

public sealed partial class EditorView : UserControl
{
    private readonly Flyout _unicodeFlyout;

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
        foreach (var d in viewModel.DeadKeys.CommonDiacritics)
        {
            CommonDeadKeysMenu.Items.Add(new MenuFlyoutItem
            {
                Text = $"{d.Spacing ?? d.Combining}  {d.Name}",
                Command = viewModel.DeadKeys.AddCommonCommand,
                CommandParameter = d.Combining.ToString(),
            });
        }
        foreach (var p in viewModel.Assistant.Presets)
        {
            PresetMenu.Items.Add(new MenuFlyoutItem { Text = p.Name, Command = viewModel.Assistant.PresetCommand, CommandParameter = p.Name });
        }

        _unicodeFlyout = CreateUnicodeFlyout();
        viewModel.Analysis.PropertyChanged += OnToolPropertyChanged;
        viewModel.Compare.PropertyChanged += OnToolPropertyChanged;
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

    // ---------------------------------------------------------------- Unicode picker

    /// <summary>Opens the character search next to a field; the picked character goes into that field.</summary>
    private void PickCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ICharacterTarget target } element) return;
        ViewModel.Unicode.Target = target;
        _unicodeFlyout.ShowAt(element, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Bottom });
    }

    private Flyout CreateUnicodeFlyout()
    {
        var picker = ViewModel.Unicode;
        var query = new TextBox { PlaceholderText = "Name, code or character: a macron, dot below, U+1E43", Width = 440 };
        query.SetBinding(TextBox.TextProperty, new Binding { Path = new PropertyPath(nameof(UnicodePickerViewModel.Query)), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var summary = new TextBlock { Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
        summary.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(UnicodePickerViewModel.ResultSummary)) });
        var list = new ListView
        {
            Height = 360,
            Width = 440,
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = (DataTemplate)Resources["UnicodeItemTemplate"],
            ItemsSource = picker.Results,
        };
        var footer = new TextBlock { Text = $"{picker.UnicodeVersion} · works offline", Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
        var panel = new StackPanel { Spacing = 8, DataContext = picker, Children = { query, summary, list, footer } };
        var flyout = new Flyout { Content = panel };

        list.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is UnicodeCharInfo info && picker.Pick(info)) flyout.Hide();
        };
        query.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter && picker.Pick()) { args.Handled = true; flyout.Hide(); }
            else if (args.Key == VirtualKey.Down && list.Items.Count > 0) { list.Focus(FocusState.Keyboard); args.Handled = true; }
        };
        flyout.Opened += (_, _) => { query.Focus(FocusState.Programmatic); query.SelectAll(); };
        return flyout;
    }

    // ---------------------------------------------------------------- pictures

    private async void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnalysisViewModel.HeatmapSvg)) await ShowSvgAsync(HeatmapImage, ViewModel.Analysis.HeatmapSvg);
        else if (e.PropertyName == nameof(CompareViewModel.DiffSvg)) await ShowSvgAsync(DiffImage, ViewModel.Compare.DiffSvg);
    }

    private static async Task ShowSvgAsync(Image image, string? svg)
    {
        if (svg is null)
        {
            image.Source = null;
            return;
        }
        var source = new SvgImageSource { RasterizePixelWidth = 1600 };
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(Encoding.UTF8.GetBytes(svg));
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        await source.SetSourceAsync(stream);
        image.Source = source;
    }

    private void SendToAssistant_Click(object sender, RoutedEventArgs e) => ViewModel.Analysis.SendUntypeableToAssistant();
}
