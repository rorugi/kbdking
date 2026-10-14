# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

*Status: application to the SignPath Foundation open-source program pending. Until it is approved,
releases are unsigned.*

## What is signed

- `Keyservant.App.exe` and its assemblies (the Windows app)
- `keyservant.exe` (command-line tool)
- `Keyservant.Stub.exe` (standalone installer template)

Layout DLLs and installers that **users build on their own PCs** are not signed by this project, because
they are not built by the project's CI.

## How

- Only binaries built by GitHub Actions from this repository's tagged commits are submitted for signing
  (`.github/workflows/release.yml`).
- Every signing request is approved manually by a maintainer.
- Build settings are fully defined by files under source control; no binaries are added by hand.

## Team

| Role | Members |
| --- | --- |
| Committers and reviewers | [Repository maintainers](https://github.com/rorugi/keyservant/graphs/contributors) |
| Approvers | Repository owner |

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy

Keyservant does not transfer any information to other networked systems unless specifically requested by
the user or the person installing or operating it.
