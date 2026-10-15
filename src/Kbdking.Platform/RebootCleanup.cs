using System.Runtime.Versioning;
using Kbdking.Platform.Native;

namespace Kbdking.Platform;

/// <summary>Files and folders that can't be deleted now (e.g. the running uninstaller) are removed at the next restart.</summary>
[SupportedOSPlatform("windows")]
public static class RebootCleanup
{
    /// <summary>Deletes the folder and its files at the next restart (files first, then the folder).</summary>
    public static void DeleteFolder(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Kernel32.MoveFileEx(file, null, Kernel32.MoveFileDelayUntilReboot); }
        }
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Kernel32.MoveFileEx(directory, null, Kernel32.MoveFileDelayUntilReboot); }
    }
}
