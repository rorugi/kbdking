using Kbdking.Platform;
using Kbdking.Presentation;

namespace Kbdking.App.Services;

/// <summary>
/// Runs install, uninstall and repair through kbdking.exe with elevation, so the app itself stays
/// unelevated and each action shows exactly one UAC prompt.
/// </summary>
internal sealed class CliPrivilegedOperations : IPrivilegedOperations
{
    private static string CliPath => Path.Combine(AppContext.BaseDirectory, "kbdking.exe");

    public Task<OperationResult> InstallAsync(string layoutFile) => RunAsync("install", layoutFile);

    public Task<OperationResult> UninstallAsync(string klid) => RunAsync("uninstall", klid, "--yes");

    public Task<OperationResult> RepairAsync() => RunAsync("repair");

    public Task<OperationResult> InstallTemporaryAsync(string layoutFile, int minutes) =>
        RunAsync("install", layoutFile, "--temporary", minutes.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public Task<OperationResult> KeepAsync(string klid) => RunAsync("keep", klid);

    private static Task<OperationResult> RunAsync(params string[] args) => Task.Run(() =>
    {
        if (!File.Exists(CliPath))
            return new OperationResult(false, $"kbdking.exe was not found next to the app ({AppContext.BaseDirectory}). Reinstall kbdking.");
        var log = Path.Combine(Path.GetTempPath(), $"kbdking-{Guid.NewGuid():N}.log");
        try
        {
            var code = Elevation.RunElevated(CliPath, [.. args, "--log", log]);
            var output = File.Exists(log) ? File.ReadAllText(log) : "";
            // The switcher belongs to this (unelevated) user; see UserSwitcher.
            if (code == 0)
            {
                try { UserSwitcher.Apply(output); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { App.Log(ex); }
            }
            return code is null
                ? new OperationResult(false, "", Cancelled: true)
                : new OperationResult(code == 0, output);
        }
        finally
        {
            if (File.Exists(log)) File.Delete(log);
        }
    });
}
