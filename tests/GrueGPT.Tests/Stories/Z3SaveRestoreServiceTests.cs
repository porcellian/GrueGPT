using GrueGPT.Core.SaveFiles;
using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3SaveRestoreServiceTests
{
    [Fact]
    public void HostSaveRoundTripsDynamicMemoryStacksAndDirectProgramCounter()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder());
        state.SetProgramCounter(0x2010);
        state.Memory.WriteWord(0x0200, 0x1234);
        state.PushStack(0x4567);
        byte[] saveFile = Z3SaveRestoreService.CreateHostSaveFile(state);

        state.SetProgramCounter(0x2020);
        state.Memory.WriteWord(0x0200, 0xABCD);
        state.WriteVariable(0, 0xEEEE);
        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(state, saveFile);

        Assert.Equal(Z3RestoreStatus.ResumedAtProgramCounter, result.Status);
        Assert.Equal(0x2010, state.ProgramCounter);
        Assert.Equal((ushort)0x1234, state.Memory.ReadWord(0x0200));
        Assert.Equal((ushort)0x4567, state.PopStack());
    }

    [Fact]
    public void HostSaveRoundTripsRoutineLocalsAndNestedEvaluationStack()
    {
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteByte(0x2200, 2)
            .WriteWord(0x2201, 0x1111)
            .WriteWord(0x2203, 0x2222);
        Z3ExecutionState state = CreateState(builder);
        state.CallRoutine(Z3Memory.PackAddress(0x2200), [0xAAAA, 0xBBBB], resultVariable: 16);
        state.WriteVariable(1, 0x1234);
        state.WriteVariable(2, 0x5678);
        state.PushStack(0xCAFE);
        state.SetProgramCounter(0x2210);
        byte[] saveFile = Z3SaveRestoreService.CreateHostSaveFile(state);

        state.WriteVariable(1, 0xFFFF);
        state.PopStack();
        state.PushStack(0xEEEE);
        state.SetProgramCounter(0x2220);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(state, saveFile);

        Assert.Equal(Z3RestoreStatus.ResumedAtProgramCounter, result.Status);
        Assert.Equal(0x2210, state.ProgramCounter);
        Assert.Equal(1, state.CallDepth);
        Assert.Equal(2, state.CurrentArgumentCount);
        Assert.Equal((ushort)0x1234, state.ReadVariableValue(1));
        Assert.Equal((ushort)0x5678, state.ReadVariableValue(2));
        Assert.Equal((ushort)0xCAFE, state.PopStack());
    }

    [Fact]
    public void RestorePreservesCurrentFlags2AndInterpreterFlags1()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder());
        state.Memory.WriteByte(Z3Memory.Flags2Address + 1, 0x01);
        state.Memory.SetInterpreterFlags1(0x30, 0x20);
        byte[] saveFile = Z3SaveRestoreService.CreateHostSaveFile(state);

        state.Memory.WriteByte(Z3Memory.Flags2Address + 1, 0x02);
        state.Memory.SetInterpreterFlags1(0x30, 0x10);
        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(state, saveFile);

        Assert.True(result.Succeeded);
        Assert.Equal((byte)0x10, (byte)(state.Memory.ReadByte(Z3Memory.Flags1Address) & 0x30));
        Assert.Equal((byte)0x02, (byte)(state.Memory.ReadByte(Z3Memory.Flags2Address + 1) & 0x03));
    }

    [Fact]
    public void RestoresUncompressedUMemFiles()
    {
        Z3ExecutionState source = CreateState(new SyntheticStoryBuilder());
        source.Memory.WriteWord(0x0200, 0x1234);
        QuetzalFile compressedFile = QuetzalFileCodec.Read(Z3SaveRestoreService.CreateHostSaveFile(source));
        byte[] currentDynamicMemory = source.Memory.GetCurrentDynamicMemory();
        QuetzalFile uncompressedFile = new(compressedFile.Chunks
            .Where(static chunk => chunk.Id != "CMem")
            .Append(QuetzalMemoryCodec.CreateUMemChunk(currentDynamicMemory)));
        byte[] encoded = QuetzalFileCodec.Write(uncompressedFile);
        Z3ExecutionState target = CreateState(new SyntheticStoryBuilder());
        target.Memory.WriteWord(0x0200, 0xABCD);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(target, encoded);

        Assert.True(result.Succeeded);
        Assert.Equal((ushort)0x1234, target.Memory.ReadWord(0x0200));
    }

    [Fact]
    public void SuccessfulSaveBranchWithReturnOffsetReturnsFromTheSavedRoutine()
    {
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteBytes(0x2200, [0, 0xB5, 0xC0]);
        Z3ExecutionState source = CreateState(builder);
        source.CallRoutine(Z3Memory.PackAddress(0x2200), [], resultVariable: 16);
        byte[] saveFile = Z3SaveRestoreService.CreateStorySaveFile(source, 0x2202);
        Z3ExecutionState target = CreateState(builder);
        target.CallRoutine(Z3Memory.PackAddress(0x2200), [], resultVariable: 16);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(target, saveFile);

        Assert.Equal(Z3RestoreStatus.ResumedSaveOpcode, result.Status);
        Assert.Equal(0, target.CallDepth);
        Assert.Equal(0x2000, target.ProgramCounter);
        Assert.Equal((ushort)0, target.Memory.ReadWord(0x0200));
    }

    [Fact]
    public void SuccessfulSaveBranchWithTwoByteOffsetResumesAtTheBranchTarget()
    {
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteBytes(0x2200, [0x80, 0x0A]);
        Z3ExecutionState source = CreateState(builder);
        byte[] saveFile = Z3SaveRestoreService.CreateStorySaveFile(source, 0x2200);
        Z3ExecutionState target = CreateState(builder);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(target, saveFile);

        Assert.Equal(Z3RestoreStatus.ResumedSaveOpcode, result.Status);
        Assert.Equal(0x220A, target.ProgramCounter);
    }

    [Fact]
    public void RejectsDifferentStoryWithoutChangingExecutionState()
    {
        Z3ExecutionState source = CreateState(new SyntheticStoryBuilder().WriteWord(0x02, 1));
        byte[] saveFile = Z3SaveRestoreService.CreateHostSaveFile(source);
        Z3ExecutionState target = CreateState(new SyntheticStoryBuilder().WriteWord(0x02, 2));
        target.SetProgramCounter(0x2010);
        target.Memory.WriteWord(0x0200, 0xABCD);
        target.PushStack(0x1234);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(target, saveFile);

        Assert.Equal(Z3RestoreStatus.Failed, result.Status);
        Assert.Contains("different story", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0x2010, target.ProgramCounter);
        Assert.Equal((ushort)0xABCD, target.Memory.ReadWord(0x0200));
        Assert.Equal((ushort)0x1234, target.PopStack());
    }

    [Fact]
    public void RejectsSerialNumberMismatchWithoutChangingExecutionState()
    {
        Z3ExecutionState source = CreateState(new SyntheticStoryBuilder());
        byte[] saveFile = Z3SaveRestoreService.CreateHostSaveFile(source);
        byte[] differentSerialImage = new SyntheticStoryBuilder().Build();
        differentSerialImage[0x12] = (byte)'9';
        Z3ExecutionState target = CreateState(differentSerialImage);
        target.SetProgramCounter(0x2010);
        target.Memory.WriteWord(0x0200, 0xABCD);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(target, saveFile);

        Assert.Equal(Z3RestoreStatus.Failed, result.Status);
        Assert.Equal(0x2010, target.ProgramCounter);
        Assert.Equal((ushort)0xABCD, target.Memory.ReadWord(0x0200));
    }

    [Fact]
    public void RejectsChecksumMismatchWithoutChangingExecutionState()
    {
        Z3ExecutionState source = CreateState(new SyntheticStoryBuilder());
        byte[] saveFile = Z3SaveRestoreService.CreateHostSaveFile(source);
        Z3ExecutionState target = CreateState(new SyntheticStoryBuilder().WriteByte(0x2050, 0xA5));
        target.SetProgramCounter(0x2010);
        target.Memory.WriteWord(0x0200, 0xABCD);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(target, saveFile);

        Assert.Equal(Z3RestoreStatus.Failed, result.Status);
        Assert.Equal(0x2010, target.ProgramCounter);
        Assert.Equal((ushort)0xABCD, target.Memory.ReadWord(0x0200));
    }

    [Theory]
    [InlineData("IFhd")]
    [InlineData("MEM ")]
    [InlineData("Stks")]
    public void RejectsFilesMissingRequiredChunksTransactionally(string missingChunk)
    {
        Z3ExecutionState source = CreateState(new SyntheticStoryBuilder());
        QuetzalFile validFile = QuetzalFileCodec.Read(Z3SaveRestoreService.CreateHostSaveFile(source));
        IEnumerable<QuetzalChunk> chunks = missingChunk == "MEM "
            ? validFile.Chunks.Where(static chunk => chunk.Id is not ("CMem" or "UMem"))
            : validFile.Chunks.Where(chunk => chunk.Id != missingChunk);
        byte[] malformed = QuetzalFileCodec.Write(new QuetzalFile(chunks));
        Z3ExecutionState target = CreateState(new SyntheticStoryBuilder());
        target.SetProgramCounter(0x2010);
        target.Memory.WriteWord(0x0200, 0xCAFE);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(target, malformed);

        Assert.Equal(Z3RestoreStatus.Failed, result.Status);
        Assert.Equal(0x2010, target.ProgramCounter);
        Assert.Equal((ushort)0xCAFE, target.Memory.ReadWord(0x0200));
    }

    [Fact]
    public void RejectsAnInvalidHostResumeMarkerWithoutChangingExecutionState()
    {
        Z3ExecutionState source = CreateState(new SyntheticStoryBuilder());
        QuetzalFile file = QuetzalFileCodec.Read(Z3SaveRestoreService.CreateHostSaveFile(source));
        QuetzalFile malformed = new(file.Chunks.Select(static chunk =>
            chunk.Id == "GGPT" ? new QuetzalChunk("GGPT", [0xFF]) : chunk));
        Z3ExecutionState target = CreateState(new SyntheticStoryBuilder());
        target.SetProgramCounter(0x2010);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(target, QuetzalFileCodec.Write(malformed));

        Assert.Equal(Z3RestoreStatus.Failed, result.Status);
        Assert.Equal(0x2010, target.ProgramCounter);
    }

    [Theory]
    [InlineData("CMem")]
    [InlineData("Stks")]
    public void RejectsMalformedSemanticChunkDataTransactionally(string malformedChunk)
    {
        Z3ExecutionState source = CreateState(new SyntheticStoryBuilder());
        QuetzalFile file = QuetzalFileCodec.Read(Z3SaveRestoreService.CreateHostSaveFile(source));
        QuetzalFile malformed = new(file.Chunks.Select(chunk => chunk.Id == malformedChunk
            ? new QuetzalChunk(malformedChunk, malformedChunk == "CMem" ? [0] : [])
            : chunk));
        Z3ExecutionState target = CreateState(new SyntheticStoryBuilder());
        target.SetProgramCounter(0x2010);
        target.Memory.WriteWord(0x0200, 0xCAFE);
        target.PushStack(0xBEEF);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(target, QuetzalFileCodec.Write(malformed));

        Assert.Equal(Z3RestoreStatus.Failed, result.Status);
        Assert.Equal(0x2010, target.ProgramCounter);
        Assert.Equal((ushort)0xCAFE, target.Memory.ReadWord(0x0200));
        Assert.Equal((ushort)0xBEEF, target.PopStack());
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 0, 0, 0, 12, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void FailedMalformedRestoreDoesNotChangeMemoryPcOrStacks(byte[] saveFile)
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder());
        state.SetProgramCounter(0x2010);
        state.Memory.WriteWord(0x0200, 0xCAFE);
        state.PushStack(0xBEEF);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(state, saveFile);

        Assert.False(result.Succeeded);
        Assert.Equal(0x2010, state.ProgramCounter);
        Assert.Equal((ushort)0xCAFE, state.Memory.ReadWord(0x0200));
        Assert.Equal((ushort)0xBEEF, state.PopStack());
    }

    [Fact]
    public void SaveUsesCalculatedChecksumWhenStoryHeaderOmitsIt()
    {
        byte[] legacyImage = new SyntheticStoryBuilder().Build();
        legacyImage[0x1A] = 0;
        legacyImage[0x1B] = 0;
        legacyImage[0x1C] = 0;
        legacyImage[0x1D] = 0;
        Z3ExecutionState state = CreateState(legacyImage);

        QuetzalFile file = QuetzalFileCodec.Read(Z3SaveRestoreService.CreateHostSaveFile(state));
        QuetzalIfhd identity = QuetzalIfhd.Read(Assert.Single(file.Chunks, static chunk => chunk.Id == "IFhd"));

        Assert.Equal(state.Memory.CalculateOriginalStoryChecksum(), identity.Checksum);
    }

    private static Z3ExecutionState CreateState(SyntheticStoryBuilder builder)
    {
        using MemoryStream stream = builder.BuildStream();
        return new Z3ExecutionState(new Z3StoryLoader().Load(stream));
    }

    private static Z3ExecutionState CreateState(byte[] image)
    {
        using MemoryStream stream = new(image, writable: false);
        return new Z3ExecutionState(new Z3StoryLoader().Load(stream));
    }
}
