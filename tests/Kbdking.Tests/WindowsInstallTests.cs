using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Kbdking.Binary;
using Kbdking.Binary.Packaging;
using Kbdking.Core.Compose;
using Kbdking.Core.Model;
using Kbdking.Platform;

namespace Kbdking.Tests;

/// <summary>
/// Runs only on Windows, as administrator, with KBDKING_INSTALL_TESTS=1 (set in CI). These tests change
/// the machine: they install a layout, type with it through Windows itself and uninstall it again.
/// </summary>
public sealed class WindowsInstallFactAttribute : FactAttribute
{
    public WindowsInstallFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows only.";
        else if (Environment.GetEnvironmentVariable("KBDKING_INSTALL_TESTS") != "1") Skip = "Set KBDKING_INSTALL_TESTS=1 to run (changes this PC).";
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

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    private static partial uint MapVirtualKeyEx(uint code, uint mapType, nint hkl);

    [LibraryImport("user32.dll", EntryPoint = "ToUnicodeEx")]
    private static unsafe partial int ToUnicodeEx(uint vk, uint scanCode, byte* keyState, char* buffer, int bufferSize, uint flags, nint hkl);

    private static unsafe (int Result, string Text) Press(nint hkl, byte vk, ShiftState state, bool capsLock, byte heldKey = 0)
    {
        var keyState = stackalloc byte[256];
        if (heldKey != 0) keyState[heldKey] = 0x80;
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
        layout.Metadata.Name = "kbdking test layout";
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

    [WindowsInstallFact]
    public void Compose_sequences_type_through_windows()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        layout.Metadata.DllName = "kbdkscmp";
        layout.Metadata.Name = "kbdking compose test";
        layout.Compose = new ComposeDefinition();
        layout.Compose.Sequences.Add(new ComposeSequence(".r", "ṛ"));
        layout.Compose.Sequences.Add(new ComposeSequence("--", "–"));
        var composeVk = ComposeCompiler.Compile(layout).Layout.ScanCodes.E0[0x5D].VirtualKey;
        var system = new WindowsLayoutSystem();
        var installed = system.Install(LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout)));
        try
        {
            var hkl = LoadKeyboardLayout(installed.Klid, KlfNotEllShell);
            Assert.NotEqual(0, hkl);
            try
            {
                Flush(hkl);
                var dotBelow = string.Concat(new[] { composeVk, (byte)0xBE, (byte)'R' }.Select(vk => Press(hkl, vk, ShiftState.Base, false).Text));
                Flush(hkl);
                var dash = string.Concat(new[] { composeVk, (byte)0xBD, (byte)0xBD }.Select(vk => Press(hkl, vk, ShiftState.Base, false).Text));
                Assert.Equal("ṛ", dotBelow);
                Assert.Equal("–", dash);
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

    [WindowsInstallFact]
    public void Test_installs_schedule_their_own_removal()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        layout.Metadata.DllName = "kbdkstmp";
        layout.Metadata.Name = "kbdking temporary test";
        var system = new WindowsLayoutSystem();
        var package = LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout));
        var installed = system.InstallTemporary(package, TimeSpan.FromMinutes(30), Environment.ProcessPath!);
        try
        {
            Assert.NotNull(system.GetLayouts().Single(l => l.Klid == installed.Klid).TemporaryUntil);
            Assert.True(TaskExists(installed.Klid));
            Assert.Empty(system.CleanupExpired()); // not expired yet

            system.Keep(installed.Klid);
            Assert.Null(system.GetLayouts().Single(l => l.Klid == installed.Klid).TemporaryUntil);
            Assert.False(TaskExists(installed.Klid));

            // Expired test installs are removed by the cleanup run.
            system.Uninstall(installed.Klid);
            installed = system.InstallTemporary(package, TimeSpan.FromMinutes(30), Environment.ProcessPath!);
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\{installed.Klid}", writable: true)!)
                key.SetValue(WindowsLayoutSystem.TemporaryValue, DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Single(system.CleanupExpired());
            Assert.DoesNotContain(system.GetLayouts(), l => l.Klid == installed.Klid);
            Assert.False(TaskExists(installed.Klid));
        }
        finally
        {
            if (system.GetLayouts().Any(l => l.Klid == installed.Klid)) system.Uninstall(installed.Klid);
        }
    }

    /// <summary>
    /// Windows caches layout names by DLL and string ID; a renamed layout must still show its new name
    /// (in the language switcher and here, which both resolve "Layout Display Name").
    /// </summary>
    [WindowsInstallFact]
    public void Renamed_layouts_show_their_new_name()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        layout.Metadata.DllName = "kbdksnam";
        layout.Metadata.Name = "kbdking name test A";
        var system = new WindowsLayoutSystem();
        var installed = system.Install(LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout)));
        var current = installed.Klid;
        try
        {
            Assert.Equal("kbdking name test A", system.GetLayouts().Single(l => l.Klid == installed.Klid).Name);
            layout.Metadata.Name = "kbdking name test B";
            layout.Metadata.LayoutId = installed.Klid;
            var again = system.Install(LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout)));
            current = again.Klid;
            // A fresh ID replaces the old one, so Windows shows the new name at once.
            Assert.NotEqual(installed.Klid, again.Klid);
            Assert.DoesNotContain(system.GetLayouts(), l => l.Klid == installed.Klid);
            Assert.Equal("kbdking name test B", system.GetLayouts().Single(l => l.Klid == again.Klid).Name);
        }
        finally
        {
            system.Uninstall(current);
        }
    }

    /// <summary>
    /// Windows keeps a loaded layout until sign-out. An update must still type the new characters right away:
    /// load version 1 in this session, install version 2, and version 2 types its own character.
    /// </summary>
    [WindowsInstallFact]
    public void Updated_layouts_type_the_new_characters_without_signing_out()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        layout.Metadata.DllName = "kbdksupd";
        layout.Metadata.Name = "kbdking update test";
        layout.SetOutput((byte)'Q', ShiftState.AltGr, KeyOutput.Of("ā"));
        var system = new WindowsLayoutSystem();
        var first = system.Install(LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout)));
        var current = first.Klid;
        try
        {
            var hkl1 = LoadKeyboardLayout(first.Klid, KlfNotEllShell);
            Assert.NotEqual(0, hkl1);
            Assert.Equal("ā", Press(hkl1, (byte)'Q', ShiftState.AltGr, false).Text);

            layout.SetOutput((byte)'Q', ShiftState.AltGr, KeyOutput.Of("ă"));
            layout.Metadata.LayoutId = first.Klid;
            var second = system.Install(LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout)));
            current = second.Klid;
            Assert.NotEqual(first.LayoutFile, second.LayoutFile);
            var hkl2 = LoadKeyboardLayout(second.Klid, KlfNotEllShell);
            Assert.NotEqual(0, hkl2);
            Assert.NotEqual(hkl1, hkl2);
            Assert.Equal("ă", Press(hkl2, (byte)'Q', ShiftState.AltGr, false).Text);
            UnloadKeyboardLayout(hkl1);
            UnloadKeyboardLayout(hkl2);
            Assert.DoesNotContain(system.GetLayouts(), l => l.Klid == first.Klid);
        }
        finally
        {
            system.Uninstall(current);
        }
    }

    private static bool TaskExists(string klid)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("schtasks.exe", ["/Query", "/TN", $@"{WindowsLayoutSystem.TaskFolder}\Revert {klid}"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    [WindowsInstallFact]
    public void Layer_keys_and_qwerty_shortcuts_work_through_windows()
    {
        var layout = TestLayouts.Layered();
        layout.Metadata.DllName = "kbdkslyr";
        layout.Metadata.Name = "kbdking layer test";
        Core.Editing.ShortcutPositions.Apply(layout, Core.Editing.ShortcutMode.Qwerty);
        var layerVk = Core.Editing.LayerKeys.Find(layout).First(k => k.Layer == ModifierBits.LayerA).VirtualKey;
        var system = new WindowsLayoutSystem();
        var installed = system.Install(LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout)));
        try
        {
            var hkl = LoadKeyboardLayout(installed.Klid, KlfNotEllShell);
            Assert.NotEqual(0, hkl);
            try
            {
                // Caps Lock sends the layer key's virtual key; holding it types the Greek layer.
                Assert.Equal(layerVk, MapVirtualKeyEx(0x3A, 1 /* MAPVK_VSC_TO_VK */, hkl));
                Flush(hkl);
                Assert.Equal("α", Press(hkl, (byte)'A', ShiftState.Base, false, layerVk).Text);
                Assert.Equal("Α", Press(hkl, (byte)'A', ShiftState.Shift, false, layerVk).Text);
                Assert.Equal("a", Press(hkl, (byte)'A', ShiftState.Base, false).Text);

                // QWERTY shortcuts: the key next to T sends VK_Y (Ctrl+Y there) but still types z.
                Assert.Equal((uint)'Y', MapVirtualKeyEx(0x15, 1, hkl));
                Assert.Equal("z", Press(hkl, (byte)'Y', ShiftState.Base, false).Text);
                Assert.Equal("\u0019", Press(hkl, (byte)'Y', ShiftState.Ctrl, false).Text);
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

    /// <summary>
    /// The Copilot key sends Left Windows + Left Shift + F23 (scan code 6E). With 6E as the compose key, the layout
    /// turns it into the compose dead key, and Win/Shift held by the keyboard don't get in the way.
    /// </summary>
    [WindowsInstallFact]
    public void Copilot_key_works_as_compose_key()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        layout.Metadata.DllName = "kbdkscop";
        layout.Metadata.Name = "kbdking Copilot compose test";
        layout.Compose = new ComposeDefinition { ScanCode = 0x6E, Extended = false };
        layout.Compose.Sequences.Add(new ComposeSequence(".r", "ṛ"));
        var system = new WindowsLayoutSystem();
        var installed = system.Install(LayoutPackage.Create(layout, LayoutDllBuilder.Build(layout)));
        try
        {
            var hkl = LoadKeyboardLayout(installed.Klid, KlfNotEllShell);
            Assert.NotEqual(0, hkl);
            try
            {
                var composeVk = (byte)MapVirtualKeyEx(0x6E, 1 /* MAPVK_VSC_TO_VK */, hkl);
                Assert.NotEqual(0x86, composeVk); // not VK_F23 any more
                Flush(hkl);
                var first = Press(hkl, composeVk, ShiftState.Shift, false, heldKey: 0x5B); // Copilot: LWin + LShift held
                Assert.Equal(-1, first.Result);                                            // a dead key
                var text = Press(hkl, 0xBE, ShiftState.Base, false).Text + Press(hkl, (byte)'R', ShiftState.Base, false).Text;
                Assert.Equal("ṛ", text);
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
