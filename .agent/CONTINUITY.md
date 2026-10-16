# Continuity

## [PLANS]
- 2026-10-16T03:49Z [USER] Implement one `TASKS.md` item per feature branch and PR; pause for manual merge. Initial target is Z3 plus Quetzal, with macOS and Windows support. Blorb and versions 4–8 are later phases.

## [DECISIONS]
- 2026-10-16T03:49Z [USER] Story files, including `stories/zork1.z3`, remain local and are not tracked by default. Only explicitly authorized open-source stories may receive `.gitignore` exceptions.
- 2026-10-16T03:49Z [CODE] Use .NET 10, Avalonia, a shared interpreter core, CLI, and xUnit.

## [PROGRESS]
- 2026-10-16T04:01Z [TOOL] Task 1 commit `e9091cb` was pushed on `feature/1-project-foundation`; user confirmed PR #1 merged and fetched `origin/main` contains merge commit `cde737f`.
- 2026-10-16T04:10Z [TOOL] Windows CI's format check found CRLF checkout line endings in C# files; added `.gitattributes` with `*.cs text eol=lf` and reverified locally.
- 2026-10-16T04:32Z [TOOL] Task 2 commit `45ec8f2` was pushed on `feature/2-project-license`; PR #2 was opened at `https://github.com/porcellian/GrueGPT/pull/2` and user later confirmed it merged.
- 2026-10-16T04:32Z [USER] User selected MIT for GrueGPT code; third-party standards and GUI reference remain outside that license.
- 2026-10-16T04:32Z [CODE] Task 2 documentation attributes the bundled standards and GUI reference and marks redistribution permissions unconfirmed; release guidance excludes them until rights are verified.
- 2026-10-16T05:05Z [TOOL] User confirmed PR #2 merged; fetched `origin/main` is at `ce2f0d0` and task 3 is underway on `feature/3-synthetic-story-test-utils`.
- 2026-10-16T05:05Z [TOOL] Task 3 commit `36dca84` is pushed on `feature/3-synthetic-story-test-utils`; PR #3 is open for manual merge at `https://github.com/porcellian/GrueGPT/pull/3`.

## [DISCOVERIES]
- 2026-10-16T03:49Z [TOOL] Local .NET SDK is 10.0.400.
- 2026-10-16T05:05Z [TOOL] xUnit and Roslyn format checks require elevated execution in this sandbox because their local socket/pipe setup is blocked; both passed when rerun elevated.

## [OUTCOMES]
- 2026-10-16T04:03Z [TOOL] Foundation build succeeded with 0 warnings/errors; xUnit passed 1 test; `dotnet format --verify-no-changes` passed. Linux is not in CI.
- 2026-10-16T04:10Z [TOOL] After the Windows CI fix, formatting passed, Release build had 0 warnings/errors, and xUnit passed 1 test.
- 2026-10-16T04:32Z [CODE] Task 2 adds an MIT license for GrueGPT, identifies third-party attribution and redistribution status in `THIRD_PARTY_NOTICES.md`, and links the notice from README.
- 2026-10-16T05:05Z [TOOL] Task 3 Release build passed with 0 warnings/errors; xUnit passed 8 tests; `dotnet format --verify-no-changes` passed. Tests use the new deterministic synthetic Z3 image builder with configurable headers/checksum and generated memory fixtures.
