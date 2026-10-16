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
Quetzal save/restore remain part of the later save-file tasks.

`Z3ScreenModel` provides the Version 3 status row and separate upper and lower
windows. It wraps fixed-width text, scrolls the lower window upward, clears and
overlays the upper window, tracks the lower-left input prompt, and formats score
or time status. Its style metadata follows the supplied reference: blue play
area, gray status strip, white play text, blue status text, and Courier New.
The default logical screen is 80 columns by 25 rows; hosts may inject a model
with dimensions that fit their display.

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
