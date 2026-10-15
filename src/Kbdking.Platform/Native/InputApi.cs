using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Kbdking.Platform.Native;

/// <summary>
/// The Windows input-profile API in input.dll: adds/removes layouts from a user's switcher.
/// https://learn.microsoft.com/windows/win32/tsf/installlayoutortip
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class InputApi
{
    public const uint IlotInstall = 0x0;
    public const uint IlotUninstall = 0x1;

    public const uint LotpKeyboardLayout = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct LayoutOrTipProfile
    {
        public uint ProfileType;
        public ushort LangId;
        public Guid Clsid;
        public Guid GuidProfile;
        public Guid CatId;
        public uint SubstituteLayout;
        public uint Flags;
        public fixed char Id[260];
    }

    [LibraryImport("input.dll", EntryPoint = "InstallLayoutOrTip", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InstallLayoutOrTip(string profile, uint flags);

    [LibraryImport("input.dll", EntryPoint = "EnumEnabledLayoutOrTip", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint EnumEnabledLayoutOrTip(string? userReg, string? systemReg, string? softwareReg,
        LayoutOrTipProfile* profiles, uint bufferLength);

    /// <summary>The "LANGID:KLID" strings of keyboard layouts enabled for the current user.</summary>
    public static IReadOnlyList<string> GetEnabledLayouts()
    {
        var count = EnumEnabledLayoutOrTip(null, null, null, null, 0);
        if (count == 0) return [];
        var buffer = new LayoutOrTipProfile[count];
        fixed (LayoutOrTipProfile* p = buffer)
        {
            count = EnumEnabledLayoutOrTip(null, null, null, p, count);
        }
        var result = new List<string>();
        for (var i = 0; i < count; i++)
        {
            if (buffer[i].ProfileType != LotpKeyboardLayout) continue;
            fixed (char* id = buffer[i].Id)
            {
                result.Add(new string(id));
            }
        }
        return result;
    }
}

[SupportedOSPlatform("windows")]
internal static partial class Kernel32
{
    public const uint MoveFileDelayUntilReboot = 0x4;

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveFileEx(string existingFileName, string? newFileName, uint flags);
}
