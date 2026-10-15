# Contributing to kbdking

Thanks for helping! Bug reports, layouts that don't import correctly, and pull requests are all welcome.

## Reporting a problem

- For a layout that imports or builds wrongly, attach the `.kbdking`, `.klc` or DLL file if you can,
  and say which Windows version you use.
- For installation problems, include the output of `kbdking list --custom` and `kbdking repair`.

## Development

- .NET 10 SDK. The engine, CLI and tests build on any OS; the app and installer stub need Windows.
- `dotnet test tests/Kbdking.Tests` before opening a pull request.
- Install `clang` and `lld` (e.g. `apt install clang lld`, or LLVM for Windows) to run the compiler
  oracle tests locally.
- The install test changes your PC (installs and removes a layout). It only runs as administrator with
  `KBDKING_INSTALL_TESTS=1`.

## Guidelines

- Keep `Kbdking.Core`, `Kbdking.Binary` and `Kbdking.Presentation` free of Windows and UI
  dependencies, so they stay testable everywhere.
- New format features need a round-trip test; changes to the binary writer need a test that reads the
  result back (and ideally an oracle test).
- Messages shown to users should say what happened and what to do, in plain words.
- Warnings are errors in the build. Please keep it that way.

## Code signing

Release binaries are built and signed only by CI, from this repository (see
[CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md)). All maintainers with write access use multi-factor
authentication.
