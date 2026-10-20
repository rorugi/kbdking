using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Kbdking.Platform.Native;
using Microsoft.Win32;

namespace Kbdking.Platform;

/// <summary>
/// The current user's Win+Space list. Installing needs administrator rights, but the switcher belongs to the
/// user: changes made by the elevated process don't reliably reach it (with a separate administrator account
/// they land in that account's list). So the unelevated app or CLI applies them after an install, from the
/// lines the install printed, and removes entries whose layout no longer exists (Windows lists them under
/// the language name and can't select them).
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class UserSwitcher
{
    private const string LayoutsKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";

    [GeneratedRegex(@" as (?<klid>[0-9A-Fa-f]{8})\b")]
    private static partial Regex InstalledPattern();

    [GeneratedRegex(@"^(Replaced|Uninstalled) (?<klid>[0-9A-Fa-f]{8})\b", RegexOptions.Multiline)]
    private static partial Regex RemovedPattern();

    /// <summary>Applies the output of "kbdking install/uninstall" to the current user's switcher.</summary>
    public static void Apply(string output)
    {
        foreach (Match m in RemovedPattern().Matches(output)) Remove(m.Groups["klid"].Value);
        if (InstalledPattern().Match(output) is { Success: true } installed) Add(installed.Groups["klid"].Value);
        RemoveStale();
    }

    /// <summary>Adds a layout to the current user's switcher.</summary>
    public static bool Add(string klid) => InputApi.InstallLayoutOrTip(Tip(klid), InputApi.IlotInstall);

    /// <summary>Removes a layout from the current user's switcher (no error if it isn't there).</summary>
    public static void Remove(string klid) => InputApi.InstallLayoutOrTip(Tip(klid), InputApi.IlotUninstall);

    /// <summary>Removes switcher entries of custom layouts (A…) that are no longer installed. Returns how many.</summary>
    public static int RemoveStale()
    {
        using var root = Registry.LocalMachine.OpenSubKey(LayoutsKey);
        if (root is null) return 0;
        var installed = new HashSet<string>(root.GetSubKeyNames(), StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var tip in InputApi.GetEnabledLayouts())
        {
            var klid = tip.Split(':').Last();
            if (klid.Length != 8 || klid[0] is not ('A' or 'a') || installed.Contains(klid)) continue;
            if (InputApi.InstallLayoutOrTip(tip, InputApi.IlotUninstall)) removed++;
        }
        return removed;
    }

    /// <summary>"0C07:A0010C07": the language is the KLID's last four digits.</summary>
    private static string Tip(string klid) => $"{klid[^4..].ToUpperInvariant()}:{klid.ToUpperInvariant()}";
}
