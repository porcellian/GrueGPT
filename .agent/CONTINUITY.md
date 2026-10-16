# Continuity

## [PLANS]
- 2026-10-16T03:49Z [USER] Implement one `TASKS.md` item per feature branch and PR; pause for manual merge. Initial target is Z3 plus Quetzal, with macOS and Windows support. Blorb and versions 4–8 are later phases.

## [DECISIONS]
- 2026-10-16T03:49Z [USER] Story files, including `stories/zork1.z3`, remain local and are not tracked by default. Only explicitly authorized open-source stories may receive `.gitignore` exceptions.
- 2026-10-16T03:49Z [CODE] Use .NET 10, Avalonia, a shared interpreter core, CLI, and xUnit.

## [PROGRESS]
- 2026-10-16T04:03Z [CODE] Task 1 foundation is implemented on `feature/1-project-foundation`; repository began without source projects or a continuity file. Local commit and PR remain pending review/credential availability.

## [DISCOVERIES]
- 2026-10-16T03:49Z [TOOL] Local .NET SDK is 10.0.400; GitHub CLI is present but its saved GitHub credential is invalid.

## [OUTCOMES]
- 2026-10-16T04:03Z [TOOL] Foundation build succeeded with 0 warnings/errors; xUnit passed 1 test; `dotnet format --verify-no-changes` passed. Linux is not in CI.
