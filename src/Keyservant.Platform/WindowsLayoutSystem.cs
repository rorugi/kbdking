using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Runtime.Versioning;
using Keyservant.Binary.Packaging;
using Keyservant.Platform.Native;
using Microsoft.Win32;

namespace Keyservant.Platform;

/// <summary>
/// <see cref="ILayoutSystem"/> for the local Windows installation.
/// </summary>
/// <remarks>
/// Layouts are registered under <c>HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layouts\&lt;KLID&gt;</c>;
/// the DLLs live in System32 (64-bit) and SysWOW64 (32-bit apps). Custom layouts get a KLID of the form
/// <c>Annn&lt;LANGID&gt;</c> (as MSKLC does) and a <c>Keyservant</c> marker value. A copy of every installed
/// package is kept in <c>%ProgramData%\Keyservant\Layouts</c> so layouts can be restored after feature updates.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsLayoutSystem : ILayoutSystem
{
    private const string LayoutsKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";
    private const string MarkerValue = "Keyservant";
    private const string PackageFileName = "package.kslayout";
    internal const string TemporaryValue = "KeyservantTemporaryUntil";
    internal const string TaskFolder = "Keyservant";

    private readonly string _system32;
    private readonly string _sysWow64;
    private readonly string _store;

    public WindowsLayoutSystem()
        : this(Environment.SystemDirectory,
               Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Keyservant", "Layouts"))
    {
    }

    internal WindowsLayoutSystem(string system32, string sysWow64, string store)
    {
        _system32 = system32;
        _sysWow64 = sysWow64;
        _store = store;
    }

    public IReadOnlyList<InstalledLayout> GetLayouts()
    {
        var enabled = new HashSet<string>(InputApi.GetEnabledLayouts().Select(t => t.Split(':').Last()), StringComparer.OrdinalIgnoreCase);
        var result = new List<InstalledLayout>();
        using var root = Registry.LocalMachine.OpenSubKey(LayoutsKey) ?? throw new LayoutSystemException("The keyboard layout registry key is missing.");
        foreach (var klid in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(klid);
            if (key is null) continue;
            var file = key.GetValue("Layout File") as string ?? "";
            var isCustom = key.GetValue(MarkerValue) is not null;
            DateTimeOffset? temporaryUntil = key.GetValue(TemporaryValue) is string t
                && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until) ? until : null;
            string? problem = null;
            if (string.IsNullOrEmpty(file)) problem = "No layout DLL is registered.";
            else if (!File.Exists(Path.Combine(_system32, file))) problem = $"{file} is missing from System32.";
            else if (isCustom && Directory.Exists(_sysWow64) && !File.Exists(Path.Combine(_sysWow64, file))) problem = $"{file} is missing from SysWOW64; 32-bit apps can't use the layout.";

            result.Add(new InstalledLayout
            {
                Klid = klid.ToUpperInvariant(),
                LayoutFile = file,
                Name = ReadName(key, klid),
                LayoutId = key.GetValue("Layout Id") as string,
                IsCustom = isCustom,
                IsEnabled = enabled.Contains(klid),
                Problem = problem,
                TemporaryUntil = temporaryUntil,
            });
        }
        return [.. result.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static string ReadName(RegistryKey key, string klid)
    {
        if (key.GetValue("Layout Display Name") is string display && display.StartsWith('@'))
        {
            var resolved = ResourceStrings.Load(Environment.ExpandEnvironmentVariables(display));
            if (!string.IsNullOrEmpty(resolved)) return resolved;
        }
        return key.GetValue("Layout Text") as string ?? klid;
    }

    public void Enable(InstalledLayout layout)
    {
        if (!InputApi.InstallLayoutOrTip(layout.InputTip(), InputApi.IlotInstall))
            throw new LayoutSystemException($"Windows refused to add {layout.Name} to the switcher.");
    }

    public void Disable(InstalledLayout layout)
    {
        if (!InputApi.InstallLayoutOrTip(layout.InputTip(), InputApi.IlotUninstall))
            throw new LayoutSystemException($"Windows refused to remove {layout.Name} from the switcher.");
    }

    public InstalledLayout Install(LayoutPackage package)
    {
        Elevation.RequireAdministrator("Installing a layout");
        using var root = Registry.LocalMachine.OpenSubKey(LayoutsKey, writable: true)
            ?? throw new LayoutSystemException("The keyboard layout registry key is missing.");

        // Reuse the KLID if this layout was installed before; otherwise allocate a new one.
        var klid = package.LayoutId?.ToUpperInvariant();
        if (klid is not null && root.OpenSubKey(klid) is { } existing)
        {
            using (existing)
            {
                if (existing.GetValue(MarkerValue) is null)
                    throw new LayoutSystemException($"Layout ID {klid} belongs to a Windows layout; Keyservant won't overwrite it.");
            }
        }
        klid ??= AllocateKlid(root, package.LanguageId);

        // Never overwrite a DLL that belongs to another layout.
        var owner = FindLayoutUsingFile(root, package.DllFileName);
        if (owner is not null && !owner.Equals(klid, StringComparison.OrdinalIgnoreCase))
            throw new LayoutSystemException($"{package.DllFileName} is already used by layout {owner}. Choose a different DLL name.");
        if (owner is null && File.Exists(Path.Combine(_system32, package.DllFileName)))
            throw new LayoutSystemException($"{package.DllFileName} already exists in System32 and isn't a Keyservant layout. Choose a different DLL name.");

        // System32 gets the DLL of the machine's own architecture; SysWOW64 the 32-bit one.
        var native = RuntimeInformation.OSArchitecture == Architecture.Arm64 && package.DllArm64 is not null ? package.DllArm64 : package.DllX64;
        CopyDll(native, Path.Combine(_system32, package.DllFileName));
        if (package.DllWow64 is not null && Directory.Exists(_sysWow64))
            CopyDll(package.DllWow64, Path.Combine(_sysWow64, package.DllFileName));

        using (var key = root.CreateSubKey(klid, writable: true))
        {
            key.SetValue("Layout File", package.DllFileName);
            key.SetValue("Layout Text", package.Name);
            key.SetValue("Layout Display Name", $"@%SystemRoot%\\system32\\{package.DllFileName},-1000", RegistryValueKind.ExpandString);
            if (key.GetValue("Layout Id") is not string)
                key.SetValue("Layout Id", AllocateLayoutId(root));
            key.SetValue(MarkerValue, package.Version);
            key.DeleteValue(TemporaryValue, throwOnMissingValue: false);
        }

        SavePackage(klid, package);
        var installed = GetLayouts().First(l => l.Klid.Equals(klid, StringComparison.OrdinalIgnoreCase));
        if (!installed.IsEnabled) Enable(installed);
        return installed with { IsEnabled = true };
    }

    public IReadOnlyList<string> DescribeUninstall(string klid)
    {
        var layout = FindCustom(klid);
        var items = new List<string> { $"Remove \"{layout.Name}\" from the switcher" };
        items.Add($@"Delete registry key HKLM\{LayoutsKey}\{layout.Klid}");
        items.Add(Path.Combine(_system32, layout.LayoutFile));
        if (File.Exists(Path.Combine(_sysWow64, layout.LayoutFile))) items.Add(Path.Combine(_sysWow64, layout.LayoutFile));
        if (Directory.Exists(Path.Combine(_store, layout.Klid))) items.Add(Path.Combine(_store, layout.Klid));
        return items;
    }

    public void Uninstall(string klid)
    {
        Elevation.RequireAdministrator("Uninstalling a layout");
        var layout = FindCustom(klid);
        if (layout.IsEnabled)
        {
            // Other user accounts keep a stale switcher entry, which Windows ignores once the layout is gone.
            try { Disable(layout); }
            catch (LayoutSystemException) { /* still remove the files */ }
        }

        using (var root = Registry.LocalMachine.OpenSubKey(LayoutsKey, writable: true))
        {
            root?.DeleteSubKeyTree(layout.Klid, throwOnMissingSubKey: false);
        }
        DeleteFile(Path.Combine(_system32, layout.LayoutFile));
        DeleteFile(Path.Combine(_sysWow64, layout.LayoutFile));
        var stored = Path.Combine(_store, layout.Klid);
        if (Directory.Exists(stored)) Directory.Delete(stored, recursive: true);
    }

    public RepairReport Repair()
    {
        Elevation.RequireAdministrator("Repairing layouts");
        var report = new RepairReport();
        using var root = Registry.LocalMachine.OpenSubKey(LayoutsKey, writable: true)
            ?? throw new LayoutSystemException("The keyboard layout registry key is missing.");

        // 1. Stored packages whose registration or DLLs went missing (typical after a feature update).
        if (Directory.Exists(_store))
        {
            foreach (var dir in Directory.GetDirectories(_store))
            {
                var klid = Path.GetFileName(dir);
                var packagePath = Path.Combine(dir, PackageFileName);
                if (!File.Exists(packagePath)) continue;
                LayoutPackage package;
                try
                {
                    using var s = File.OpenRead(packagePath);
                    package = LayoutPackage.ReadFrom(s);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException)
                {
                    report.Problems.Add($"Stored package for {klid} is damaged: {ex.Message}");
                    continue;
                }

                using var key = root.OpenSubKey(klid);
                var dll64 = Path.Combine(_system32, package.DllFileName);
                var dll32 = Path.Combine(_sysWow64, package.DllFileName);
                var needsDll = !File.Exists(dll64) || package.DllWow64 is not null && Directory.Exists(_sysWow64) && !File.Exists(dll32);
                if (key is null || needsDll)
                {
                    Install(new LayoutPackage
                    {
                        Name = package.Name, DllFileName = package.DllFileName, Locale = package.Locale,
                        LanguageId = package.LanguageId, Version = package.Version, LayoutId = klid,
                        DllX64 = package.DllX64, DllWow64 = package.DllWow64, DllArm64 = package.DllArm64, LayoutSource = package.LayoutSource,
                    });
                    report.Fixed.Add($"Restored {package.Name} ({klid}).");
                }
            }
        }

        // 2. Keyservant registrations without DLL and without stored package: nothing to restore from.
        foreach (var klid in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(klid);
            if (key?.GetValue(MarkerValue) is null) continue;
            var file = key.GetValue("Layout File") as string;
            if (!string.IsNullOrEmpty(file) && File.Exists(Path.Combine(_system32, file))) continue;
            if (File.Exists(Path.Combine(_store, klid, PackageFileName))) continue;
            root.DeleteSubKeyTree(klid, throwOnMissingSubKey: false);
            report.Fixed.Add($"Removed orphaned registration {klid} (its DLL and package were gone).");
        }
        return report;
    }

    // ---------------------------------------------------------------- temporary installs

    public InstalledLayout InstallTemporary(LayoutPackage package, TimeSpan duration, string cleanupExecutable)
    {
        var installed = Install(package);
        var until = DateTimeOffset.UtcNow + duration;
        using (var key = Registry.LocalMachine.OpenSubKey($@"{LayoutsKey}\{installed.Klid}", writable: true))
        {
            key?.SetValue(TemporaryValue, until.ToString("O", CultureInfo.InvariantCulture));
        }
        ScheduleCleanup(installed.Klid, until, cleanupExecutable);
        return installed with { TemporaryUntil = until };
    }

    public void Keep(string klid)
    {
        Elevation.RequireAdministrator("Keeping a layout");
        var layout = FindCustom(klid);
        using (var key = Registry.LocalMachine.OpenSubKey($@"{LayoutsKey}\{layout.Klid}", writable: true))
        {
            key?.DeleteValue(TemporaryValue, throwOnMissingValue: false);
        }
        DeleteCleanupTask(layout.Klid);
    }

    public IReadOnlyList<string> CleanupExpired()
    {
        Elevation.RequireAdministrator("Removing expired test installs");
        var removed = new List<string>();
        foreach (var layout in GetLayouts().Where(l => l.IsCustom && l.TemporaryUntil is { } u && u <= DateTimeOffset.UtcNow))
        {
            Uninstall(layout.Klid);
            DeleteCleanupTask(layout.Klid);
            removed.Add($"{layout.Name} ({layout.Klid})");
        }
        return removed;
    }

    /// <summary>
    /// Registers a one-time scheduled task (as SYSTEM) that runs "keyservant cleanup" when the test install
    /// expires, so the layout is reverted even if the app is closed or the PC restarted in between.
    /// The task is defined in XML, which avoids locale-dependent date formats on the command line.
    /// </summary>
    private static void ScheduleCleanup(string klid, DateTimeOffset at, string executable)
    {
        var start = at.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var xml = $$"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Reverts the Keyservant test install of keyboard layout {{klid}}.</Description></RegistrationInfo>
              <Triggers><TimeTrigger><StartBoundary>{{start}}</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>
              <Principals><Principal id="System"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <StartWhenAvailable>true</StartWhenAvailable>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
              </Settings>
              <Actions Context="System"><Exec><Command>{{SecurityElement.Escape(executable)}}</Command><Arguments>cleanup</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"keyservant-task-{klid}.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            RunSchtasks(["/Create", "/TN", $@"{TaskFolder}\Revert {klid}", "/XML", file, "/F"]);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void DeleteCleanupTask(string klid)
    {
        try { RunSchtasks(["/Delete", "/TN", $@"{TaskFolder}\Revert {klid}", "/F"]); }
        catch (LayoutSystemException) { /* the task may not exist */ }
    }

    private static void RunSchtasks(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new LayoutSystemException("Could not start schtasks.exe.");
        var error = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new LayoutSystemException($"schtasks failed: {error.Trim()}");
    }

    // ---------------------------------------------------------------- helpers

    private InstalledLayout FindCustom(string klid)
    {
        var layout = GetLayouts().FirstOrDefault(l => l.Klid.Equals(klid, StringComparison.OrdinalIgnoreCase))
            ?? throw new LayoutSystemException($"No layout with ID {klid} is installed.");
        if (!layout.IsCustom)
            throw new LayoutSystemException($"{layout.Name} is part of Windows. It can be removed from the switcher, but not uninstalled.");
        return layout;
    }

    private static string AllocateKlid(RegistryKey root, ushort languageId) => LayoutIds.AllocateKlid(root.GetSubKeyNames(), languageId);

    private static string AllocateLayoutId(RegistryKey root) =>
        LayoutIds.AllocateLayoutId(root.GetSubKeyNames().Select(k =>
        {
            using var key = root.OpenSubKey(k);
            return key?.GetValue("Layout Id") as string;
        }).ToList());

    private static string? FindLayoutUsingFile(RegistryKey root, string file)
    {
        foreach (var klid in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(klid);
            if (string.Equals(key?.GetValue("Layout File") as string, file, StringComparison.OrdinalIgnoreCase)) return klid;
        }
        return null;
    }

    private static void CopyDll(byte[] bytes, string path)
    {
        try
        {
            File.WriteAllBytes(path, bytes);
        }
        catch (IOException)
        {
            // The old DLL is loaded by running processes: write next to it and swap it in at reboot.
            var pending = path + ".new";
            File.WriteAllBytes(pending, bytes);
            if (!Kernel32.MoveFileEx(pending, path, Kernel32.MoveFileDelayUntilReboot | 0x1 /*REPLACE_EXISTING*/))
                throw new LayoutSystemException($"{Path.GetFileName(path)} is in use and could not be replaced.");
        }
    }

    private static void DeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Loaded by a running process: delete at the next restart.
            Kernel32.MoveFileEx(path, null, Kernel32.MoveFileDelayUntilReboot);
        }
    }

    private void SavePackage(string klid, LayoutPackage package)
    {
        var dir = Path.Combine(_store, klid);
        Directory.CreateDirectory(dir);
        using var s = File.Create(Path.Combine(dir, PackageFileName));
        package.WriteTo(s);
    }
}
