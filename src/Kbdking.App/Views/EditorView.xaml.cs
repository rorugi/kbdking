using System.ComponentModel;
using System.Text;
using Kbdking.Core.Unicode;
using Kbdking.Presentation;
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

namespace Kbdking.App.Views;

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
        viewModel.TabRequested += (_, topic) => SelectTab(topic);
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

    /// <summary>
    /// The preview is editable only so it shows a cursor: text comes from the layout simulation alone, so
    /// pasting, IME input or keys the simulation doesn't handle never change it.
    /// </summary>
    private void Preview_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        static string Lines(string s) => s.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
        if (Lines(args.NewText) != Lines(ViewModel.PreviewText)) args.Cancel = true;
    }

    /// <summary>Keeps the cursor after the last character typed.</summary>
    private void Preview_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box) box.SelectionStart = box.Text.Length;
    }

    /// <summary>Layer keys work while held, so the preview also needs to know when they are released.</summary>
    private void Preview_PreviewKeyUp(object sender, KeyRoutedEventArgs e) =>
        e.Handled = ViewModel.HandlePreviewKeyUp((int)e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey);

    /// <summary>Key-up events go elsewhere once the preview loses focus, so forget held layer keys.</summary>
    private void Preview_LostFocus(object sender, RoutedEventArgs e) => ViewModel.ResetPreviewKeys();

    // ---------------------------------------------------------------- tabs

    private void EditorTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => ShowSelectedPages();

    private void AdvancedTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => ShowSelectedPages();

    /// <summary>Shows the page of the selected tab (the tab bar stays fixed) and points the ⓘ button at it.</summary>
    private void ShowSelectedPages()
    {
        static void Show(SelectorBar? bar, Grid? pages)
        {
            if (bar is null || pages is null) return; // during XAML loading
            var index = bar.SelectedItem is { } item ? bar.Items.IndexOf(item) : 0;
            for (var i = 0; i < pages.Children.Count; i++) pages.Children[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }
        Show(EditorTabs, EditorPages);
        Show(AdvancedTabs, AdvancedPages);
        if (TabInfo is null) return;
        var topic = EditorTabs?.SelectedItem?.Tag as string ?? "tab-keys";
        TabInfo.Topic = topic == "advanced" ? AdvancedTabs?.SelectedItem?.Tag as string ?? "tab-shiftstates" : topic;
    }

    /// <summary>Shows the tab with this topic id (e.g. "tab-deadkeys"), also inside Advanced.</summary>
    internal void SelectTab(string topic)
    {
        foreach (var (bar, parent) in new[] { (EditorTabs, (string?)null), (AdvancedTabs, "advanced") })
        {
            if (bar.Items.FirstOrDefault(i => i.Tag as string == topic) is not { } item) continue;
            if (parent is not null) EditorTabs.SelectedItem = EditorTabs.Items.First(i => i.Tag as string == parent);
            bar.SelectedItem = item;
            ShowSelectedPages();
            if (topic == "tab-deadkeys" && ViewModel.DeadKeys.Selected is { } selected)
                DispatcherQueue.TryEnqueue(() => DeadKeyList.ScrollIntoView(selected));
            return;
        }
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
        list.SetBinding(Selector.SelectedItemProperty, new Binding { Path = new PropertyPath(nameof(UnicodePickerViewModel.Selected)), Mode = BindingMode.TwoWay });
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

    private readonly Dictionary<Image, int> _svgVersions = [];

    /// <summary>Shows SVG text in an image. Renders can overlap; only the newest one is shown.</summary>
    private async Task ShowSvgAsync(Image image, string? svg)
    {
        var version = _svgVersions[image] = _svgVersions.GetValueOrDefault(image) + 1;
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
        var status = await source.SetSourceAsync(stream);
        if (version != _svgVersions[image]) return; // a newer picture is on its way
        image.Source = status == SvgImageSourceLoadStatus.Success ? source : null;
    }

    private void SendToAssistant_Click(object sender, RoutedEventArgs e) => ViewModel.Analysis.SendUntypeableToAssistant();
}
