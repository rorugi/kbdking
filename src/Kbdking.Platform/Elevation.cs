using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Kbdking.Platform;

/// <summary>Administrator checks and running a helper with elevation (one UAC prompt per action).</summary>
[SupportedOSPlatform("windows")]
public static class Elevation
{
    public static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static void RequireAdministrator(string action)
    {
        if (!IsAdministrator) throw new LayoutSystemException($"{action} requires administrator rights.");
    }

    /// <summary>
    /// Runs <paramref name="exe"/> elevated and waits for it. Returns the exit code, or null if the user
    /// declined the UAC prompt.
    /// </summary>
    public static int? RunElevated(string exe, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi) ?? throw new LayoutSystemException($"Could not start {exe}.");
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: UAC declined
        {
            return null;
        }
    }
}

/// <summary>Resolves "@file.dll,-id" resource references (as used in "Layout Display Name").</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class ResourceStrings
{
    [LibraryImport("shlwapi.dll", EntryPoint = "SHLoadIndirectString", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHLoadIndirectString(string source, char* buffer, uint bufferLength, nint reserved);

    public static string? Load(string reference)
    {
        var buffer = stackalloc char[512];
        return SHLoadIndirectString(reference, buffer, 512, 0) == 0 ? new string(buffer) : null;
    }
}
