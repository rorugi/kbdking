using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Keyservant.Binary;
using Keyservant.Binary.Packaging;
using Keyservant.Core.Model;
using Keyservant.Platform;

namespace Keyservant.Tests;

/// <summary>
/// Runs only on Windows, as administrator, with KEYSERVANT_INSTALL_TESTS=1 (set in CI). These tests change
/// the machine: they install a layout, type with it through Windows itself and uninstall it again.
/// </summary>
public sealed class WindowsInstallFactAttribute : FactAttribute
{
    public WindowsInstallFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows only.";
        else if (Environment.GetEnvironmentVariable("KEYSERVANT_INSTALL_TESTS") != "1") Skip = "Set KEYSERVANT_INSTALL_TESTS=1 to run (changes this PC).";
        else if (!Elevation.IsAdministrator) Skip = "Needs administrator rights.";
    }
}

/// <summary>
/// The behavioural test from the plan: install the generated DLL, load it with LoadKeyboardLayout and
/// compare what ToUnicodeEx types for every key, shift state and dead-key sequence with the model.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class WindowsInstallTests
{
    private const uint KlfNotEllShell = 0x00000080;

    [LibraryImport("user32.dll", EntryPoint = "LoadKeyboardLayoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadKeyboardLayout(string klid, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnloadKeyboardLayout(nint hkl);

    [LibraryImport("user32.dll", EntryPoint = "ToUnicodeEx")]
    private static unsafe partial int ToUnicodeEx(uint vk, uint scanCode, byte* keyState, char* buffer, int bufferSize, uint flags, nint hkl);

    private static unsafe (int Result, string Text) Press(nint hkl, byte vk, ShiftState state, bool capsLock)
    {
        var keyState = stackalloc byte[256];
        if (state.Bits.HasFlag(ModifierBits.Shift)) keyState[0x10] = 0x80;
        if (state.Bits.HasFlag(ModifierBits.Ctrl)) keyState[0x11] = keyState[0xA2] = 0x80;
        if (state.Bits.HasFlag(ModifierBits.Alt)) keyState[0x12] = keyState[0xA5] = 0x80;
        if (capsLock) keyState[0x14] = 0x01;
        var buffer = stackalloc char[16];
        var n = ToUnicodeEx(vk, 0, keyState, buffer, 16, 0, hkl);
        return (n, n > 0 ? new string(buffer, 0, n) : "");
    }

    /// <summary>Clears a pending dead key in the thread's keyboard state.</summary>
    private static void Flush(nint hkl)
    {
        for (var i = 0; i < 2; i++) Press(hkl, VirtualKeys.Space, ShiftState.Base, false);
    }

    [WindowsInstallFact]
    public void Installed_layout_types_exactly_what_the_model_says()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        layout.Metadata.DllName = "kbdkstst";
        layout.Metadata.Name = "Keyservant test layout";
        var system = new WindowsLayoutSystem();
        var installed = system.Install(LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout)));
        try
        {
            var hkl = LoadKeyboardLayout(installed.Klid, KlfNotEllShell);
            Assert.NotEqual(0, hkl);
            try
            {
                var failures = new StringBuilder();
                foreach (var key in layout.Keys)
                {
                    // Ctrl states are skipped: Windows synthesizes Ctrl+letter control characters itself.
                    foreach (var state in layout.Modifiers.Columns.Where(s => !s.Bits.HasFlag(ModifierBits.Ctrl) || s.Bits.HasFlag(ModifierBits.Alt)))
                    {
                        foreach (var caps in new[] { false, true })
                        {
                            Flush(hkl);
                            var sim = new TypingSimulator(layout);
                            var expected = sim.Press(key.VirtualKey, state, caps);
                            var (result, text) = Press(hkl, key.VirtualKey, state, caps);
                            var expectedResult = sim.PendingDeadKey is not null ? -1 : expected.Length;
                            if (result != expectedResult || (result > 0 && text != expected))
                                failures.AppendLine($"{key.Name} {state} caps={caps}: Windows {result} \"{text}\", model {expectedResult} \"{expected}\"");
                        }
                    }
                }

                (byte, ShiftState)[][] sequences =
                [
                    [(0xBE, ShiftState.AltGr), ((byte)'T', ShiftState.Base)],                              // ṭ
                    [(0xBE, ShiftState.AltGr), (0xBD, ShiftState.AltGr), ((byte)'R', ShiftState.Base)],    // ṝ (chained)
                    [(0xDC, ShiftState.Base), ((byte)'A', ShiftState.Base)],                               // â
                    [(0xDC, ShiftState.Base), ((byte)'X', ShiftState.Base)],                               // ^x
                ];
                foreach (var sequence in sequences)
                {
                    Flush(hkl);
                    var sim = new TypingSimulator(layout);
                    var expected = string.Concat(sequence.Select(k => sim.Press(k.Item1, k.Item2)));
                    var actual = string.Concat(sequence.Select(k => Press(hkl, k.Item1, k.Item2, false).Text));
                    if (actual != expected) failures.AppendLine($"sequence {string.Join(" ", sequence)}: Windows \"{actual}\", model \"{expected}\"");
                }
                Assert.True(failures.Length == 0, failures.ToString());
            }
            finally
            {
                UnloadKeyboardLayout(hkl);
            }
        }
        finally
        {
            system.Uninstall(installed.Klid);
        }
    }
}
