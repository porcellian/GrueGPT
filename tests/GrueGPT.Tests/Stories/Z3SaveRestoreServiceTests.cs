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
