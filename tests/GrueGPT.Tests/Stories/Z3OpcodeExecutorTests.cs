using GrueGPT.Core.Runtime;
using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3OpcodeExecutorTests
{
    private const int CodeAddress = 0x2000;
    private const int GlobalVariablesAddress = 0x0200;

    [Fact]
    public async Task ExecutesArithmeticAndStoresTheResultInAGlobal()
    {
        Z3ExecutionState state = CreateState(
            [0x14, 7, 5, 16, 0xBA]); // add 7 5 -> global 16; quit
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.RunAsync();

        Assert.Equal((ushort)12, state.Memory.ReadWord(GlobalVariablesAddress));
        Assert.True(executor.IsHalted);
    }

    [Fact]
    public async Task ExecutesSignedComparisonAndBranchesToReturn()
    {
        // jl -1 1 branches by three bytes to a following quit instruction.
        Z3ExecutionState state = CreateState([0xC2, 0x1F, 0xFF, 0xFF, 1, 0xC3, 0xBA, 0xBA]);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.ExecuteNextAsync();

        Assert.Equal(CodeAddress + 7, state.ProgramCounter);
    }

    [Fact]
    public async Task JeComparesEveryEncodedOperandInVariableForm()
    {
        // je 1 2 3 4 branches when it matches the fourth operand.
        Z3ExecutionState state = CreateState([0xC1, 0x55, 1, 2, 3, 1, 0xC3, 0xBA, 0xBA]);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.ExecuteNextAsync();

        Assert.Equal(CodeAddress + 8, state.ProgramCounter);
    }

    [Fact]
    public async Task PrintOutputsInlineZText()
    {
        TestInterpreterIO io = new();
        Z3ExecutionState state = CreateState([0xB2, 0x98, 0xA5, 0xBA]); // print "a"; quit
        Z3OpcodeExecutor executor = new(state, io);

        await executor.RunAsync();

        Assert.Equal("a", string.Concat(io.WrittenText));
    }

    [Fact]
    public async Task ExecutesPropertyLookupAddressIterationAndUpdateOpcodes()
    {
        byte[] code =
        [
            0x11, 1, 5, 16, // get_prop 1 5 -> global 16
            0x12, 1, 5, 17, // get_prop_addr 1 5 -> global 17
            0x13, 1, 0, 18, // get_next_prop 1 0 -> global 18
            0xE3, 0x57, 1, 5, 0xAB, // put_prop 1 5 171
            0x11, 1, 5, 19, // get_prop 1 5 -> global 19
            0xBA
        ];
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteBytes(CodeAddress, code)
            .WriteWord(0x0165, 0x0400)
            .WriteByte(0x0400, 0)
            .WriteByte(0x0401, 0x25)
            .WriteWord(0x0402, 0x1234)
            .WriteByte(0x0404, 0);
        Z3ExecutionState state = CreateState(builder);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.RunAsync();

        Assert.Equal((ushort)0x1234, state.Memory.ReadWord(GlobalVariablesAddress));
        Assert.Equal((ushort)0x0402, state.Memory.ReadWord(GlobalVariablesAddress + 2));
        Assert.Equal((ushort)5, state.Memory.ReadWord(GlobalVariablesAddress + 4));
        Assert.Equal((ushort)0x00AB, state.Memory.ReadWord(GlobalVariablesAddress + 6));
        Assert.Equal((ushort)0x00AB, state.Memory.ReadWord(0x0402));
    }

    [Fact]
    public async Task ExecutesMemoryTableLoadAndStoreOpcodes()
    {
        byte[] code =
        [
            0xE1, 0x97, 16, 0, 0xCA, // storew global 16 (base) 0 202
            0xE2, 0x97, 16, 1, 0xAB, // storeb global 16 (base) 1 171
            0x4F, 16, 0, 17, // loadw global 16 0 -> global 17
            0x50, 16, 1, 18, // loadb global 16 1 -> global 18
            0xBA
        ];
        Z3ExecutionState state = CreateState(code);
        state.Memory.WriteWord(GlobalVariablesAddress, 0x0500);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.RunAsync();

        Assert.Equal((ushort)0x00AB, state.Memory.ReadWord(GlobalVariablesAddress + 2));
        Assert.Equal((ushort)0x00AB, state.Memory.ReadWord(GlobalVariablesAddress + 4));
        Assert.Equal((ushort)0x00AB, state.Memory.ReadWord(0x0500));
    }

    [Fact]
    public async Task ExecutesObjectAttributeAndTreeOperations()
    {
        byte[] code =
        [
            0x0B, 1, 0, // set_attr 1 0
            0x0A, 1, 0, 0xC3, // test_attr 1 0 ?remove
            0xBA, // quit if the attribute test was false
            0x99, 2, // remove_obj 2
            0x93, 2, 16, // get_parent 2 -> global 16
            0x0E, 2, 3, // insert_obj 2 3
            0x93, 2, 17, // get_parent 2 -> global 17
            0x0C, 1, 0, // clear_attr 1 0
            0xBA
        ];
        int firstObjectAddress = 0x0120 + (31 * sizeof(ushort));
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteBytes(CodeAddress, code)
            .WriteByte(firstObjectAddress + 6, 2)
            .WriteByte(firstObjectAddress + 9 + 4, 1);
        Z3ExecutionState state = CreateState(builder);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.RunAsync();

        Z3ObjectTable objects = new(state.Memory, state.Header.ObjectTableAddress);
        Assert.False(objects.TestAttribute(1, 0));
        Assert.Equal((ushort)0, state.Memory.ReadWord(GlobalVariablesAddress));
        Assert.Equal((ushort)3, state.Memory.ReadWord(GlobalVariablesAddress + 2));
        Assert.Equal((byte)2, objects.GetChild(3));
        Assert.Equal((byte)0, objects.GetChild(1));
    }

    [Fact]
    public async Task IncAndLoadUseVariableZeroIndirectlyWithoutConsumingTheStack()
    {
        // push 40; inc (sp); load (sp) -> global 16; quit
        Z3ExecutionState state = CreateState(
            [0xE8, 0x7F, 40, 0xA5, 0, 0xAE, 0, 16, 0xBA]);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.RunAsync();

        Assert.Equal((ushort)41, state.Memory.ReadWord(GlobalVariablesAddress));
        Assert.Equal(1, state.EvaluationStackDepth);
        Assert.Equal((ushort)41, state.PeekStack());
    }

    [Fact]
    public async Task StoreOpcodeUsesItsFirstOperandAsAnIndirectVariableReference()
    {
        // push 10; store (sp) 25; quit
        Z3ExecutionState state = CreateState([0xE8, 0x7F, 10, 0x4D, 0, 25, 0xBA]);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.RunAsync();

        Assert.Equal(2, state.EvaluationStackDepth);
        Assert.Equal((ushort)25, state.PeekStack());
    }

    [Fact]
    public async Task CallsAndReturnsFromZeroLocalRoutine()
    {
        // call routine at 0x2200 (packed address 0x1100) -> global 16; quit
        // Routine body: zero locals; rtrue.
        Z3ExecutionState state = CreateState(
            [0xE0, 0x3F, 0x11, 0x00, 16, 0xBA],
            [0x00, 0xB0],
            codeAtRoutine: 0x2200);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.RunAsync();

        Assert.Equal((ushort)1, state.Memory.ReadWord(GlobalVariablesAddress));
        Assert.Equal(0, state.CallDepth);
        Assert.True(executor.IsHalted);
    }

    [Fact]
    public async Task TextAndNumberOutputAreSentToHostInInstructionOrder()
    {
        // print_char 65; print_char 0; print_num -7; new_line; quit
        TestInterpreterIO io = new();
        Z3ExecutionState state = CreateState(
            [0xE5, 0x7F, 65, 0xE5, 0x7F, 0, 0xE6, 0x3F, 0xFF, 0xF9, 0xBB, 0xBA]);
        Z3OpcodeExecutor executor = new(state, io);

        await executor.RunAsync();

        Assert.Equal("A-7\n", string.Concat(io.WrittenText));
    }

    [Fact]
    public async Task StoreResultStackSemanticsAndRandomRangeAreApplied()
    {
        // random 1 -> (sp); quit
        Z3ExecutionState state = CreateState([0xE7, 0x7F, 1, 0, 0xBA]);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO(), new Z3RandomNumberGenerator());

        await executor.RunAsync();

        Assert.Equal((ushort)1, state.PeekStack());
    }

    [Fact]
    public void NegativeRandomOperandReseedsPredictableSequence()
    {
        Z3RandomNumberGenerator random = new();

        Assert.Equal((ushort)0, random.Next(unchecked((ushort)-7)));
        ushort[] firstSequence = [random.Next(100), random.Next(100), random.Next(100)];
        Assert.Equal((ushort)0, random.Next(unchecked((ushort)-7)));
        ushort[] secondSequence = [random.Next(100), random.Next(100), random.Next(100)];

        Assert.Equal(firstSequence, secondSequence);
    }

    [Fact]
    public async Task UpdatesStatusFromTheHeaderRoomAndGlobals()
    {
        // Object 1 has the short name "a" at its property table.
        TestInterpreterIO io = new();
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteBytes(CodeAddress, [0xBC, 0xBA])
            .WriteWord(GlobalVariablesAddress, 1)
            .WriteWord(GlobalVariablesAddress + 2, 12)
            .WriteWord(GlobalVariablesAddress + 4, 34)
            .WriteWord(0x0120 + (31 * 2) + 7, 0x0400)
            .WriteByte(0x0400, 1)
            .WriteWord(0x0401, 0x98A5);
        Z3ExecutionState state = CreateState(builder);
        Z3OpcodeExecutor executor = new(state, io);

        await executor.ExecuteNextAsync();

        Assert.Equal(new StoryStatus("a", 12, 34), Assert.Single(io.Statuses));
    }

    [Fact]
    public async Task ShowsAnEmptyLocationWhenTheRoomGlobalIsZero()
    {
        TestInterpreterIO io = new();
        Z3ExecutionState state = CreateState([0xBC, 0xBA]);
        Z3OpcodeExecutor executor = new(state, io);

        await executor.ExecuteNextAsync();

        Assert.Equal(string.Empty, Assert.Single(io.Statuses).Location);
    }

    [Fact]
    public async Task VerifyChecksTheOriginalStoryBytesAfterDynamicMemoryChanges()
    {
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteBytes(CodeAddress, [0xBD, 0xC3, 0xBA, 0xBA])
            .WriteByte(0x0400, 1);
        Z3ExecutionState state = CreateState(builder);
        state.Memory.WriteByte(0x0400, 2);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.ExecuteNextAsync();

        Assert.Equal(CodeAddress + 3, state.ProgramCounter);
    }

    [Fact]
    public async Task RejectsOpcodesAssignedToLaterTasksWithClearOwnership()
    {
        Z3ExecutionState state = CreateState([0xB5]); // save
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
            async () => await executor.ExecuteNextAsync());

        Assert.Contains("task 17", exception.Message);
    }

    [Fact]
    public async Task ReadsLowercaseInputIntoTextBufferAndTokenizesIt()
    {
        TestInterpreterIO io = new();
        io.KeyboardLines.Enqueue("LOOK NORTH");
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteBytes(CodeAddress, [0xE4, 0x0F, 0x04, 0x00, 0x05, 0x00, 0xBA])
            .WriteByte(0x0100, 0)
            .WriteByte(0x0101, 7)
            .WriteWord(0x0102, 0)
            .WriteByte(0x0400, 10)
            .WriteByte(0x0500, 8);
        Z3ExecutionState state = CreateState(builder);
        Z3OpcodeExecutor executor = new(state, io);

        await executor.RunAsync();

        Assert.Equal("look north\0", ReadZsciiText(state.Memory, 0x0400));
        Assert.Equal((byte)2, state.Memory.ReadByte(0x0501));
        Assert.Equal((byte)4, state.Memory.ReadByte(0x0504));
        Assert.Equal((byte)1, state.Memory.ReadByte(0x0505));
        Assert.Equal((byte)5, state.Memory.ReadByte(0x0508));
        Assert.Equal((byte)6, state.Memory.ReadByte(0x0509));
        Assert.Equal("LOOK NORTH\n", string.Concat(io.WrittenText));
        Assert.Single(io.Statuses);
    }

    [Fact]
    public async Task PlaybackInputIsTruncatedEchoedAndRecordedAsOneCompletedCommand()
    {
        byte[] code =
        [
            0xF4, 0x7F, 1, // input_stream 1
            0xF3, 0x7F, 4, // output_stream 4
            0xE4, 0x0F, 0x04, 0x00, 0x05, 0x00, // read text $0400 parse $0500
            0xBA
        ];
        TestInterpreterIO io = new();
        io.PlaybackCommands.Enqueue("NORTH");
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteBytes(CodeAddress, code)
            .WriteByte(0x0100, 0)
            .WriteByte(0x0101, 7)
            .WriteWord(0x0102, 0)
            .WriteByte(0x0400, 4)
            .WriteByte(0x0500, 1);
        Z3ExecutionState state = CreateState(builder);

        await new Z3OpcodeExecutor(state, io).RunAsync();

        Assert.Equal("nort\0", ReadZsciiText(state.Memory, 0x0400));
        Assert.Equal("NORT\n", string.Concat(io.WrittenText));
        Assert.Equal("NORT", Assert.Single(io.RecordedCommands));
    }

    [Fact]
    public async Task OutputStreamThreeWritesZsciiAndFinalizesItsCount()
    {
        byte[] code =
        [
            0xF3, 0x4F, 3, 0x04, 0x00, // output_stream 3 table $0400
            0xE5, 0x7F, 65, // print_char 'A'
            0xBB, // new_line
            0xF3, 0x3F, 0xFF, 0xFD, // output_stream -3
            0xBA
        ];
        TestInterpreterIO io = new();
        Z3ExecutionState state = CreateState(code);
        Z3OpcodeExecutor executor = new(state, io);

        await executor.RunAsync();

        Assert.Equal((ushort)2, state.Memory.ReadWord(0x0400));
        Assert.Equal((byte)'A', state.Memory.ReadByte(0x0402));
        Assert.Equal((byte)13, state.Memory.ReadByte(0x0403));
        Assert.Empty(io.WrittenText);
    }

    [Fact]
    public async Task MemoryOutputStreamSuppressesSelectedScreenAndTranscriptStreams()
    {
        byte[] code =
        [
            0xF3, 0x7F, 2, // output_stream 2
            0xF3, 0x4F, 3, 0x04, 0x00, // output_stream 3 table $0400
            0xE5, 0x7F, 65, // print_char 'A'
            0xF3, 0x3F, 0xFF, 0xFD, // output_stream -3
            0xF3, 0x3F, 0xFF, 0xFE, // output_stream -2
            0xBA
        ];
        TestInterpreterIO io = new();
        Z3ExecutionState state = CreateState(code);

        await new Z3OpcodeExecutor(state, io).RunAsync();

        Assert.Equal((ushort)1, state.Memory.ReadWord(0x0400));
        Assert.Equal((byte)'A', state.Memory.ReadByte(0x0402));
        Assert.Empty(io.WrittenText);
        Assert.Empty(io.TranscriptText);
    }

    [Fact]
    public async Task TranscriptSelectionTracksFlagsAndCapturesStoryOutput()
    {
        byte[] code =
        [
            0xF3, 0x7F, 2, // output_stream 2
            0xE5, 0x7F, 65, // print_char 'A'
            0xF3, 0x3F, 0xFF, 0xFE, // output_stream -2
            0xBA
        ];
        TestInterpreterIO io = new();
        Z3ExecutionState state = CreateState(code);
        Z3OpcodeExecutor executor = new(state, io);

        await executor.RunAsync();

        Assert.Equal("A", string.Concat(io.TranscriptText));
        Assert.Equal((byte)0, (byte)(state.Memory.ReadByte(Z3Memory.Flags2Address + 1) & 0x01));
        Assert.Equal("A", string.Concat(io.WrittenText));
    }

    [Fact]
    public async Task RestartRestoresMemoryStackAndPcWhilePreservingFlags2BitsZeroAndOne()
    {
        Z3ExecutionState state = CreateState([0xB7]);
        state.Memory.WriteWord(GlobalVariablesAddress, 0xCAFE);
        state.Memory.WriteByte(Z3Memory.Flags2Address + 1, 0x03);
        state.PushStack(123);
        Z3OpcodeExecutor executor = new(state, new TestInterpreterIO());

        await executor.ExecuteNextAsync();

        Assert.Equal(CodeAddress, state.ProgramCounter);
        Assert.Equal((ushort)0, state.Memory.ReadWord(GlobalVariablesAddress));
        Assert.Equal((byte)0x03, (byte)(state.Memory.ReadByte(Z3Memory.Flags2Address + 1) & 0x03));
        Assert.Equal(0, state.EvaluationStackDepth);
    }

    [Fact]
    public async Task RunReportsFatalStoryErrorsBeforeRethrowingThem()
    {
        TestInterpreterIO io = new();
        Z3OpcodeExecutor executor = new(CreateState([0xBF]), io);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await executor.RunAsync());

        Assert.Contains("0x2001", Assert.Single(io.Errors));
        Assert.Contains(exception.Message, io.Errors[0]);
    }

    private static Z3ExecutionState CreateState(byte[] code, byte[]? routine = null, int codeAtRoutine = 0)
    {
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteBytes(CodeAddress, code);
        if (routine is not null)
        {
            builder.WriteBytes(codeAtRoutine, routine);
        }

        return CreateState(builder);
    }

    private static Z3ExecutionState CreateState(SyntheticStoryBuilder builder)
    {
        using MemoryStream stream = builder.BuildStream();
        return new Z3ExecutionState(new Z3StoryLoader().Load(stream));
    }

    private static string ReadZsciiText(Z3Memory memory, int address)
    {
        int maximumCharacters = memory.ReadByte(address);
        char[] characters = new char[maximumCharacters + 1];
        for (int index = 0; index <= maximumCharacters; index++)
        {
            byte code = memory.ReadByte(address + 1 + index);
            if (code == 0)
            {
                return new string(characters, 0, index) + '\0';
            }

            characters[index] = Z3TextDecoder.ToUnicodeCharacter(code) ?? '\0';
        }

        return new string(characters);
    }

    private sealed class TestInterpreterIO : IInterpreterIO, IZ3StreamIO
    {
        public List<string> WrittenText { get; } = [];

        public List<StoryStatus> Statuses { get; } = [];

        public List<string> TranscriptText { get; } = [];

        public List<string> RecordedCommands { get; } = [];

        public List<string> Errors { get; } = [];

        public Queue<string?> KeyboardLines { get; } = new();

        public Queue<string?> PlaybackCommands { get; } = new();

        public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
        {
            WrittenText.Add(text);
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateStatusAsync(StoryStatus status, CancellationToken cancellationToken = default)
        {
            Statuses.Add(status);
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(KeyboardLines.TryDequeue(out string? line) ? line : null);

        public ValueTask ReportErrorAsync(string message, CancellationToken cancellationToken = default)
        {
            Errors.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteTranscriptAsync(string text, CancellationToken cancellationToken = default)
        {
            TranscriptText.Add(text);
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> ReadPlaybackCommandAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlaybackCommands.TryDequeue(out string? command) ? command : null);

        public ValueTask WriteRecordedCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            RecordedCommands.Add(command);
            return ValueTask.CompletedTask;
        }
    }
}
