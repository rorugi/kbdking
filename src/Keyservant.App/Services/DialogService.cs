using Keyservant.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Keyservant.App.Services;

/// <summary>Content dialogs and file pickers for the main window.</summary>
internal sealed class DialogService(Window window) : IDialogService
{
    // Only one ContentDialog can be open at a time.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText) =>
        await ShowAsync(title, message, confirmText, "Cancel") == ContentDialogResult.Primary;

    public async Task ShowMessageAsync(string title, string message) => await ShowAsync(title, message, null, "OK");

    private async Task<ContentDialogResult> ShowAsync(string title, string message, string? primary, string close)
    {
        await _gate.WaitAsync();
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } },
                CloseButtonText = close,
                DefaultButton = primary is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
                XamlRoot = window.Content.XamlRoot,
            };
            if (primary is not null) dialog.PrimaryButtonText = primary;
            return await dialog.ShowAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> PickOpenFileAsync(IReadOnlyList<string> extensions)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        foreach (var e in extensions) picker.FileTypeFilter.Add(e);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName, IReadOnlyList<string> extensions)
    {
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName) };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        foreach (var e in extensions) picker.FileTypeChoices.Add($"{e.TrimStart('.').ToUpperInvariant()} file", [e]);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
