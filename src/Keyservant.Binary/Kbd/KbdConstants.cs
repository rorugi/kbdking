namespace Keyservant.Binary.Kbd;

/// <summary>Values from Windows' kbd.h.</summary>
internal static class KbdConstants
{
    public const ushort WchNone = 0xF000;
    public const ushort WchDead = 0xF001;
    public const ushort WchLgtr = 0xF002;

    public const byte AttrCapLok = 0x01;
    public const byte AttrSgCaps = 0x02;

    public const ushort DkfDead = 0x0001;

    public const byte ContinuationVk = 0xFF;

    /// <summary>
    /// Size of pointers inside KBDTABLES. Always 8 for layouts we write: the 64-bit DLL uses native
    /// pointers and the 32-bit WOW64 DLL uses <c>__ptr64</c> (kbd.h: KBD_LONG_POINTER), because the
    /// kernel reads the tables of both.
    /// </summary>
    public const int TablePointerSize = 8;

    public const string ExportName = "KbdLayerDescriptor";
}

/// <summary>Byte offsets of KBDTABLES fields for a given pointer size.</summary>
internal readonly record struct KbdTablesLayout(int PointerSize)
{
    public int CharModifiers => 0;
    public int VkToWcharTable => P;
    public int DeadKey => 2 * P;
    public int KeyNames => 3 * P;
    public int KeyNamesExt => 4 * P;
    public int KeyNamesDead => 5 * P;
    public int VscToVk => 6 * P;
    public int MaxVscToVk => 7 * P;
    public int VscToVkE0 => 8 * P;
    public int VscToVkE1 => 9 * P;
    public int LocaleFlags => 10 * P;
    public int LgMax => 10 * P + 4;
    public int LgEntrySize => 10 * P + 5;
    public int Ligature => Align(10 * P + 6, P);
    public int Type => Ligature + P;
    public int SubType => Type + 4;
    public int Size => Align(SubType + 4, P);

    /// <summary>Size of a VK_TO_WCHAR_TABLE entry (pointer, two bytes, padding).</summary>
    public int WcharTableEntrySize => Align(P + 2, P);

    /// <summary>Size of a VSC_LPWSTR entry (byte, padding, pointer).</summary>
    public int KeyNameEntrySize => 2 * P;

    private int P => PointerSize;

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
