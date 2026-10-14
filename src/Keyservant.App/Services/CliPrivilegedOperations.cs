using Keyservant.Platform;
using Keyservant.Presentation;

namespace Keyservant.App.Services;

/// <summary>
/// Runs install, uninstall and repair through keyservant.exe with elevation, so the app itself stays
/// unelevated and each action shows exactly one UAC prompt.
/// </summary>
internal sealed class CliPrivilegedOperations : IPrivilegedOperations
{
    private static string CliPath => Path.Combine(AppContext.BaseDirectory, "keyservant.exe");

    public Task<OperationResult> InstallAsync(string layoutFile) => RunAsync("install", layoutFile);

    public Task<OperationResult> UninstallAsync(string klid) => RunAsync("uninstall", klid, "--yes");

    public Task<OperationResult> RepairAsync() => RunAsync("repair");

    private static Task<OperationResult> RunAsync(params string[] args) => Task.Run(() =>
    {
        if (!File.Exists(CliPath))
            return new OperationResult(false, $"keyservant.exe was not found next to the app ({AppContext.BaseDirectory}). Reinstall Keyservant.");
        var log = Path.Combine(Path.GetTempPath(), $"keyservant-{Guid.NewGuid():N}.log");
        try
        {
            var code = Elevation.RunElevated(CliPath, [.. args, "--log", log]);
            var output = File.Exists(log) ? File.ReadAllText(log) : "";
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
