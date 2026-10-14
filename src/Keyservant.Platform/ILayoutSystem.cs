using Keyservant.Binary.Packaging;

namespace Keyservant.Platform;

/// <summary>
/// The keyboard layouts of this PC. The editor and CLI use this interface; tests substitute a fake.
/// </summary>
public interface ILayoutSystem
{
    /// <summary>All registered layouts, Windows' own and custom.</summary>
    IReadOnlyList<InstalledLayout> GetLayouts();

    /// <summary>Adds a layout to the current user's switcher. Needs no admin rights.</summary>
    void Enable(InstalledLayout layout);

    /// <summary>Removes a layout from the current user's switcher. Needs no admin rights; nothing is deleted.</summary>
    void Disable(InstalledLayout layout);

    /// <summary>
    /// Installs a custom layout: copies the DLLs, registers the layout and enables it for the current user.
    /// Reinstalling a layout with the same KLID updates it in place. Requires administrator rights.
    /// </summary>
    InstalledLayout Install(LayoutPackage package);

    /// <summary>
    /// Completely removes a custom layout: switcher entry, registry key, both DLLs and the stored package.
    /// Windows' own layouts can only be disabled, never uninstalled. Requires administrator rights.
    /// </summary>
    void Uninstall(string klid);

    /// <summary>Lists what <see cref="Uninstall"/> would remove, for the confirmation dialog.</summary>
    IReadOnlyList<string> DescribeUninstall(string klid);

    /// <summary>
    /// Re-registers custom layouts that a Windows feature update removed and cleans up orphaned entries.
    /// Requires administrator rights.
    /// </summary>
    RepairReport Repair();

    /// <summary>
    /// Installs a layout for a limited time. A scheduled task runs <paramref name="cleanupExecutable"/>
    /// with "cleanup" when it expires, so the layout is removed even if the app is closed.
    /// </summary>
    InstalledLayout InstallTemporary(LayoutPackage package, TimeSpan duration, string cleanupExecutable);

    /// <summary>Turns a test install into a normal install.</summary>
    void Keep(string klid);

    /// <summary>Uninstalls test installs whose time is up. Returns what was removed.</summary>
    IReadOnlyList<string> CleanupExpired();
}
