namespace Kbdking.Core.Geometry;

/// <summary>
/// The same physical key in three systems: the PC scan code (Windows), the macOS virtual key code and
/// the XKB key name (Linux). macOS codes follow how macOS maps a PC (USB HID) keyboard.
/// </summary>
public static class PhysicalKeyCodes
{
    public sealed record Entry(int ScanCode, bool Extended, int? MacCode, string? XkbName);

    public static IReadOnlyList<Entry> All { get; } =
    [
        new(0x29, false, 50, "TLDE"),
        new(0x02, false, 18, "AE01"), new(0x03, false, 19, "AE02"), new(0x04, false, 20, "AE03"), new(0x05, false, 21, "AE04"),
        new(0x06, false, 23, "AE05"), new(0x07, false, 22, "AE06"), new(0x08, false, 26, "AE07"), new(0x09, false, 28, "AE08"),
        new(0x0A, false, 25, "AE09"), new(0x0B, false, 29, "AE10"), new(0x0C, false, 27, "AE11"), new(0x0D, false, 24, "AE12"),
        new(0x7D, false, 93, "AE13"),
        new(0x10, false, 12, "AD01"), new(0x11, false, 13, "AD02"), new(0x12, false, 14, "AD03"), new(0x13, false, 15, "AD04"),
        new(0x14, false, 17, "AD05"), new(0x15, false, 16, "AD06"), new(0x16, false, 32, "AD07"), new(0x17, false, 34, "AD08"),
        new(0x18, false, 31, "AD09"), new(0x19, false, 35, "AD10"), new(0x1A, false, 33, "AD11"), new(0x1B, false, 30, "AD12"),
        new(0x1E, false, 0, "AC01"), new(0x1F, false, 1, "AC02"), new(0x20, false, 2, "AC03"), new(0x21, false, 3, "AC04"),
        new(0x22, false, 5, "AC05"), new(0x23, false, 4, "AC06"), new(0x24, false, 38, "AC07"), new(0x25, false, 40, "AC08"),
        new(0x26, false, 37, "AC09"), new(0x27, false, 41, "AC10"), new(0x28, false, 39, "AC11"), new(0x2B, false, 42, "BKSL"),
        new(0x56, false, 10, "LSGT"),
        new(0x2C, false, 6, "AB01"), new(0x2D, false, 7, "AB02"), new(0x2E, false, 8, "AB03"), new(0x2F, false, 9, "AB04"),
        new(0x30, false, 11, "AB05"), new(0x31, false, 45, "AB06"), new(0x32, false, 46, "AB07"), new(0x33, false, 43, "AB08"),
        new(0x34, false, 47, "AB09"), new(0x35, false, 44, "AB10"), new(0x73, false, 94, "AB11"),
        new(0x39, false, 49, "SPCE"),
        new(0x53, false, 65, "KPDL"), new(0x37, false, 67, "KPMU"), new(0x4E, false, 69, "KPAD"), new(0x4A, false, 78, "KPSU"),
        new(0x35, true, 75, "KPDV"),
    ];

    public static Entry? ByScanCode(int scanCode, bool extended = false) => All.FirstOrDefault(e => e.ScanCode == scanCode && e.Extended == extended);
    public static Entry? ByMacCode(int macCode) => All.FirstOrDefault(e => e.MacCode == macCode);
    public static Entry? ByXkbName(string name) =>
        All.FirstOrDefault(e => e.XkbName == name) ?? (name == "AC12" ? ByScanCode(0x2B) : null);
}
