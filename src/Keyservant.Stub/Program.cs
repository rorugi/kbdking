using System.Runtime.Versioning;
using Keyservant.Binary.Packaging;
using Keyservant.Platform;
using Microsoft.Win32;

namespace Keyservant.Stub;

/// <summary>
/// Standalone installer for one keyboard layout. Usage:
///   setup.exe               install, show the result and wait for a key
///   setup.exe /silent       install without output or prompts (for admins deploying to many PCs)
///   setup.exe /uninstall    remove the layout again (used by "Apps &amp; features")
/// </summary>
internal static class Program
{
    private const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static int Main(string[] args)
    {
        var silent = args.Any(a => a.Equals("/silent", StringComparison.OrdinalIgnoreCase) || a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));
        var uninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("This installer only runs on Windows.");
            return 1;
        }
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Can't find the installer file.");
            var package = LayoutPackage.ReadFromInstaller(exe)
                ?? throw new InvalidDataException("This installer contains no layout. Create one with 'keyservant package'.");
            var code = uninstall ? Uninstall(package, silent) : Install(package, exe, silent);
            Pause(silent);
            return code;
        }
        catch (Exception ex)
        {
            if (!silent) Console.Error.WriteLine($"Installation failed: {ex.Message}");
            Pause(silent);
            return 1;
        }
    }

    [SupportedOSPlatform("windows")]
    private static int Install(LayoutPackage package, string exe, bool silent)
    {
        if (!silent) Console.WriteLine($"Installing keyboard layout \"{package.Name}\"...");
        var installed = new WindowsLayoutSystem().Install(package);

        // Keep a copy of this installer as the uninstaller and register it in Apps & features.
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Keyservant", "Uninstall", installed.Klid);
        Directory.CreateDirectory(dir);
        var uninstaller = Path.Combine(dir, "uninstall.exe");
        if (!string.Equals(Path.GetFullPath(exe), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
            File.Copy(exe, uninstaller, overwrite: true);
        using (var key = Registry.LocalMachine.CreateSubKey($@"{UninstallRoot}\Keyservant-{installed.Klid}"))
        {
            key.SetValue("DisplayName", $"{package.Name} (keyboard layout)");
            key.SetValue("DisplayVersion", package.Version);
            key.SetValue("Publisher", "Keyservant");
            key.SetValue("UninstallString", $"\"{uninstaller}\" /uninstall");
            key.SetValue("QuietUninstallString", $"\"{uninstaller}\" /uninstall /silent");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (package.DllX64.Length + (package.DllWow64?.Length ?? 0)) / 1024 + 1, RegistryValueKind.DWord);
        }

        if (!silent)
        {
            Console.WriteLine($"Done. \"{installed.Name}\" is installed as {installed.Klid}.");
            Console.WriteLine("Switch to it with Win+Space. Apps that were already open may need a restart to see it.");
        }
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static int Uninstall(LayoutPackage package, bool silent)
    {
        var system = new WindowsLayoutSystem();
        var layout = system.GetLayouts().FirstOrDefault(l => l.IsCustom && l.LayoutFile.Equals(package.DllFileName, StringComparison.OrdinalIgnoreCase));
        if (layout is not null) system.Uninstall(layout.Klid);

        // Remove the "Apps & features" entry and the stored uninstaller even if the layout was already removed
        // another way (e.g. with 'keyservant uninstall'); otherwise the entry could never be removed.
        var uninstallDir = Path.GetDirectoryName(Path.GetFullPath(Environment.ProcessPath ?? ""));
        var klid = layout?.Klid
            ?? (uninstallDir is not null && Path.GetFileName(Path.GetDirectoryName(uninstallDir)) == "Uninstall" ? Path.GetFileName(uninstallDir) : null);
        if (klid is not null)
        {
            Registry.LocalMachine.DeleteSubKeyTree($@"{UninstallRoot}\Keyservant-{klid}", throwOnMissingSubKey: false);
            var stored = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Keyservant", "Uninstall", klid);
            RebootCleanup.DeleteFolder(stored);
        }
        if (!silent) Console.WriteLine(layout is null ? $"\"{package.Name}\" was not installed any more; removed its entry." : $"Removed \"{layout.Name}\".");
        return 0;
    }

    private static void Pause(bool silent)
    {
        if (silent || Console.IsInputRedirected) return;
        Console.WriteLine("Press any key to close.");
        Console.ReadKey(intercept: true);
    }
}
