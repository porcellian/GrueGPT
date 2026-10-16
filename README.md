# GrueGPT

GrueGPT is a C# Z-machine interpreter project. Its first playable milestone targets version 3 story files and Quetzal save/restore, with a desktop GUI and command-line runner sharing the same interpreter core.

## Project layout

- `src/GrueGPT.Core` contains the interpreter engine.
- `src/GrueGPT.Desktop` contains the Avalonia desktop app for macOS and Windows.
- `src/GrueGPT.Cli` contains the command-line runner.
- `tests/GrueGPT.Tests` contains xUnit tests.
- `tests/GrueGPT.Tests/TestUtilities` contains generated story builders and shared test helpers; CI does not require local or copyrighted story files.
- `specs` and `reference` contain the project specifications and initial GUI reference.
- `stories` is for local playtesting. Story files are ignored by Git unless an individual open-source file is explicitly authorized for tracking.

## Interpreter boundary

The Core project defines `IStorySession` and `IInterpreterIO` so a story session
can use the same asynchronous line-input, story-text, and status-update contract
with either the CLI or desktop host. The Core has no console or Avalonia UI
dependency; each front end owns input editing and rendering. Hosts that
implement `IZ3ScreenIO` receive character-cell screen snapshots from the Core.

The Version 3 `read` opcode requests a complete line from the host, updates the
story's text and parse buffers, and echoes the completed command through the
currently selected output streams. Hosts own interactive editing and key
handling. Optional `IZ3StreamIO` lets hosts provide transcript and command-file
streams; selecting those streams without that capability produces a reported
runtime error. Memory output stream 3 is handled directly by Core, including
nested tables and stream suppression. Restart restores story memory, stack,
program counter, random mode, and output selection while retaining the Version
3 transcript and fixed-pitch flags.

`Z3StoryLoader` reads a raw story from a readable stream, validates its v3
header, memory ranges, declared length, and available checksum, then returns a
`LoadedStory` containing the logical story bytes and parsed `StoryHeader`.
Physical padding past the declared length is excluded from the loaded image.
Legacy v3 files with omitted length and checksum fields use the physical stream
length and load without checksum verification.

`Z3Memory` starts from a private copy of the loaded image. It reads bytes and
big-endian words throughout addressable story memory, permits story writes only
below the static-memory base, and applies the Version 3 game/interpreter rules
for the header flags. `PackAddress` and `UnpackAddress` convert Version 3 packed
addresses using the two-byte scale.

`Z3TextDecoder` decodes packed Z-characters using the Version 3 default
alphabets, expands the story's abbreviation table, handles A2 newline and
ZSCII escape sequences, and maps defined output characters to Unicode. Custom
alphabet tables and later-version Unicode translation tables are outside the
Version 3 scope.

`Z3ObjectTable` reads the Version 3 property defaults, 9-byte object entries,
attributes, and descending property lists. It supports property lookup and
updates, property length queries, and object removal/insertion in the object
tree. Object attributes and property writes use the dynamic-memory rules from
`Z3Memory`.

`Z3Dictionary` parses Version 3 dictionary headers and validates sorted entry
order, encodes input words to six Z-characters, and searches sorted or unsorted
dictionaries. `Z3Tokenizer` reads the Version 3 text-buffer layout, separates
words and dictionary word separators, and writes bounded parse-buffer entries
with dictionary addresses, token lengths, and text positions. It also supports
the tokenise flag that preserves existing entries for unrecognized words.

`Z3InstructionDecoder` reads v3 long, short, and variable instruction forms,
records variable references, and captures store, branch, and inline-text data
while advancing the program counter. Variable values are evaluated by the
executor when the instruction semantics require them, so indirect stack
operations do not accidentally pop their target variable. `Z3ExecutionState`
manages stack, local, and global variables; routine frames and returns; signed
jumps; and conditional branches.

`Z3OpcodeExecutor` implements the Version 3 arithmetic and comparison
instructions, branches and jumps, routine calls and returns, object/property
operations, packed text and character/number output, random numbers, memory
reads and writes, stack operations, status updates, verification, line input,
restart, and output stream management. It tracks the two Version 3 output
windows and accepts sound-effect instructions without requiring a sound device.
`QuetzalFileCodec` reads and writes the IFF `FORM/IFZS` container used by
Quetzal. It validates big-endian form and chunk lengths, printable identifiers,
chunk bounds, and zero padding for odd-length chunks. Chunk order, duplicates,
and unknown chunk data are preserved.

`QuetzalIfhd` reads and writes the standard 13-byte story identity and
three-byte resume PC. `QuetzalMemoryCodec` writes CMem XOR/run-length data,
restores shortened CMem tails as unchanged story bytes, and reads/writes UMem
only when its size exactly matches dynamic memory. `QuetzalStackCodec`
serializes and restores Version 3 top-level evaluation
values, the required dummy frame, and oldest-first call frames with locals,
argument masks, result behavior, and per-frame evaluation stacks. The execution
state can snapshot and restore these stacks together with a supplied resume PC.
`Z3SaveRestoreService` combines IFhd, CMem, and Stks, verifies release/serial/
checksum identity, preserves interpreter-owned header flags, and validates a
restore before changing live state. The Z3 SAVE/RESTORE opcodes apply Version 3
branch behavior; host save/restore actions are exposed through
`IZ3SaveRestoreIO` and `Z3OpcodeExecutor` for desktop menus. Host snapshots use
a private Quetzal extension to distinguish direct-PC resume from a story SAVE
continuation.

