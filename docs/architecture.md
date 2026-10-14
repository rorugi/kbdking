# Architecture

All logic lives in UI-free libraries; the app, the CLI and the installer stub are thin front ends.

```
Keyservant.App (WinUI 3) ──┐
Keyservant.Cli ────────────┼──> Keyservant.Presentation (view models, app only)
Keyservant.Stub ───────────┘            │
                                         v
                            Keyservant.Platform (Windows integration)
                                         │
                                         v
                            Keyservant.Binary (PE writer/reader, packages)
                                         │
                                         v
                            Keyservant.Core (model, formats, validation)
```

## How Windows loads a layout

A layout is a data-only DLL exporting `KbdLayerDescriptor`, which returns a pointer to `KBDTABLES`
(defined in the WDK's `kbd.h`). When an app calls `LoadKeyboardLayout`, user32 loads the DLL in that
process, calls `KbdLayerDescriptor` and passes the table's offset to win32k, which maps the file as data
and copies its `.data` section. Two consequences shape the writer:

- Every table **and every string** must be in the section named `.data`.
- The pointer values in the file must be correct for the image's preferred base address, and every
  pointer needs a base relocation for user-mode loads.

The 32-bit DLL in `SysWOW64` uses 64-bit pointers inside its tables (`KBD_LONG_POINTER` = `__ptr64`),
because the kernel reads them; only its PE headers and code stub are 32-bit.

## The PE writer (`Keyservant.Binary`)

1. `KbdTablesPlan` turns the model into the exact rows of every kbd.h table (one character table for all
   keys plus the numpad table last, dead-key rows after their keys, SGCAPS rows, ligatures, dead keys,
   key names, scan codes).
2. `KbdTablesWriter` serializes the plan into a `.data` section, recording each pointer as a fixup.
3. `PeImageWriter` lays out `.text` (a 8- or 9-byte stub returning the table address), `.rdata` (export
   directory), `.data`, `.rsrc` (display-name string 1000 and version info) and `.reloc`, resolves fixups,
   writes headers and the PE checksum. Output is deterministic.

`KbdCSourceWriter` serializes the same plan as C. CI compiles it with clang-cl and lld-link and checks that
both DLLs read back identically; this guards the writer against layout mistakes.

## The reader

`LayoutDllReader` parses DLLs statically and never executes them: it decodes the few instructions of
`KbdLayerDescriptor` (x64 `lea rax,[rip+…]`, x86 `mov eax,imm32`, ARM64 `adrp/add`) to find `KBDTABLES`,
then follows pointers with bounds checks everywhere. A fuzz test feeds it corrupted files.

## Installing (`Keyservant.Platform`)

- Registry: `HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layouts\<KLID>` with `Layout File`,
  `Layout Text`, `Layout Display Name` (`@%SystemRoot%\system32\<dll>,-1000`), `Layout Id` and a
  `Keyservant` marker value.
- KLIDs for custom layouts: `Annn<LANGID>`, as MSKLC does.
- DLLs go to System32 and SysWOW64; in-use files are replaced or deleted at the next restart.
- The switcher is changed with `InstallLayoutOrTip` (input.dll), which needs no admin rights.
- A copy of each installed package is kept in `%ProgramData%\Keyservant\Layouts\<KLID>` so `repair` can
  restore layouts that a Windows feature update removed.

The app never runs elevated. Install, uninstall and repair run through `keyservant.exe` with a UAC prompt;
the elevated process writes its output to a temporary log that the app shows.
