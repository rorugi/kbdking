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
- **Editor**: on-screen ISO/ANSI keyboard, every shift state (Shift, AltGr, Shift+AltGr, Ctrl, …),
  dead keys including **chained dead keys**, ligatures (several characters per key, including characters
  outside the BMP), Caps Lock behaviour and SGCAPS, and a live preview that types with the layout you are
  editing before you install it.
- **Formats**: `.keyservant` (readable JSON, diff-friendly), MSKLC `.klc`, Windows layout DLLs, and C
  source for building with your own compiler. macOS `.keylayout` and Linux XKB are planned.
- **Builds without a compiler**: Keyservant writes the PE file itself, for x64 and the 32-bit WOW64 DLL
  that 32-bit apps use. No Visual Studio, WDK or MSKLC needed, and builds are reproducible.
- **Standalone installers**: turn a layout into a small `setup.exe` that installs it on any Windows PC,
  with an entry in *Apps & features* for uninstalling.

## Quick start (CLI)

```powershell
keyservant new de-AT -o my-layout.keyservant --name "German + IAST" --dll kbdiast
# edit my-layout.keyservant, or open it in the Keyservant app
keyservant validate my-layout.keyservant
keyservant install my-layout.keyservant        # asks for admin rights once
keyservant list --custom
keyservant uninstall A0000C07
```

Other commands: `templates`, `convert <in> <out>` (any supported formats), `build` (DLLs only),
`package` (standalone installer), `enable`/`disable` (switcher), `import` (save an installed layout as a
`.keyservant` file) and `repair`. Run `keyservant help` for details.

The sample [`samples/german-austria-iast.keyservant`](samples/german-austria-iast.keyservant) adds IAST
transliteration for Sanskrit to the Austrian German layout: AltGr+a/i/u → ā ī ū, AltGr+s → ś,
AltGr+g → ṅ, AltGr+n → ñ, a dot-below dead key on AltGr+. (ṭ ḍ ṇ ṣ ṛ ḷ ṃ ḥ) and a macron dead key on
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
| `Keyservant.Core` | Layout model, `.keyservant` and `.klc` formats, validator, templates, typing simulator |
| `Keyservant.Binary` | PE writer and reader for layout DLLs, C source export, layout packages |
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
| DLL writer and reader (x64, WOW64) | Implemented; tables verified against clang-cl/lld-link builds; both DLLs load and relocate correctly |
| Install / uninstall on Windows | Verified in CI: a generated layout is installed, loaded with `LoadKeyboardLayout`, every key, shift state, Caps Lock and dead-key sequence is checked with `ToUnicodeEx`, then it is uninstalled |
| CLI | Implemented; cross-platform commands tested, Windows commands used by the install test |
| Windows app | First version; builds in CI, not yet tried by people |
| Standalone installer | Builds in CI (NativeAOT); not yet run on a PC |
| Repair after feature updates | Implemented; untested |
| macOS `.keylayout`, Linux XKB | Planned (milestone M5) |
| ARM64 | Planned |

Not yet checked by hand: typing in real apps (Notepad, a 32-bit app), the app's UI and the installer.
If you try them, please report what happened in an issue.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Code signing

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org) (application pending). See
[CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

## License

[MIT](LICENSE)
