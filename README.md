# GrueGPT

GrueGPT is a C# Z-machine interpreter project. Its first playable milestone targets version 3 story files and Quetzal save/restore, with a desktop GUI and command-line runner sharing the same interpreter core.

## Project layout

- `src/GrueGPT.Core` contains the interpreter engine.
- `src/GrueGPT.Desktop` contains the Avalonia desktop app for macOS and Windows.
- `src/GrueGPT.Cli` contains the command-line runner.
- `tests/GrueGPT.Tests` contains xUnit tests.
- `specs` and `reference` contain the project specifications and initial GUI reference.
- `stories` is for local playtesting. Story files are ignored by Git unless an individual open-source file is explicitly authorized for tracking.

## Supported platforms

The initial desktop target is macOS and Windows. Linux is not a supported target at this time and is not included in CI.

## Build and test

Install the .NET 10 SDK, then run:

```sh
dotnet restore GrueGPT.sln
dotnet build GrueGPT.sln --no-restore
dotnet test GrueGPT.sln --no-build
```

See [TASKS.md](TASKS.md) for the staged implementation roadmap. The initial CI matrix runs on macOS and Windows.

## License and reference materials

GrueGPT's original code and project documentation are licensed under the MIT
License; see [LICENSE](LICENSE). The project standards and GUI screenshot are
third-party reference materials and are not covered by that license. Their
attribution and current redistribution status are recorded in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Confirm permissions before
including those materials in a release.
