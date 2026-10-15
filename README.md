# Keyservant

Keyservant creates, edits and installs Windows keyboard layouts. It imports an existing layout, lets you
change any key, dead key or ligature, and builds a real Windows layout DLL, so the result works in every
app with nothing running in the background. Free and open source (MIT).

> **Status: v0.1, early development.** The engine, file formats, DLL writer and CLI are implemented and
> tested; the Windows app and the installer are first versions that still need testing on real PCs.
> See [Status](#status) before using it on a machine you care about.

## Features

- **Layout manager**: list every layout on the PC, add or remove it from the Win+Space switcher with a
  toggle, install custom layouts, uninstall them completely, and repair layouts lost in Windows updates.
- **Editor**: on-screen ISO, ANSI, JIS and ABNT keyboards, every shift state (Shift, AltGr, Ctrl, Kana, …)
  with a shift-state manager, dead keys including **chained dead keys**, ligatures (several characters per
  key, including characters outside the BMP) with their own list, Caps Lock behaviour and SGCAPS, key
  names, a low-level scan-code editor, **undo/redo**, and a live preview that types with the layout you
  are editing before you install it.
- **Shortcuts stay in place**: one setting keeps Ctrl+C, Ctrl+V, Ctrl+Z… at their QWERTY (or QWERTZ,
  AZERTY) positions while the keys type Colemak, Dvorak, Neo or your own arrangement.
- **Layer keys**: turn Caps Lock, the < > key, Menu or any other key into an extra Shift that switches to
  a layer of your own (Greek, symbols, IAST…), up to three layers, each also with Shift. Built into the
  layout, no background program.
- **Unicode picker**: find any character by name (“a macron”, “dot below”), by code or by pasting it.
  Works offline (Unicode 15.1 names are built in).
- **Placement assistant**: give it the characters you need (or a sample text) and it proposes dead keys for
  shared accents, look-alike keys for the rest and chained dead keys for double accents, while keeping
  what you already have. Presets for IAST, Pinyin, Vietnamese, Polish and more.
- **Compose key**: sequences like Compose . r → ṛ, compiled into the layout itself (no background
  program). Imports `.XCompose` and WinCompose files.
- **Optimize for real text**: paste a text you type and see the typing effort per character, a heatmap
  and what can't be typed; compare two layouts on the same text.
- **Layout tests**: “AltGr+. t must type ṭ”, saved in the file and checked after every change.
- **Compare**: the differences between two layouts, listed and marked on the keyboard.
- **Test installs**: install a layout for a set time; Windows removes it again automatically, even if
  Keyservant is closed, unless you choose *Keep*.
- **Printouts**: keyboard pictures and key stickers (SVG, real size).
- **Formats**: `.keyservant` (readable JSON, diff-friendly), MSKLC `.klc`, Windows layout DLLs, macOS
  `.keylayout`, Linux XKB symbols (plus a `.XCompose` file for dead keys), and C source.
- **Builds without a compiler**: Keyservant writes the PE file itself, for x64, ARM64 and the 32-bit WOW64
  DLL that 32-bit apps use. No Visual Studio, WDK or MSKLC needed, and builds are reproducible.
- **Deployment**: a small standalone `setup.exe` for any Windows PC (with an entry in *Apps & features*),
  an MSI for Group Policy and Intune, and a ready-to-wrap Intune Win32 app folder with a detection script.

## Quick start (CLI)

```powershell
keyservant new de-AT -o my-layout.keyservant --name "German + IAST" --dll kbdiast
# edit my-layout.keyservant, or open it in the Keyservant app
keyservant validate my-layout.keyservant
keyservant install my-layout.keyservant        # asks for admin rights once
keyservant list --custom
keyservant uninstall A0000C07
```

More commands:

```powershell
keyservant install my-layout.keyservant --temporary 30   # test install, reverts itself after 30 minutes
keyservant keep A0000C07                                 # ...or keep it
keyservant suggest my-layout.keyservant --chars "āīūṛṝḷṅñṭḍṇśṣṃḥ" --apply
keyservant compose import my-layout.keyservant ~/.XCompose
keyservant test my-layout.keyservant
keyservant analyze my-layout.keyservant mytext.txt --heatmap heat.svg --compare other.keyservant
keyservant diff old.keyservant my-layout.keyservant --svg changes.svg
keyservant render my-layout.keyservant -o stickers.svg --stickers
keyservant convert my-layout.keyservant my-layout.keylayout      # macOS; .xkb for Linux
keyservant package my-layout.keyservant --msi my-layout.msi       # needs WiX: dotnet tool install -g wix
keyservant package my-layout.keyservant --intune intune-folder
keyservant unicode a macron
keyservant shortcuts colemak.keyservant qwerty                    # Ctrl+C/V/Z stay at QWERTY positions
keyservant layers my-layout.keyservant add 3A A                  # Caps Lock selects Layer A while held
```

Also: `templates`, `build` (DLLs only), `enable`/`disable` (switcher), `import` (save an installed layout
as a `.keyservant` file) and `repair`. Run `keyservant help` for details.

The sample [`samples/german-austria-iast.keyservant`](samples/german-austria-iast.keyservant) adds IAST
transliteration for Sanskrit to the Austrian German layout: AltGr+a/i/u → ā ī ū, AltGr+s → ś,
AltGr+g → ṅ, AltGr+n → ñ, AltGr+r → √ (root sign, as in √gam), a dot-below dead key on AltGr+. (ṭ ḍ ṇ ṣ ṛ ḷ ṃ ḥ) and a macron dead key on
AltGr+- that chain into ṝ and ḹ.

## The .keyservant format

UTF-8 JSON with one key per line:

```json
{"vk": "A", "attrs": ["capslock"], "base": "a", "shift": "A", "altgr": "ā", "shift+altgr": "Ā"},
{"vk": "OEM_PERIOD", "base": ".", "shift": ":", "altgr": {"dead": "̣"}},
```

Invisible characters (control characters, combining marks) are written as `\u` escapes, everything else
literally. Full description: [docs/file-format.md](docs/file-format.md).

## Building from source

Requirements: .NET 10 SDK. The engine, CLI and tests build on Windows, Linux and macOS; the app and the
installer stub need Windows.

```bash
dotnet test tests/Keyservant.Tests          # all platforms
dotnet run --project src/Keyservant.Cli -- help
dotnet build src/Keyservant.App -p:Platform=x64   # Windows only
```

If `clang-cl` and `lld-link` are on the `PATH`, the tests also compile every test layout as C with clang
and check that Keyservant's own DLL writer produces the same tables.

## Project structure

| Project | Purpose |
| --- | --- |
| `Keyservant.Core` | Layout model, formats (`.keyservant`, `.klc`, `.keylayout`, XKB), validator, templates, typing simulator, compose compiler, assistant, analysis, diff, SVG rendering, Unicode names |
| `Keyservant.Binary` | PE writer and reader for layout DLLs (x64, WOW64, ARM64), C source export, layout packages, MSI and Intune packaging |
| `Keyservant.Platform` | Windows integration: registry, switcher, install, uninstall, repair |
| `Keyservant.Presentation` | View models for the app (UI-framework independent, tested on any OS) |
| `Keyservant.App` | WinUI 3 app |
| `Keyservant.Cli` | `keyservant` command-line tool, also the elevated helper used by the app |
| `Keyservant.Stub` | NativeAOT standalone installer |
| `Keyservant.Tests` | Unit, round-trip, compiler-oracle and Windows install tests |

More in [docs/architecture.md](docs/architecture.md).

## Status

| Area | State |
| --- | --- |
| Layout model, `.keyservant`, `.klc` import/export, validator | Implemented, unit-tested |
| DLL writer and reader (x64, WOW64, ARM64) | Implemented; tables verified against clang-cl/lld-link builds; x64 and WOW64 DLLs load and relocate correctly; ARM64 is installed and typed with on a Windows on ARM runner in CI |
| Install / uninstall on Windows | Verified in CI: a generated layout is installed, loaded with `LoadKeyboardLayout`, every key, shift state, Caps Lock and dead-key sequence is checked with `ToUnicodeEx`, then it is uninstalled. Compose sequences and test installs (scheduled removal, keep, cleanup) are checked the same way |
| CLI | Implemented; cross-platform commands tested, Windows commands used by the install test |
| Windows app | First version; builds in CI, not yet tried by people |
| Standalone installer | Builds in CI (NativeAOT); not yet run on a PC |
| MSI and Intune package | MSI is built with WiX, installed and removed again in CI; the Intune detection script is run there. The MSI is for x64 PCs; use `setup.exe` or the Intune folder for ARM64 |
| macOS `.keylayout`, Linux XKB | Import and export implemented and round-trip tested; not yet tried on a Mac or Linux desktop. Chained dead keys don't survive an XKB import (XKB keeps them in `.XCompose`) |
| Repair after feature updates | Implemented; untested |

Not yet checked by hand: typing in real apps (Notepad, a 32-bit app, x64 apps emulated on ARM64), the
app's UI and the installer.
If you try them, please report what happened in an issue.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Code signing

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org) (application pending). See
[CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

## License

[MIT](LICENSE)
