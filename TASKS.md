# GrueGPT implementation tasks

Each checkbox is a separate task and pull request. The explicit `Task N` labels are stable task IDs across sections. Before starting a task, create `feature/<task-number>-<brief-description>`. Use a conventional commit, update this checklist, run the relevant checks, and open a PR. Do not merge PRs automatically.

## Project foundation

- [x] **Task 1:** Establish the .NET solution, project layout, test project, cross-platform CI, project guidance, and this roadmap. Keep story files local and ignored by default.
- [x] **Task 2:** Select and add a project license; document provenance and redistribution rules for the standards and GUI reference before publishing releases.
- [x] **Task 3:** Add a synthetic story-file builder and shared test utilities so all CI tests run without copyrighted story files.

## Z-machine version 3 interpreter

- [x] **Task 4:** Define the interpreter core's story/session and input/output boundaries so the desktop app and CLI use the same engine.
- [x] **Task 5:** Load raw Z3 story files; validate file size, version, header fields, checksum, and initial execution address with clear errors.
- [x] **Task 6:** Implement the Z3 memory map, dynamic/static memory rules, byte/word access, packed-address calculations, and header flags.
- [x] **Task 7:** Decode Z-machine text, alphabets, abbreviations, ZSCII escapes, and character output according to the Z3 rules.
- [x] **Task 8:** Implement instruction decoding, operand forms, variable access, stores, branches, jumps, routine calls, returns, and stack frames.
- [x] **Task 9:** Implement the Z3 object tree, attributes, property defaults, property lookup/update, and object movement operations.
- [x] **Task 10:** Implement the Z3 dictionary format, tokenization, input-buffer layout, and parser-buffer updates.
- [x] **Task 11:** Implement the legal Z3 opcode set, including arithmetic, comparisons, control flow, object/property, text, random, memory/table, and call operations.
- [x] **Task 12:** Implement Z3 line input, command editing, terminating characters, output streams, transcript behavior, restart, and error reporting.
- [x] **Task 13:** Implement the Z3 screen model, status line (location, score, moves), colors, fixed-width font, prompt placement, and upward-scrolling text.

## Quetzal save and restore for Z3

- [x] **Task 14:** Implement IFF/IFZS parsing and writing with big-endian values, chunk bounds, odd-length padding, and safe handling of unknown chunks.
- [x] **Task 15:** Implement Quetzal `IFhd`, `CMem`, and `UMem` support, including dynamic-memory compression/decompression and malformed-data checks.
- [x] **Task 16:** Implement Quetzal `Stks` serialization and restore for Z3 call frames, locals, evaluation stack, and the required dummy frame.
- [x] **Task 17:** Integrate save/restore with Z3 SAVE/RESTORE branch semantics and host menu actions; reject restores for a mismatched story and preserve the running state on failed restores.

## Desktop and CLI front ends

- [x] **Task 18:** Build the Avalonia GUI for macOS and Windows, matching the reference colors, Courier-style text, status strip, input prompt, and scrolling transcript.
- [x] **Task 19:** Add story selection, keyboard command entry, save/load dialogs, and actionable error messages to the desktop app.
- [x] **Task 20:** Implement CLI story loading, command input, text output, and save/restore using the shared interpreter core.

## Z3 verification and release readiness

- [x] **Task 21:** Add Z3 conformance tests for memory, text, object/property, dictionary, opcode, input/output, and control-flow edge cases.
- [x] **Task 22:** Add Quetzal round-trip, malformed-file, wrong-story, branch-resume, and interoperability tests using redistributable or generated fixtures.
- [x] **Task 23:** Add optional local Zork regression scripts that skip cleanly when `stories/zork1.z3` is absent; never make CI depend on or upload that file.
- [x] **Task 24:** Document build/run instructions, supported Z3 behavior, save-file handling, known limitations, and release packaging for macOS and Windows.

## Later phase: Blorb

- [x] **Task 25:** Parse Blorb IFF/IFRS structure, resource index, chunk offsets, padding, and Z-code executable resources.
- [ ] **Task 26:** Add Blorb resource lookup and support applicable metadata, descriptions, frontispiece, palette, resolution, and Z-machine-specific chunks.
- [ ] **Task 27:** Add applicable Blorb picture and sound resource decoding/playback, with tests for supported formats and malformed resources.

## Later phase: Z-machine versions 4–8

- [ ] **Task 28:** Add version-aware headers, memory/address rules, instruction availability, and Quetzal resume behavior for versions 4–5 and 7–8.
- [ ] **Task 29:** Add version 4–5 opcodes and screen/input features, including window, cursor, style, timed input, and extended save/restore behavior.
- [ ] **Task 30:** Add version 6 windowing, graphics, picture, mouse/menu, sound, and scaling behavior with Blorb resources.
- [ ] **Task 31:** Add version 7–8 packed-address and routine/string address rules, plus remaining version-specific opcodes and state behavior.
- [ ] **Task 32:** Add cross-version conformance and save interoperability tests using only generated or redistribution-cleared fixtures.
