namespace Kbdking.Presentation;

/// <summary>Dialogs and pickers, implemented by the UI layer.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    Task ShowMessageAsync(string title, string message);

    /// <summary>Returns the chosen file, or null if cancelled. <paramref name="extensions"/> include the dot.</summary>
    Task<string?> PickOpenFileAsync(IReadOnlyList<string> extensions);

    Task<string?> PickSaveFileAsync(string suggestedName, IReadOnlyList<string> extensions);

    Task<string?> PickFolderAsync();
}

/// <summary>Result of an operation that ran in an elevated helper process.</summary>
public sealed record OperationResult(bool Succeeded, string Output, bool Cancelled = false);

/// <summary>
/// Operations that need administrator rights. The app runs them through the elevated CLI helper,
/// so the app itself never runs as administrator (one UAC prompt per action).
/// </summary>
public interface IPrivilegedOperations
{
    /// <summary>Installs the layout saved at <paramref name="layoutFile"/> (a .kbdking file).</summary>
    Task<OperationResult> InstallAsync(string layoutFile);

    Task<OperationResult> UninstallAsync(string klid);

    Task<OperationResult> RepairAsync();

    /// <summary>
    /// Installs the layout for <paramref name="minutes"/> minutes; Windows removes it again automatically
    /// unless <see cref="KeepAsync"/> is called.
    /// </summary>
    Task<OperationResult> InstallTemporaryAsync(string layoutFile, int minutes);

    /// <summary>Turns a test install into a normal install.</summary>
    Task<OperationResult> KeepAsync(string klid);
}