`Z3ScreenModel` provides the Version 3 status row and separate upper and lower
windows. It wraps fixed-width text, scrolls the lower window upward, clears and
overlays the upper window, tracks the lower-left input prompt, and formats score
or time status. Its style metadata follows the supplied reference: blue play
area, gray status strip, white play text, blue status text, and Courier New.
The default logical screen is 80 columns by 25 rows; hosts may inject a model
with dimensions that fit their display. The Avalonia desktop window renders full
screen snapshots with a responsive Courier-style size, status strip, and
bottom-left prompt. Open Story uses the native file picker and validates raw v3
story files before starting the interpreter. Enter submits a command at the
prompt. Save Game and Load Game use native Quetzal file dialogs; host saves made
at a command prompt resume by re-running the pending `read` instruction. Failed
story loads, runtime errors, and invalid saves appear in a dismissible error
banner. Loading a save validates it in a new state before replacing the active
story session.

## Supported platforms

The desktop target is macOS and Windows. Linux is not supported and is not included in CI. The interpreter currently accepts raw Version 3 story files only. Versions 4–8 and Blorb files are planned for later phases.

## Build and test

Install the .NET 10 SDK, then run:

```sh
dotnet restore GrueGPT.sln
dotnet build GrueGPT.sln --no-restore
dotnet test GrueGPT.sln --no-build
```

See [TASKS.md](TASKS.md) for the staged implementation roadmap. The initial CI matrix runs on macOS and Windows.

The xUnit suite builds synthetic Version 3 stories and checks interpreter
boundaries, including dynamic/static memory writes, all legal property lengths,
malformed and empty dictionaries, conditional branch fallthrough, invalid
arithmetic, and nested memory output streams. CI does not depend on local story
files.

Quetzal tests cover complete memory and call-stack round trips, compressed and
uncompressed dynamic memory, malformed or incomplete saves, story identity
mismatches, SAVE branch resume, and independently framed IFZS files with
unknown padded chunks. All save fixtures are generated in the test suite.

## Supported behavior and current limitations

The first milestone implements the Version 3 instruction set and runtime
features described above, including status updates, two output windows, line
input, and Quetzal saves. The desktop and CLI accept raw story files; they do
not open Blorb containers. Saves are associated with the story release,
serial, and checksum, so restore them against the matching story file.

Both front ends currently read a completed line at a time. Timed or
character-by-character input is not implemented. The core supports transcript
and command-file stream interfaces, but neither front end currently provides
those optional host streams. Memory output stream 3 is supported. Sound-effect
instructions are accepted without audio playback. Text decoding uses the
Version 3 default alphabets and defined ZSCII mappings; custom alphabet and
later-version Unicode tables are outside this milestone.

Story files, including `stories/zork1.z3`, remain local and ignored by Git.
Builds, tests, and published output do not need or bundle a story. Obtain a
story separately and ensure you have the rights needed to use and distribute
it.

## Command-line runner

Run a local Version 3 story with:

```sh
dotnet run --project src/GrueGPT.Cli -- stories/zork1.z3
```

The CLI reads player commands from standard input and writes story text and
the `> ` prompt to standard output. Status updates and errors go to standard
error. When a story executes SAVE or RESTORE, the CLI prompts for a Quetzal
file path; an empty path cancels the operation. Resume a saved game directly
with:

```sh
dotnet run --project src/GrueGPT.Cli -- stories/zork1.z3 --restore saves/zork1.sav
```

The runner uses the same Version 3 story loader, opcode executor, and Quetzal
restore service as the desktop app. It does not require stories to be tracked
in the repository.

Optional local Zork regression transcripts are available with
`bash scripts/zork-regression.sh` on macOS or
`powershell -NoProfile -File scripts/zork-regression.ps1` on Windows. Both use
`stories/zork1.z3` by default, accept an alternate story path, and exit
successfully with a `SKIP` message when the selected file is absent. These
scripts are run manually and are not part of CI; the story remains ignored by
Git.

## Publishing desktop builds

Install the .NET 10 SDK and publish separately for each target runtime. These
commands create self-contained publish directories, so users do not need to
install the .NET runtime:

```sh
dotnet publish src/GrueGPT.Desktop/GrueGPT.Desktop.csproj -c Release -r osx-arm64 --self-contained true -o artifacts/publish/osx-arm64
dotnet publish src/GrueGPT.Desktop/GrueGPT.Desktop.csproj -c Release -r osx-x64 --self-contained true -o artifacts/publish/osx-x64
dotnet publish src/GrueGPT.Desktop/GrueGPT.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

The output is a platform-specific application directory, not a signed
installer or native app bundle. Release packaging, code signing, macOS
notarization, and Windows installer creation are not configured yet. Review
the publish output before distributing it, and keep local stories, save files,
the reference screenshot, and specification documents out of releases unless
their redistribution rights are confirmed. See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## License and reference materials

GrueGPT's original code and project documentation are licensed under the MIT
License; see [LICENSE](LICENSE). The project standards and GUI screenshot are
third-party reference materials and are not covered by that license. Their
attribution and current redistribution status are recorded in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Confirm permissions before
including those materials in a release.
