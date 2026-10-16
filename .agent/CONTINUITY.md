# Continuity

## [PLANS]
- 2026-10-16T03:49Z [USER] Implement one `TASKS.md` item per feature branch and PR; pause for manual merge. Initial target is Z3 plus Quetzal, with macOS and Windows support. Blorb and versions 4–8 are later phases.

## [DECISIONS]
- 2026-10-16T03:49Z [USER] Story files, including `stories/zork1.z3`, remain local and are not tracked by default. Only explicitly authorized open-source stories may receive `.gitignore` exceptions.
- 2026-10-16T03:49Z [CODE] Use .NET 10, Avalonia, a shared interpreter core, CLI, and xUnit.

## [PROGRESS]
- 2026-10-16T04:01Z [TOOL] Task 1 commit `e9091cb` is pushed on `feature/1-project-foundation`; PR #1 is open for manual review and merge.
- 2026-10-16T04:10Z [TOOL] Windows CI's format check found CRLF checkout line endings in C# files; added `.gitattributes` with `*.cs text eol=lf` and reverified locally.

## [DISCOVERIES]
- 2026-10-16T03:49Z [TOOL] Local .NET SDK is 10.0.400; GitHub CLI is present but its saved GitHub credential is invalid.

## [OUTCOMES]
- 2026-10-16T04:03Z [TOOL] Foundation build succeeded with 0 warnings/errors; xUnit passed 1 test; `dotnet format --verify-no-changes` passed. Linux is not in CI.
- 2026-10-16T04:10Z [TOOL] After the Windows CI fix, formatting passed, Release build had 0 warnings/errors, and xUnit passed 1 test.
